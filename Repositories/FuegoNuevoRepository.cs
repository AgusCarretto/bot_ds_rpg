using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class FuegoNuevoRepository(IDbConnectionFactory connectionFactory) : IFuegoNuevoRepository
{
    public async Task<RenewPreview?> GetPreviewAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        using DbConnection connection = connectionFactory.CreateConnection();

        var user = await connection.QuerySingleOrDefaultAsync<PreviewRow>(new CommandDefinition(
            """
            SELECT u.class AS "Class", u.level AS "Level", u.fuego_nuevo AS "FuegoNuevo", u.gate_cleared AS "GateCleared",
                   u.gold AS "Gold", u.bank_gold AS "BankGold", u.has_bank AS "HasBank", u.dust AS "Dust",
                   w.name AS "WeaponName", u.weapon_enchant AS "WeaponEnchant", a.name AS "AmuletName", u.amulet_enchant AS "AmuletEnchant",
                   (SELECT count(*) FROM player_pets p WHERE p.discord_id = u.discord_id)::int AS "Pets",
                   (SELECT count(*) FROM player_blessings b WHERE b.discord_id = u.discord_id)::int AS "Blessings"
            FROM users u
            LEFT JOIN items w ON w.item_id = u.weapon_id
            LEFT JOIN items a ON a.item_id = u.amulet_id
            WHERE u.discord_id = @DiscordId;
            """,
            new { DiscordId = (long)discordId }, cancellationToken: cancellationToken));

        if (user is null)
        {
            return null;
        }

        var units = await connection.QueryAsync<UnitsRow>(new CommandDefinition(
            """
            SELECT i.type AS "Type", SUM(inv.quantity)::bigint AS "Units"
            FROM inventory inv
            JOIN items i ON i.item_id = inv.item_id
            WHERE inv.discord_id = @DiscordId AND inv.quantity > 0
            GROUP BY i.type;
            """,
            new { DiscordId = (long)discordId }, cancellationToken: cancellationToken));

        var lost = new Dictionary<string, long>(StringComparer.Ordinal);
        long eggs = 0, petFood = 0;
        foreach (var row in units)
        {
            if (row.Type == FuegoNuevoRules.KeptItemTypes[0])
            {
                eggs += row.Units;
            }
            else if (row.Type == FuegoNuevoRules.KeptItemTypes[1])
            {
                petFood += row.Units;
            }
            else
            {
                lost[row.Type] = row.Units;
            }
        }

        return new RenewPreview(
            user.Class, user.Level, user.FuegoNuevo, user.GateCleared, user.Gold, user.BankGold, user.HasBank, user.Dust,
            user.WeaponName, user.WeaponEnchant, user.AmuletName, user.AmuletEnchant, lost, eggs, petFood, user.Pets, user.Blessings);
    }

    public async Task<RenewOutcome> RenewAsync(ulong discordId, int expectedCount, string newClass, CancellationToken cancellationToken = default)
    {
        if (ClassCatalog.All.All(c => c.Name != newClass))
        {
            return new RenewOutcome(RenewStatus.InvalidClass, 0, null, 0, null, 0, 0);
        }

        using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // La fila del jugador queda tomada hasta el final: dos reinicios a la vez se hacen uno detrás del otro, y el segundo ya ve el contador nuevo y gate_cleared en falso.
            var player = await connection.QuerySingleOrDefaultAsync<LockedRow>(new CommandDefinition(
                """
                SELECT class AS "Class", level AS "Level", fuego_nuevo AS "FuegoNuevo", gate_cleared AS "GateCleared", run_started_at AS "RunStartedAt"
                FROM users WHERE discord_id = @DiscordId FOR UPDATE;
                """,
                new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            if (player is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new RenewOutcome(RenewStatus.NoAccount, 0, null, 0, null, 0, 0);
            }

            if (!player.GateCleared)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new RenewOutcome(RenewStatus.NotCleared, player.FuegoNuevo, player.Class, player.Level, null, 0, 0);
            }

            if (player.FuegoNuevo != expectedCount)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new RenewOutcome(RenewStatus.StaleCount, player.FuegoNuevo, player.Class, player.Level, null, 0, 0);
            }

            // La mochila: se va todo menos los huevos y la comida de las mascotas (FuegoNuevoRules.KeptItemTypes). Sin reembolso: lo dijo el dueño («nada de polvo ni nada»).
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM inventory WHERE discord_id = @DiscordId AND item_id IN (SELECT item_id FROM items WHERE type <> @KeptEgg AND type <> @KeptFood);",
                new { DiscordId = (long)discordId, KeptEgg = FuegoNuevoRules.KeptItemTypes[0], KeptFood = FuegoNuevoRules.KeptItemTypes[1] },
                transaction: transaction, cancellationToken: cancellationToken));

            // Todo lo que empieza de cero usa el DEFAULT de la columna (nivel 1, 0 EXP, la vida de un jugador nuevo...) en vez de repetir los números acá: si el esquema cambia, el
            // reinicio sigue dejando al jugador igual que un /start. El oro, el banco, la racha diaria y las mascotas NO se tocan.
            int newNumber = await connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                UPDATE users SET
                    class = @NewClass,
                    level = DEFAULT, xp = DEFAULT, max_hp = DEFAULT, current_hp = DEFAULT,
                    weapon_id = NULL, amulet_id = NULL, weapon_enchant = 0, amulet_enchant = 0,
                    dust = 0,
                    current_zone_id = (SELECT zone_id FROM zones WHERE kind = 'normal' ORDER BY min_level, zone_id LIMIT 1),
                    highest_zone_cleared = 0,
                    in_gate = false, gate_cleared = false,
                    fuego_nuevo = fuego_nuevo + 1,
                    run_started_at = now()
                WHERE discord_id = @DiscordId
                RETURNING fuego_nuevo;
                """,
                new { DiscordId = (long)discordId, NewClass = newClass }, transaction: transaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM player_buffs WHERE discord_id = @DiscordId;",
                new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

            // Los cooldowns de la vuelta (cacería, viaje, jefe, talar, minar) arrancan limpios; el de comprar cajas es de la economía, no de la vuelta, y se queda.
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM cooldowns WHERE discord_id = @DiscordId AND command_name <> @BoxBuy;",
                new { DiscordId = (long)discordId, BoxBuy = CooldownCatalog.BoxBuy.CommandName }, transaction: transaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO fuego_nuevo_history (discord_id, number, class_before, class_after, level_before, started_at)
                VALUES (@DiscordId, @Number, @ClassBefore, @ClassAfter, @LevelBefore, @StartedAt);
                """,
                new
                {
                    DiscordId = (long)discordId, Number = newNumber, ClassBefore = player.Class, ClassAfter = newClass,
                    LevelBefore = player.Level, StartedAt = player.RunStartedAt,
                },
                transaction: transaction, cancellationToken: cancellationToken));

            // La oferta de bendiciones de ESTE Fuego Nuevo: 3 sorteadas entre las que todavía no están al máximo. Queda guardada hasta que elija.
            var levelRows = await connection.QueryAsync<LevelRow>(new CommandDefinition(
                "SELECT blessing_key AS \"Key\", level AS \"Level\" FROM player_blessings WHERE discord_id = @DiscordId;",
                new { DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));
            var levels = levelRows.ToDictionary(row => row.Key, row => row.Level, StringComparer.Ordinal);

            var keys = BlessingCatalog.RollOffer(levels, Random.Shared);
            BlessingOffer? offer = null;
            if (keys.Count > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO blessing_offers (discord_id, fuego_nuevo_no, offered_keys) VALUES (@DiscordId, @Number, @Keys);",
                    new { DiscordId = (long)discordId, Number = newNumber, Keys = string.Join(',', keys) }, transaction: transaction, cancellationToken: cancellationToken));
                offer = new BlessingOffer(newNumber, keys);
            }

            // La Alforja del Fogonero paga también al renacer (no solo al elegirla).
            var (petFood, boxes) = levels.TryGetValue(BlessingCatalog.SatchelKey, out int satchelLevel)
                ? await SatchelGrant.GrantAsync(connection, transaction, discordId, satchelLevel, cancellationToken)
                : (0, 0);

            await transaction.CommitAsync(cancellationToken);
            return new RenewOutcome(RenewStatus.Ok, newNumber, player.Class, player.Level, offer, petFood, boxes);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private sealed record LockedRow(string Class, int Level, int FuegoNuevo, bool GateCleared, DateTime RunStartedAt);

    private sealed record LevelRow(string Key, int Level);

    private sealed record UnitsRow(string Type, long Units);

    private sealed record PreviewRow(
        string Class, int Level, int FuegoNuevo, bool GateCleared, int Gold, int BankGold, bool HasBank, int Dust,
        string? WeaponName, int WeaponEnchant, string? AmuletName, int AmuletEnchant, int Pets, int Blessings);
}
