using BotDsRpg.GameData;
using BotDsRpg.Models;

namespace BotDsRpg.Services;

// Atajos para los eventos que se repiten en muchos comandos, así cada uno registra lo mismo de la misma manera.
public static class GameEventExtensions
{
    // Una victoria o una recompensa que da XP: registra el evento ("hunt_win", "daily_claim"...) y, si además subió de
    // nivel, el "level_up" con el nivel nuevo. La zona sale del propio jugador, que ya viene resuelto en el resultado.
    public static async Task RecordVictoryAsync(this IGameEvents events, ulong discordId, string kind, LevelUpOutcome outcome)
    {
        await events.RecordAsync(discordId, kind, outcome.Player.CurrentZoneId);

        // Cada victoria de combate (cacería, viaje, jefe, raid, /autohunt) también es un enemigo vencido: es el contador del logro Exterminador.
        if (GameEventKinds.IsEnemyWin(kind))
        {
            await events.RecordAsync(discordId, GameEventKinds.EnemyDefeated, outcome.Player.CurrentZoneId);
        }

        if (outcome.LevelsGained > 0)
        {
            await events.RecordAsync(
                discordId, GameEventKinds.LevelUp, outcome.Player.CurrentZoneId, outcome.LevelsGained, outcome.Player.Level.ToString());
        }

        // Ganarle al Asador Eterno (El Fogón Eterno, v0.11.0): es el contador del logro Asador.
        if (outcome.Gate == GateEvent.Cleared)
        {
            await events.RecordAsync(discordId, GameEventKinds.GateWin, outcome.Player.CurrentZoneId);
        }
    }
}

// Lo que se registra al recolectar (/chop y /mine, por slash y por texto): el éxito y las unidades que dio.
public static class GatheringEvents
{
    public static Task RecordAsync(IGameEvents events, ulong discordId, CooldownDefinition definition, Item item, int quantity) =>
        RecordAsync(events, discordId, definition, [(item, quantity)], rolls: 1);

    // rolls: cuántos usos cuenta esta recolección (1 la normal, 4 la avanzada de los oficios): el contador de /chop o /mine sube eso, y de ese contador sale la XP del oficio
    // (GameData/ProfessionRules.cs). Las unidades son la suma de todo lo que dio.
    public static async Task RecordAsync(IGameEvents events, ulong discordId, CooldownDefinition definition, IReadOnlyList<(Item Item, int Quantity)> drops, int rolls)
    {
        string names = string.Join(", ", drops.Select(d => d.Item.Name).Distinct());
        bool isChop = definition.CommandName == "chop" || definition.CommandName == "chop_adv";
        await events.RecordAsync(discordId, isChop ? GameEventKinds.Chop : GameEventKinds.Mine, amount: rolls, detail: names);
        await events.RecordAsync(discordId, GameEventKinds.GatheredUnits, amount: drops.Sum(d => d.Quantity), detail: names);
    }
}
