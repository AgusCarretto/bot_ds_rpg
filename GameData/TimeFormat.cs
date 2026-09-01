namespace BotDsRpg.GameData;

public static class TimeFormat
{
    // "Xm Ys" (o solo "Ys" si dura menos de un minuto). Compartido por los embeds
    // de cooldown de /hunt, /travel, /chop, /mine y /cd para que el formato sea consistente.
    public static string Remaining(TimeSpan remaining) =>
        remaining.TotalMinutes >= 1
            ? $"{(int)remaining.TotalMinutes}m {remaining.Seconds}s"
            : $"{remaining.Seconds}s";
}
