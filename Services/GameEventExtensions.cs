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
    }
}

// Lo que se registra al recolectar (/chop y /mine, por slash y por texto): el éxito y las unidades que dio.
public static class GatheringEvents
{
    public static async Task RecordAsync(IGameEvents events, ulong discordId, CooldownDefinition definition, Item item, int quantity)
    {
        await events.RecordAsync(discordId, definition.CommandName == "chop" ? GameEventKinds.Chop : GameEventKinds.Mine, detail: item.Name);
        await events.RecordAsync(discordId, GameEventKinds.GatheredUnits, amount: quantity, detail: item.Name);
    }
}
