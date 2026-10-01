namespace BotDsRpg.GameData;

public static class TimeFormat
{
    // Cuánto falta, con las dos o tres unidades más grandes: "2d 3h 5m", "1h 5m 10s", "4m 20s" o "35s" (nunca "0s": si falta algo, al
    // menos "1s"). Compartido por los embeds de cooldown de /hunt, /travel, /chop, /mine, /daily, la compra de cajas y /cd, para que el
    // formato sea consistente — antes todo se mostraba en minutos y un cooldown de horas decía "1440m 0s".
    public static string Remaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return "0s";
        }

        // Se redondea para arriba al segundo: un cooldown de 0,4 segundos no es "0s".
        var span = TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds));

        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m";
        }

        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m {span.Seconds}s";
        }

        return span.TotalMinutes >= 1
            ? $"{(int)span.TotalMinutes}m {span.Seconds}s"
            : $"{span.Seconds}s";
    }
}
