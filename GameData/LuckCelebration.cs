namespace BotDsRpg.GameData;

// El festejo cuando sale algo raro en /chop, /mine y /fish (también en la versión avanzada) — v0.60.0, el dueño (2026-11-17): «cuando alguien saque algo legendario o mítico, que el texto
// sea como WOOOWWW TREMENDA SUERTE o cosas así… desde épico en adelante, cada vez más exagerado». Una línea al tope de la pantalla, elegida al azar entre varias de su rareza.
// Las cajas NO festejan a propósito: los drops de monstruo traen una rareza en la base que no significa nada (v0.15.1, ver BoxModule.BuildOpenedEmbed) y gritaría por un drop común.
// Las rarezas se comparan por su orden (RarityCatalog.RankOf), nunca por el texto mostrado.
public static class LuckCelebration
{
    private static readonly string[] Epic =
    [
        Loc.Mark("✨ ¡Uh, mirá eso! ¡Te salió algo **ÉPICO**! ✨"),
        Loc.Mark("✨ ¡Epa! ¡Un **ÉPICO**! Hoy la mano viene buena ✨"),
        Loc.Mark("💜 ¡Qué lindo! ¡Sacaste un **ÉPICO**! 💜"),
    ];

    private static readonly string[] Legendary =
    [
        Loc.Mark("🔥🔥 ¡¡WOOOOW!! ¡¡TREMENDA SUERTE!! ¡¡UN **LEGENDARIO**!! 🔥🔥"),
        Loc.Mark("🌟🌟 ¡¡NO PUEDE SER!! ¡¡SACASTE UN **LEGENDARIO**!! 🌟🌟"),
        Loc.Mark("💛💛 ¡¡PERO QUÉ SUERTE, LOCO!! ¡¡ES **LEGENDARIO**!! 💛💛"),
    ];

    private static readonly string[] Mythic =
    [
        Loc.Mark("🚨🚨🚨 ¡¡¡WOOOOOOOOWWWWWW!!! ¡¡¡ES UN **MÍTICO**!!! ¡¡¡SUERTE DE OTRO PLANETA, TREMENDOOOO!!! 🚨🚨🚨"),
        Loc.Mark("🎰🎰🎰 ¡¡¡NOOOOOO, UN **MÍTICO**!!! ¡¡¡CORRÉ A LA RULETA Y TIRÁ TODO AL ROJO, HOY NO PERDÉS!!! 🎰🎰🎰"),
        Loc.Mark("🤯🤯🤯 ¡¡¡QUÉÉÉÉÉÉ!!! ¡¡¡UN **MÍTICO**!!! ¡¡¡ANDÁ A JUGAR A LA QUINIELA YA MISMO!!! 🤯🤯🤯"),
        Loc.Mark("🌋🌋🌋 ¡¡¡SE PRENDIÓ FUEGO EL FOGÓN!!! ¡¡¡**MÍTICO**!!! ¡¡¡TREMENDA SUERTE, NO LO PUEDO CREER!!! 🌋🌋🌋"),
    ];

    // Las variantes de una rareza (vacío por debajo de Épico). Público para la prueba.
    public static IReadOnlyList<string> VariantsFor(string? rarity)
    {
        int rank = RarityCatalog.RankOf(rarity ?? string.Empty);
        if (rank >= RarityCatalog.RankOf("Mítico"))
        {
            return Mythic;
        }

        if (rank >= RarityCatalog.RankOf("Legendario"))
        {
            return Legendary;
        }

        return rank >= RarityCatalog.RankOf("Épico") ? Epic : [];
    }

    // La línea de festejo, en el idioma de quien la ve, o null si la rareza no da para festejar.
    public static string? Line(string? rarity, Random? random = null)
    {
        var variants = VariantsFor(rarity);
        return variants.Count == 0 ? null : Loc.Tr(variants[(random ?? Random.Shared).Next(variants.Count)]);
    }
}
