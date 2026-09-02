namespace BotDsRpg.GameData;

public static class ProgressBar
{
    // Barra de texto tipo "████████░░" para mostrar progreso (XP, HP) en los embeds.
    public static string Render(int current, int max, int segments = 10)
    {
        if (max <= 0)
        {
            return new string('░', segments);
        }

        double ratio = Math.Clamp((double)current / max, 0, 1);
        int filled = (int)Math.Round(segments * ratio);
        return new string('█', filled) + new string('░', segments - filled);
    }
}
