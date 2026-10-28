using BotDsRpg.GameData;

namespace BotDsRpg.Services;

// La caja que puede caer al ganar un /travel (v0.36.0). El viaje ya no suelta ningún material: paga oro y EXP (×30 una cacería) y, con una chance BAJA que sale de la fila "travel" de zone_boxes
// (Database/remove_travel_drops.sql), la caja chica de la zona. Lo comparten el combate solitario y la pantalla de /drops.
public static class TravelBox
{
    // La chance base (en %) y el nombre de la caja de los viajes de esa zona. (0, null) si la zona no tiene la fila o si falla la lectura: una victoria NUNCA se rompe por esto
    // (se loguea y esa vez no cae caja).
    public static async Task<(int ChancePercent, string? BoxName)> ChanceAsync(IZoneBoxService zoneBoxes, int zoneId)
    {
        try
        {
            var row = (await zoneBoxes.GetAsync()).ForZone(zoneId, ZoneBoxRole.Travel);
            return row is null ? (0, null) : (row.ChancePercent, row.BoxName);
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
            return (0, null);
        }
    }
}
