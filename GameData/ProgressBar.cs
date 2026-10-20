using System.Text;

namespace BotDsRpg.GameData;

public static class ProgressBar
{
    // Una barra con emojis no pasa de acá: cada pieza son ~30-45 caracteres de código y un campo de embed admite 1024.
    public const int MaxEmojiSegments = 10;

    // Barra de texto tipo "████████░░" para mostrar progreso (XP, HP) en los embeds.
    //
    // kind (UiEmojis.BarHp / BarXp / BarPesar): si ese tipo de barra tiene sus piezas subidas como emojis propios (GameData/UiEmojis.cs), la barra sale con ellos; si no, la de texto de siempre.
    // Usar kind SOLO en campos de ancho completo: una barra de emojis son ~240 px y en una columna de un tercio del ancho (campo inline) se corta en dos líneas.
    // Sin kind (o con menos de 2 tramos) es siempre la de texto: el combate, las misiones y el plantel del raid siguen como estaban.
    public static string Render(int current, int max, int segments = 10, string? kind = null)
    {
        double ratio = max <= 0 ? 0 : Math.Clamp((double)current / max, 0, 1);

        if (kind is not null && segments >= 2 && UiEmojis.Bar(kind) is { } pieces)
        {
            int count = Math.Min(segments, MaxEmojiSegments);
            int full = (int)Math.Round(count * ratio);
            var bar = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                bool filled = i < full;
                bar.Append(i == 0
                    ? (filled ? pieces.LeftFull : pieces.LeftEmpty)
                    : i == count - 1
                        ? (filled ? pieces.RightFull : pieces.RightEmpty)
                        : (filled ? pieces.MidFull : pieces.MidEmpty));
            }

            return bar.ToString();
        }

        if (max <= 0)
        {
            return new string('░', segments);
        }

        int filledSegments = (int)Math.Round(segments * ratio);
        return new string('█', filledSegments) + new string('░', segments - filledSegments);
    }
}
