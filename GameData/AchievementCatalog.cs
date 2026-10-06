namespace BotDsRpg.GameData;

// Un tramo de un logro: al llegar a Threshold del contador del logro se desbloquea y se puede reclamar su premio.
public sealed record AchievementTier(long Threshold, RewardSpec Reward);

// Un logro por tramos (Cazador I, II, III). StatKind es el contador de player_stats que lo alimenta (el mismo vocabulario que
// GameEventKinds): el logro no tiene estado propio, es "el contador llegó a tal número".
public sealed record AchievementDefinition(
    string Key, string Name, string Emoji, string StatKind, string Description, IReadOnlyList<AchievementTier> Tiers);

// El catálogo de logros y las cuentas puras para saber qué tramos están desbloqueados.
//
// DESDE CUÁNDO CUENTAN: los contadores empezaron cuando se activó el registro de eventos (v0.6), no antes. Lo que jugó cada uno
// hasta ese día no se sabe, así que los logros se miden desde ahí.
//
// PREMIOS: tramo I chico (oro y XP), II con una caja de su zona, III más grande. Se pagan según la zona del jugador AL RECLAMAR.
// Dos tramos III dan el Arca del Soberano (la caja Mítica, la única con los objetivos larguísimos): son los más difíciles y los
// únicos premios de esa caja, a propósito — no se compra y no hay otra forma de conseguirla.
public static class AchievementCatalog
{
    // Cuántos trofeos hay (los materiales que ningún monstruo suelta y solo salen de las cajas, ver Database/seed_boxes.sql).
    // Los tramos del coleccionista son contra este número: si se agrega o saca un trofeo hay que actualizarlo (lo chequea la prueba).
    public const int TrophyTotal = 17;

    private static readonly RewardSpec TierOne = new(12, 4);
    private static readonly RewardSpec TierTwo = new(40, 8, BoxGrant.ZoneTier);
    private static readonly RewardSpec TierThree = new(100, 15, BoxGrant.ZoneTierPlusOne);
    private static readonly RewardSpec TierThreeMythic = new(100, 15, BoxGrant.Mythic);

    private static IReadOnlyList<AchievementTier> Standard(long one, long two, long three, bool mythicTop = false) =>
        [new(one, TierOne), new(two, TierTwo), new(three, mythicTop ? TierThreeMythic : TierThree)];

    // Los logros de v0.9.0 (comandos, enemigos, misiones...) pagan SOLO oro y XP, sin caja, y poco: sus contadores se pueden inflar (los comandos
    // se cuentan aunque sean de mirar, un enemigo vencido ya cuenta además para Cazador/Viajero/Matajefes) y una caja por tramo se podría farmear.
    // Mismo espíritu que los premios chicos de las misiones: un empujoncito por constancia, no una fuente de oro.
    private static IReadOnlyList<AchievementTier> Plain(long one, long two, long three) =>
        [new(one, new RewardSpec(4, 3)), new(two, new RewardSpec(12, 5)), new(three, new RewardSpec(30, 8))];

    public static readonly IReadOnlyList<AchievementDefinition> All =
    [
        new("cazador",       "Cazador",       "🏹", GameEventKinds.HuntWin,       "Ganá cacerías con /hunt",                          Standard(25, 150, 600)),
        new("viajero",       "Viajero",       "🗺️", GameEventKinds.TravelWin,     "Ganá viajes contra élites con /travel",            Standard(10, 50, 200)),
        new("matajefes",     "Matajefes",     "👑", GameEventKinds.BossWin,       "Vencé a los jefes de zona con /boss",              Standard(1, 5, 20, mythicTop: true)),
        new("recolector",    "Recolector",    "🪓", GameEventKinds.GatheredUnits, "Juntá materiales con /chop y /mine",               Standard(100, 600, 3000)),
        new("herrero",       "Herrero",       "🔨", GameEventKinds.Craft,         "Forjá equipo en la herrería con /forge",           Standard(1, 10, 40)),
        new("comerciante",   "Comerciante",   "🛒", GameEventKinds.ShopGoldSpent, "Gastá oro en la tienda (/shop)",                   Standard(1000, 25000, 250000)),
        new("generoso",      "Generoso",      "🤝", GameEventKinds.GoldGiven,     "Regalá monedas a otros jugadores con /give",       Standard(500, 5000, 50000)),
        new("abridor",       "Abridor",       "📦", GameEventKinds.BoxOpened,     "Abrí cajas con /open",                            Standard(5, 30, 150)),
        new("coleccionista", "Coleccionista", "🏺", GameEventKinds.TrophyFound,   "Conseguí trofeos distintos en las cajas",          Standard(6, 12, TrophyTotal, mythicTop: true)),
        new("constante",     "Constante",     "📅", GameEventKinds.DailyClaim,    "Reclamá tu recompensa diaria con /daily",          Standard(7, 30, 100)),

        // v0.9.0 (premios solo de oro y XP, ver Plain).
        new("comandante",    "Comandante",    "⌨️", GameEventKinds.CommandUsed,   "Usá comandos del bot (de barra o con aa)",          Plain(100, 1000, 10000)),
        new("exterminador",  "Exterminador",  "☠️", GameEventKinds.EnemyDefeated, "Vencé enemigos: cacerías, viajes, jefes y raids",   Plain(50, 500, 3000)),
        new("misionero",     "Misionero",     "📋", GameEventKinds.MissionClaimed,"Reclamá misiones diarias y semanales",              Plain(5, 40, 150)),
        new("desmantelador", "Desmantelador", "🧰", GameEventKinds.Dismantle,     "Desmantelá materiales para sacar Polvo",           Plain(20, 150, 1000)),
        new("encantador",    "Encantador",    "✨", GameEventKinds.Enchant,       "Intentá encantar tu arma o tu amuleto",            Plain(1, 10, 40)),
        new("afortunado",    "Afortunado",    "🎰", GameEventKinds.CasinoWin,     "Ganá oro en el casino con /play",                  Plain(500, 5000, 50000)),
        new("gladiador",     "Gladiador",     "🏟️", GameEventKinds.ArenaJoin,     "Anotate en el torneo diario con /arena join",      Plain(3, 15, 60)),

        // v0.10.0: las mascotas (también solo oro y XP). Hay 5 especies, así que Domador termina en 5; para dejar a las cinco en el nivel máximo hacen falta
        // 125 comidas (PetRules.TotalFeedsToMax × 5) y ese es el último tramo de Criador. Se alimenta a cada una una vez por hora: el contador no se infla.
        new("domador",       "Domador",       "🐾", GameEventKinds.PetHatched,    "Abrí huevos y sumá mascotas con /open",            Plain(1, 3, 5)),
        new("criador",       "Criador",       "🍖", GameEventKinds.PetFed,        "Alimentá a tus mascotas con /pet feed",            Plain(10, 50, 125)),

        // v0.11.0: El Fogón Eterno (también solo oro y XP; el contador sube una vez por victoria sobre el Asador, que tiene el cooldown del jefe).
        new("asador",        "Asador",        "🔥", GameEventKinds.GateWin,       "Vencé al Asador Eterno en El Fogón Eterno",        Plain(1, 3, 10)),

        // v0.12.0: Fuego Nuevo (solo oro y XP; cada reinicio exige volver a vencer al Asador, así que el contador no se infla).
        new("renacido",      "Renacido",      "♻️", GameEventKinds.FuegoNuevo,    "Hacé un Fuego Nuevo y empezá otra vuelta",         Plain(1, 5, 20)),
    ];

    // Los logros se muestran en páginas por tema (/achievements): cada logro está en exactamente una (lo chequea la prueba).
    public sealed record AchievementCategory(string Name, string Emoji, IReadOnlyList<string> Keys);

    public static readonly IReadOnlyList<AchievementCategory> Categories =
    [
        new("Combate", "⚔️", ["cazador", "viajero", "matajefes", "exterminador", "gladiador", "asador"]),
        new("Oficios", "🔨", ["recolector", "herrero", "desmantelador", "encantador"]),
        new("Economía", "💰", ["comerciante", "generoso", "abridor", "coleccionista", "afortunado"]),
        new("Constancia", "📅", ["constante", "comandante", "misionero", "domador", "criador", "renacido"]),
    ];

    // Los logros de una página (0..Categories.Count-1); una página fuera de rango se acota a la más cercana.
    public static IReadOnlyList<AchievementDefinition> OfPage(int page)
    {
        var keys = Categories[Math.Clamp(page, 0, Categories.Count - 1)].Keys;
        return keys.Select(k => Find(k)!).ToList();
    }

    // "aa logros 2" o "aa logros oficios" → el número de página (0-based), o null si no es ninguna. Sin tildes ni mayúsculas.
    public static int? ParsePage(string? text)
    {
        string wanted = Normalize(text);
        if (wanted.Length == 0)
        {
            return null;
        }

        if (int.TryParse(wanted, out int number))
        {
            return number >= 1 && number <= Categories.Count ? number - 1 : null;
        }

        for (int i = 0; i < Categories.Count; i++)
        {
            if (Normalize(Categories[i].Name).StartsWith(wanted, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return null;
    }

    private static string Normalize(string? text) =>
        new string((text ?? string.Empty).Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());

    public static AchievementDefinition? Find(string key) => All.FirstOrDefault(a => a.Key == key);

    // Cuántos tramos (0..3) tiene desbloqueados con ese valor del contador.
    public static int TiersReached(AchievementDefinition achievement, long value) =>
        achievement.Tiers.Count(t => value >= t.Threshold);

    // Los tramos que se cruzaron con un evento que llevó el contador de "previous" a "current" (lo usa el aviso de "¡Logro
    // desbloqueado!": se avisa justo cuando se cruza, no cada vez que se suma algo estando por encima).
    public static IReadOnlyList<(AchievementDefinition Achievement, int Tier)> Crossed(string statKind, long previous, long current)
    {
        var crossed = new List<(AchievementDefinition, int)>();

        foreach (var achievement in All.Where(a => a.StatKind == statKind))
        {
            for (int i = 0; i < achievement.Tiers.Count; i++)
            {
                long threshold = achievement.Tiers[i].Threshold;
                if (previous < threshold && threshold <= current)
                {
                    crossed.Add((achievement, i + 1));
                }
            }
        }

        return crossed;
    }

    public static string Roman(int tier) => tier switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => tier.ToString() };

    public static string TierName(AchievementDefinition achievement, int tier) => $"{achievement.Name} {Roman(tier)}";
}
