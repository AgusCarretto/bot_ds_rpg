using System.Data.Common;
using BotDsRpg.Data;
using BotDsRpg.GameData;
using Dapper;

namespace BotDsRpg.Repositories;

public sealed class BoxRepository(IDbConnectionFactory connectionFactory) : IBoxRepository
{
    public async Task<BoxDefinition?> GetDefinitionAsync(int boxItemId, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();

        var header = await connection.QuerySingleOrDefaultAsync<BoxHeaderRow>(new CommandDefinition(
            """
            SELECT b.box_item_id AS BoxItemId, i.name AS BoxName, b.rolls AS Rolls
            FROM boxes b JOIN items i ON i.item_id = b.box_item_id
            WHERE b.box_item_id = @BoxItemId;
            """,
            new { BoxItemId = boxItemId }, cancellationToken: cancellationToken));

        if (header is null)
        {
            return null;
        }

        var rows = await connection.QueryAsync<LootRow>(new CommandDefinition(
            """
            SELECT l.kind AS Kind, l.item_id AS ItemId, it.name AS ItemName, it.rarity AS ItemRarity, it.emoji AS ItemEmoji,
                   l.weight AS Weight, l.min_qty AS MinQty, l.max_qty AS MaxQty
            FROM box_loot l LEFT JOIN items it ON it.item_id = l.item_id
            WHERE l.box_item_id = @BoxItemId
            ORDER BY l.loot_id;
            """,
            new { BoxItemId = boxItemId }, cancellationToken: cancellationToken));

        var entries = rows
            .Select(r => new BoxLootEntry(
                r.Kind == "gold" ? LootKind.Gold : LootKind.Item, r.ItemId, r.ItemName, r.ItemRarity, r.ItemEmoji, r.Weight, r.MinQty, r.MaxQty))
            .ToList();

        return new BoxDefinition(header.BoxItemId, header.BoxName, header.Rolls, entries);
    }

    public async Task<BoxOpenOutcome?> OpenAsync(ulong discordId, int boxItemId, BoxLootResult loot, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Gastar la caja, con guarda: si ya no la tiene (otra apertura se la llevó un instante antes), no devuelve fila y no se
        // aplica NADA — la transacción se descarta al salir sin commit.
        int? left = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            """
            UPDATE inventory SET quantity = quantity - 1
            WHERE discord_id = @DiscordId AND item_id = @BoxItemId AND quantity >= 1
            RETURNING quantity;
            """,
            new { DiscordId = (long)discordId, BoxItemId = boxItemId }, transaction: transaction, cancellationToken: cancellationToken));

        if (left is null)
        {
            return null;
        }

        if (left == 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM inventory WHERE discord_id = @DiscordId AND item_id = @BoxItemId AND quantity = 0;",
                new { DiscordId = (long)discordId, BoxItemId = boxItemId }, transaction: transaction, cancellationToken: cancellationToken));
        }

        int goldAfter = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "UPDATE users SET gold = gold + @Gold WHERE discord_id = @DiscordId RETURNING gold;",
            new { Gold = loot.Gold, DiscordId = (long)discordId }, transaction: transaction, cancellationToken: cancellationToken));

        foreach (var item in loot.Items)
        {
            await InventoryUpsert.AddItemAsync(connection, transaction, discordId, item.ItemId, item.Quantity, cancellationToken);
        }

        // La colección de trofeos (materiales que ningún monstruo suelta, o sea los que solo salen de cajas): se anotan los que
        // el jugador no tenía NUNCA, en la misma transacción que el botín. Alimenta el logro Coleccionista (GameData/AchievementCatalog).
        var newTrophies = (await connection.QueryAsync<string>(new CommandDefinition(
            """
            WITH added AS (
                INSERT INTO player_collection (discord_id, item_id)
                SELECT @DiscordId, i.item_id
                FROM items i
                WHERE i.item_id = ANY(@ItemIds)
                  AND i.type = 'Material'
                  AND NOT EXISTS (SELECT 1 FROM monster_drops d WHERE d.item_id = i.item_id)
                ON CONFLICT DO NOTHING
                RETURNING item_id
            )
            SELECT i.name FROM added JOIN items i ON i.item_id = added.item_id ORDER BY i.name;
            """,
            new { DiscordId = (long)discordId, ItemIds = loot.Items.Select(i => i.ItemId).ToArray() },
            transaction: transaction, cancellationToken: cancellationToken))).ToList();

        await transaction.CommitAsync(cancellationToken);
        return new BoxOpenOutcome(goldAfter, newTrophies);
    }

    private sealed record BoxHeaderRow(int BoxItemId, string BoxName, int Rolls);

    private sealed record LootRow(
        string Kind, int? ItemId, string? ItemName, string? ItemRarity, string? ItemEmoji, int Weight, int MinQty, int MaxQty);
}
