namespace BotDsRpg.GameData;

// Las misiones diarias y semanales se reinician a medianoche HORA DE URUGUAY (lunes 00:00 las semanales). Uruguay no usa
// horario de verano desde 2015: es UTC-3 todo el año, así que alcanza con un desplazamiento fijo. A propósito NO se usa la base
// de zonas horarias del sistema (TimeZoneInfo): el identificador cambia entre Windows y Linux, y en una imagen mínima de Docker
// puede ni estar. Si Uruguay volviera al horario de verano, este es el único archivo para tocar.
//
// Todas las fechas que entran y salen son UTC (lo que guarda la base); solo el corte del día es "de Uruguay".
public static class UruguayCalendar
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(-3);

    // El instante UTC en que empezó el día de Uruguay al que pertenece "utcNow" (la medianoche local, 03:00 UTC).
    public static DateTime DayStartUtc(DateTime utcNow)
    {
        var localDate = ToLocal(utcNow).Date;
        return DateTime.SpecifyKind(localDate - Offset, DateTimeKind.Utc);
    }

    public static DateTime NextDayStartUtc(DateTime utcNow) => DayStartUtc(utcNow).AddDays(1);

    // El lunes 00:00 (hora de Uruguay) de la semana a la que pertenece "utcNow", en UTC.
    public static DateTime WeekStartUtc(DateTime utcNow)
    {
        var localDate = ToLocal(utcNow).Date;
        int daysSinceMonday = ((int)localDate.DayOfWeek + 6) % 7; // Domingo = 0 en .NET: el lunes es el primer día de la semana
        return DateTime.SpecifyKind(localDate.AddDays(-daysSinceMonday) - Offset, DateTimeKind.Utc);
    }

    public static DateTime NextWeekStartUtc(DateTime utcNow) => WeekStartUtc(utcNow).AddDays(7);

    // La hora de pared de Uruguay para un instante UTC (Kind sin especificar: es una fecha "local", no un instante).
    public static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Unspecified) + Offset;

    // "5 h 12 min", "2 d 4 h", "38 min": cuánto falta, para decir cuándo se reinician las misiones.
    public static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return "un instante";
        }

        if (remaining.TotalDays >= 1)
        {
            return $"{(int)remaining.TotalDays} d {remaining.Hours} h";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"{(int)remaining.TotalHours} h {remaining.Minutes} min";
        }

        return $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))} min";
    }
}
