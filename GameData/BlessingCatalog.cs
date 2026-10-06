using System.Globalization;

namespace BotDsRpg.GameData;

public enum BlessingKind { Speed, Power, Pets, Economy, Items, Cosmetic }

// Una bendición: la clave (en la base), su nombre, su ícono, de qué tipo es y, en palabras, lo que da cada nivel.
public sealed record BlessingDefinition(string Key, string Name, string Emoji, BlessingKind Kind, string PerLevel);

// Las bendiciones (v0.12.0): una por Fuego Nuevo, elegida entre 3 sorteadas. Se repiten: si volvés a elegir una que ya tenés sube de nivel (I a V), así con ~13 bendiciones alcanzan
// muchas vueltas. Variadas a pedido del dueño: velocidad (más drop, más cantidad, más EXP), poder CHICO y con tope (ataque, defensa, vida: como mucho +5 %, +5 % y +10 % al nivel V),
// mascotas, oro, ítems y cosmética. Nada de lo permanente cuenta en duelos ni en la Arena, y ninguna toca cooldowns (todo está calibrado por minuto).
// Los números de cada nivel están acá, con nombre, y PlayerBonuses los lee (así la ayuda y el juego no se desincronizan). La oferta y el nivel de cada jugador viven en la base
// (blessing_offers, player_blessings); la definición no.
public static class BlessingCatalog
{
    public const int MaxLevel = 5;
    public const int OfferSize = 3;

    // Claves (las que se guardan en player_blessings.blessing_key).
    public const string ChopKey = "mano_lenador";
    public const string MineKey = "pico_fino";
    public const string TravelDropKey = "brasa_vieja";
    public const string HuntDropKey = "ojo_de_halcon";
    public const string XpKey = "aprendiz";
    public const string AttackKey = "filo_antiguo";
    public const string DefenseKey = "piel_curtida";
    public const string HpKey = "corazon_de_brasa";
    public const string PetsKey = "manada";
    public const string PetFeedKey = "buen_pienso";
    public const string GoldKey = "bolsillo_hondo";
    public const string SatchelKey = "alforja_del_fogonero";
    public const string ColorKey = "brasa_de_color";

    // Lo que da CADA NIVEL.
    public const double ChopPerLevel = 0.05;          // +5 % de cantidad en /chop
    public const double MinePerLevel = 0.05;          // +5 % de cantidad en /mine
    public const double TravelDropPerLevel = 0.05;    // +5 % relativo a la chance de /travel
    public const double HuntDropPerLevel = 0.05;      // +5 % relativo a la chance de /hunt
    public const double XpPerLevel = 0.04;            // +4 % de EXP en las peleas
    public const double AttackPerLevel = 0.01;        // +1 % del ataque total (solo contra monstruos)
    public const double DefensePerLevel = 0.01;       // +1 % de la defensa total (solo contra monstruos)
    public const double HpPerLevel = 0.02;            // +2 % de la vida máxima (solo contra monstruos)
    public const double PetsPerLevel = 0.10;          // +10 % al bonus de todas las mascotas
    public const double GoldPerLevel = 0.03;          // +3 % de oro en las peleas
    public static readonly TimeSpan PetFeedReductionPerLevel = TimeSpan.FromMinutes(5); // -5 min al cooldown de alimentar a cada mascota
    public const int SatchelPetFoodPerLevel = 3;      // Alforja: Comidas para Mascotas por nivel...
    public const int SatchelBoxesPerLevel = 1;        // ...y Cajones de Pino por nivel, al elegirla y cada vez que se renace
    public const string SatchelBoxName = "Cajón de Pino";

    public static readonly IReadOnlyList<BlessingDefinition> All =
    [
        new(ChopKey, "Mano de Leñador", "🪓", BlessingKind.Speed, $"+{Pct(ChopPerLevel)} de cantidad en /chop"),
        new(MineKey, "Pico Fino", "⛏️", BlessingKind.Speed, $"+{Pct(MinePerLevel)} de cantidad en /mine"),
        new(TravelDropKey, "Brasa Vieja", "🗺️", BlessingKind.Speed, $"+{Pct(TravelDropPerLevel)} de chance de drop en /travel"),
        new(HuntDropKey, "Ojo de Halcón", "🏹", BlessingKind.Speed, $"+{Pct(HuntDropPerLevel)} de chance de drop en /hunt"),
        new(XpKey, "Aprendiz", "📊", BlessingKind.Speed, $"+{Pct(XpPerLevel)} de EXP en las peleas"),
        new(AttackKey, "Filo Antiguo", "⚔️", BlessingKind.Power, $"+{Pct(AttackPerLevel)} de ataque contra monstruos"),
        new(DefenseKey, "Piel Curtida", "🛡️", BlessingKind.Power, $"+{Pct(DefensePerLevel)} de defensa contra monstruos"),
        new(HpKey, "Corazón de Brasa", "❤️", BlessingKind.Power, $"+{Pct(HpPerLevel)} de vida máxima contra monstruos"),
        new(PetsKey, "Manada", "🐾", BlessingKind.Pets, $"+{Pct(PetsPerLevel)} al bonus de todas tus mascotas"),
        new(PetFeedKey, "Buen Pienso", "🍖", BlessingKind.Pets, $"-{(int)PetFeedReductionPerLevel.TotalMinutes} min de espera para alimentar a cada mascota"),
        new(GoldKey, "Bolsillo Hondo", "💰", BlessingKind.Economy, $"+{Pct(GoldPerLevel)} de oro en las peleas"),
        new(SatchelKey, "Alforja del Fogonero", "🎒", BlessingKind.Items, $"{SatchelPetFoodPerLevel} Comidas para Mascotas y {SatchelBoxesPerLevel} {SatchelBoxName} al elegirla y cada vez que renacés"),
        new(ColorKey, "Brasa de Color", "🌈", BlessingKind.Cosmetic, "un título y un color nuevo para tu perfil"),
    ];

    public static BlessingDefinition? Find(string? key) => All.FirstOrDefault(b => string.Equals(b.Key, key, StringComparison.Ordinal));

    // La oferta de un Fuego Nuevo: hasta OfferSize bendiciones DISTINTAS entre las que todavía no están al nivel máximo. owned = clave → nivel. rng se inyecta para probarlo.
    public static IReadOnlyList<string> RollOffer(IReadOnlyDictionary<string, int> owned, Random rng, int count = OfferSize)
    {
        var candidates = All.Where(b => !owned.TryGetValue(b.Key, out int level) || level < MaxLevel).Select(b => b.Key).ToList();

        // Fisher-Yates sobre la lista de candidatas.
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        return candidates.Take(count).ToList();
    }

    public static string RomanLevel(int level) => level switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => level.ToString(CultureInfo.InvariantCulture) };

    // "🪓 Mano de Leñador III" (con el nivel que va a tener; 0 = todavía no la tiene).
    public static string Label(BlessingDefinition blessing, int level) => $"{blessing.Emoji} {blessing.Name}{(level > 0 ? " " + RomanLevel(level) : string.Empty)}";

    // Lo que da la bendición en ese nivel, en palabras ("+15 % de cantidad en /chop"): lo de cada nivel multiplicado.
    public static string Describe(BlessingDefinition blessing, int level)
    {
        int n = Math.Clamp(level, 1, MaxLevel);
        return blessing.Key switch
        {
            ChopKey => $"+{Pct(ChopPerLevel * n)} de cantidad en /chop",
            MineKey => $"+{Pct(MinePerLevel * n)} de cantidad en /mine",
            TravelDropKey => $"+{Pct(TravelDropPerLevel * n)} de chance de drop en /travel",
            HuntDropKey => $"+{Pct(HuntDropPerLevel * n)} de chance de drop en /hunt",
            XpKey => $"+{Pct(XpPerLevel * n)} de EXP en las peleas",
            AttackKey => $"+{Pct(AttackPerLevel * n)} de ataque contra monstruos",
            DefenseKey => $"+{Pct(DefensePerLevel * n)} de defensa contra monstruos",
            HpKey => $"+{Pct(HpPerLevel * n)} de vida máxima contra monstruos",
            PetsKey => $"+{Pct(PetsPerLevel * n)} al bonus de todas tus mascotas",
            PetFeedKey => $"-{(int)(PetFeedReductionPerLevel.TotalMinutes * n)} min de espera para alimentar a cada mascota",
            GoldKey => $"+{Pct(GoldPerLevel * n)} de oro en las peleas",
            SatchelKey => $"{SatchelPetFoodPerLevel * n} Comidas para Mascotas y {SatchelBoxesPerLevel * n} {SatchelBoxName} al renacer",
            _ => blessing.PerLevel,
        };
    }

    // Los renglones de las bendiciones que TIENE el jugador (las del catálogo en orden, con su nivel y lo que dan), para el perfil y /blessings. Vacío si no tiene ninguna.
    public static IReadOnlyList<string> OwnedLines(IReadOnlyDictionary<string, int>? levels)
    {
        if (levels is null)
        {
            return [];
        }

        var lines = new List<string>();
        foreach (var blessing in All)
        {
            if (levels.TryGetValue(blessing.Key, out int level) && level > 0)
            {
                int clamped = Math.Min(level, MaxLevel);
                lines.Add($"{Label(blessing, clamped)} — {Describe(blessing, clamped)}");
            }
        }

        return lines;
    }

    // Brasa de Color (cosmética): un título y un color para el perfil que cambian con el nivel (I a V). Los colores van como 0xRRGGBB para que esto siga siendo puro (sin Discord).
    private static readonly string[] ColorTitles = ["Brasa Viva", "Llama Dorada", "Fuego Azul", "Ascua Violeta", "Sol del Fogón"];
    private static readonly int[] ColorValues = [0xE8590C, 0xFFB000, 0x1C7ED6, 0x9C36B5, 0xF03E3E];

    public static string ColorTitle(int level) => ColorTitles[Math.Clamp(level, 1, MaxLevel) - 1];

    public static int ColorValue(int level) => ColorValues[Math.Clamp(level, 1, MaxLevel) - 1];

    // 0,05 → "5 %" (con coma decimal y sin ceros de más).
    private static string Pct(double fraction) => $"{(fraction * 100).ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',')} %";
}
