using BotDsRpg.GameData;
using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class DustRepository(IDbConnectionFactory connectionFactory) : IDustRepository
{
    public async Task<DustSpendOutcome> BuyWithDustAsync(ulong discordId, int itemId, int quantity, int dustCost, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // La guarda de siempre: si ya no tiene el Polvo, el WHERE no deja actualizar y no vuelve fila.
            int? dustAfter = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                "UPDATE users SET dust = dust - @Cost WHERE discord_id = @DiscordId AND dust >= @Cost RETURNING dust;",
                new { DiscordId = (long)discordId, Cost = dustCost }, transaction: transaction, cancellationToken: cancellationToken));

            if (dustAfter is null)
            {
                bool exists = await connection.QuerySingleAsync<bool>(new CommandDefinition(
                    "SELECT EXISTS (SELECT 1 FROM users WHERE discord_id = @DiscordId);",
                    new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));
                await transaction.RollbackAsync(cancellationToken);
                return new DustSpendOutcome(exists ? DustSpendStatus.NotEnoughDust : DustSpendStatus.NoAccount, 0);
            }

            await InventoryUpsert.AddItemAsync(connection, transaction, discordId, itemId, quantity, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new DustSpendOutcome(DustSpendStatus.Ok, dustAfter.Value);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<int> GetQuantityAsync(ulong discordId, int itemId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT COALESCE(SUM(quantity), 0)::int FROM inventory WHERE discord_id = @DiscordId AND item_id = @ItemId;",
            new { DiscordId = (long)discordId, ItemId = itemId }, cancellationToken: cancellationToken));
    }

    public async Task<DismantleOutcome?> DismantleAsync(ulong discordId, int itemId, int quantity, int dust, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // La guarda de siempre (mismo patrón que InventoryRepository.TryConsumeAsync): si ya no tiene esa cantidad, el WHERE no deja actualizar,
            // no vuelve fila y se revierte todo.
            int? remaining = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                """
                UPDATE inventory
                SET quantity = quantity - @Quantity
                WHERE discord_id = @DiscordId AND item_id = @ItemId AND quantity >= @Quantity
                RETURNING quantity;
                """,
                new { DiscordId = (long)discordId, ItemId = itemId, Quantity = quantity }, transaction: transaction, cancellationToken: cancellationToken));

            if (remaining is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM inventory WHERE discord_id = @DiscordId AND quantity <= 0;",
                new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            int dustAfter = await connection.QuerySingleAsync<int>(new CommandDefinition(
                "UPDATE users SET dust = dust + @Dust WHERE discord_id = @DiscordId RETURNING dust;",
                new { DiscordId = (long)discordId, Dust = dust }, transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new DismantleOutcome(dustAfter, remaining.Value);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<EnchantOutcome> TryEnchantAsync(
        ulong discordId, string slot, int rolledTier, int goldCost, int dustCost, string? kind = null, bool allowSwitch = false, CancellationToken cancellationToken = default)
    {
        bool weapon = slot == "weapon";
        if (!weapon && slot != "amulet")
        {
            throw new ArgumentException("slot tiene que ser \"weapon\" o \"amulet\".", nameof(slot));
        }

        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // FOR UPDATE: la fila del jugador queda tomada hasta el final, así que dos intentos simultáneos se hacen uno detrás del otro y cada uno mira el oro,
            // el Polvo y el tier que de verdad hay en ese momento.
            var player = await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
                $"SELECT {UserSql.SelectColumns} FROM users WHERE discord_id = @DiscordId FOR UPDATE;",
                new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            if (player is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new EnchantOutcome(EnchantStatus.NoPlayer, 0, rolledTier, 0, null, null, null);
            }

            int previous = weapon ? player.WeaponEnchant : player.AmuletEnchant;
            string previousKind = weapon ? player.WeaponEnchantKind : player.AmuletEnchantKind;

            // El efecto (v0.22.0): sin elegir queda el que tiene la pieza; uno de la OTRA pieza es un error de programación (el módulo ya lo validó).
            string chosenKind = kind ?? previousKind;
            if (Enchantments.KindsFor(slot).All(k => k.Key != chosenKind))
            {
                throw new ArgumentException($"El efecto \"{chosenKind}\" no existe para {slot}.", nameof(kind));
            }

            bool switching = chosenKind != previousKind;
            bool hasGear = (weapon ? player.WeaponId : player.AmuletId) is not null;
            EnchantStatus? rejected =
                !hasGear ? EnchantStatus.NoGear :
                player.Gold < goldCost ? EnchantStatus.NotEnoughGold :
                player.Dust < dustCost ? EnchantStatus.NotEnoughDust : null;

            if (rejected is { } status)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new EnchantOutcome(status, previous, rolledTier, previous, null, previousKind, previousKind);
            }

            // Cambiar de efecto REEMPLAZA el que tenía la pieza (con el tier que salga, aunque sea menor): si ya tenía un tier y nadie confirmó, no se cobra nada.
            if (switching && previous > 0 && !allowSwitch)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new EnchantOutcome(EnchantStatus.NeedsConfirm, previous, rolledTier, previous, null, previousKind, chosenKind);
            }

            // Mismo efecto: el tier solo sube. Efecto nuevo: queda el que salió.
            int newTier = switching ? rolledTier : Math.Max(previous, rolledTier);

            // El nombre de la columna es fijo (nunca input de usuario): seguro interpolarlo.
            var updated = await connection.QuerySingleAsync<User>(new CommandDefinition(
                $"""
                UPDATE users
                SET gold = gold - @Gold, dust = dust - @Dust, {(weapon ? "weapon_enchant" : "amulet_enchant")} = @NewTier, {(weapon ? "weapon_enchant_kind" : "amulet_enchant_kind")} = @NewKind
                WHERE discord_id = @DiscordId
                RETURNING {UserSql.SelectColumns};
                """,
                new { DiscordId = (long)discordId, Gold = goldCost, Dust = dustCost, NewTier = newTier, NewKind = chosenKind }, transaction: transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return new EnchantOutcome(EnchantStatus.Ok, previous, rolledTier, newTier, updated, previousKind, chosenKind);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
