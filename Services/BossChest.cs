using BotDsRpg.GameData;
using BotDsRpg.Models;

namespace BotDsRpg.Services;

// El cofre de un jefe al ganar (v0.14.0). La PRIMERA vez de cada vuelta que un jugador vence a ese jefe cae SIEMPRE el cofre de la zona (su fila de monster_drops); las
// repeticiones dan la caja de zone_boxes (rol "repeat") con la chance que diga esa fila: una caja más chica, casi siempre, en vez de "40 % del cofre grande". Lo comparten
// el combate solitario, el raid y la pantalla de /drops.
public static class BossChest
{
    // ¿Es la primera vez de esta vuelta? En las zonas de la escalera se mira highest_zone_cleared de ANTES de aplicar la victoria (el que sube); en el Fogón Eterno (zona 0), gate_cleared
    // (que vuelve a false con cada Fuego Nuevo). Un jugador sin cuenta cuenta como primera vez (no hay nada que mirar).
    public static bool IsFirstClear(User? player, int bossZoneId) =>
        player is null || (bossZoneId == FogonRules.GateZoneId ? !player.GateCleared : player.HighestZoneCleared < bossZoneId);

    // La chance (en %) y el nombre de la caja de las repeticiones de ese jefe. (0, null) si la zona no tiene la fila o si falla la lectura: una victoria NUNCA se rompe por esto
    // (se loguea y esa vez no cae caja de repetición).
    public static async Task<(int ChancePercent, string? BoxName)> RepeatAsync(IZoneBoxService zoneBoxes, int bossZoneId)
    {
        try
        {
            var row = (await zoneBoxes.GetAsync()).ForZone(bossZoneId, ZoneBoxRole.Repeat);
            return row is null ? (0, null) : (row.ChancePercent, row.BoxName);
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
            return (0, null);
        }
    }
}
