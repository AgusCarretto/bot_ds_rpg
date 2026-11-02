namespace BotDsRpg.GameData.Dungeons;

// Lo que se puede ESCRIBIR en una mazmorra («aa d agua», «aa d b3», «aa d probar»), para cuando un botón no responde: cada palabra se traduce a la acción del botón. Español e inglés.
public static class DungeonActions
{
    public static string? Parse(DungeonKind kind, string? text)
    {
        string word = Normalize(text);
        if (word.Length == 0)
        {
            return null;
        }

        return kind switch
        {
            DungeonKind.FirePattern => word switch
            {
                "listo" or "ready" or "ok" or "ya" => FirePattern.Ready,
                "agua" or "water" or "w" => FirePattern.Water,
                "piedra" or "stone" or "rock" or "s" => FirePattern.Stone,
                "fuego" or "fire" or "f" => FirePattern.Fire,
                _ => null,
            },
            DungeonKind.SmokeMaze => word switch
            {
                "arriba" or "up" or "u" or "norte" or "n" => SmokeMaze.Up,
                "abajo" or "down" or "d" or "sur" => SmokeMaze.Down,
                "izquierda" or "izq" or "left" or "l" or "oeste" or "o" => SmokeMaze.Left,
                "derecha" or "der" or "right" or "r" or "este" or "e" => SmokeMaze.Right,
                _ => null,
            },
            DungeonKind.Embers => word.Length == 2 && word[0] is >= 'a' and <= 'd' && word[1] is >= '1' and <= '4'
                ? $"e{((word[1] - '1') * Embers.Size) + (word[0] - 'a')}"
                : null,
            DungeonKind.Lock => word switch
            {
                "borrar" or "del" or "delete" or "back" => FireLock.Delete,
                "probar" or "try" or "abrir" or "open" => FireLock.Try,
                "fuego" or "fire" => "s0",
                "agua" or "water" => "s1",
                "piedra" or "stone" or "rock" => "s2",
                "hoja" or "planta" or "leaf" or "plant" => "s3",
                "rayo" or "lightning" or "bolt" => "s4",
                _ => null,
            },
            _ => word switch
            {
                "llamarada" or "fuego" or "flame" or "fire" or "f" => Rhythm.Flame,
                "hachazo" or "hacha" or "axe" or "a" => Rhythm.Axe,
                "rafaga" or "viento" or "gust" or "wind" or "g" => Rhythm.Gust,
                _ => null,
            },
        };
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposed.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }
}
