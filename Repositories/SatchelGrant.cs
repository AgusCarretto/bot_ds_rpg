using System.Data.Common;
using BotDsRpg.GameData;
using Dapper;

namespace BotDsRpg.Repositories;

// Lo que da la bendición Alforja del Fogonero: Comidas para Mascotas y Cajones de Pino por nivel, al elegirla y cada vez que se renace (v0.12.0).
// Siempre va DENTRO de la transacción que lo dispara (elegir la bendición, o el reinicio), así que si algo falla no queda dado a medias.
internal static class SatchelGrant
{
    public static async Task<(int PetFood, int Boxes)> GrantAsync(
        DbConnection connection, DbTransaction transaction, ulong discordId, int level, CancellationToken cancellationToken)
    {
        if (level <= 0)
        {
            return (0, 0);
        }

        int petFood = BlessingCatalog.SatchelPetFoodPerLevel * level;
        int boxes = BlessingCatalog.SatchelBoxesPerLevel * level;

        await AddAsync(connection, transaction, discordId, PetRules.FoodItemName, petFood, cancellationToken);
        await AddAsync(connection, transaction, discordId, BlessingCatalog.SatchelBoxName, boxes, cancellationToken);
        return (petFood, boxes);
    }

    private static async Task AddAsync(
        DbConnection connection, DbTransaction transaction, ulong discordId, string itemName, int quantity, CancellationToken cancellationToken)
    {
        // Si el ítem no existe el SELECT no devuelve filas y no se inserta nada: se corta TODO en vez de marcar la bendición como pagada sin pagarla.
        int? total = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            """
            INSERT INTO inventory (discord_id, item_id, quantity)
            SELECT @DiscordId, item_id, @Quantity FROM items WHERE name = @ItemName
            ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = inventory.quantity + EXCLUDED.quantity
            RETURNING quantity;
            """,
            new { DiscordId = (long)discordId, ItemName = itemName, Quantity = quantity }, transaction: transaction, cancellationToken: cancellationToken));

        if (total is null)
        {
            throw new InvalidOperationException($"La Alforja del Fogonero nombra un ítem que no existe en el catálogo: \"{itemName}\".");
        }
    }
}
