using System.Globalization;
using System.Text;

namespace BotDsRpg.GameData;

// Cómo se desbloquea un título (v0.23.0). Nada se guarda: cada regla se calcula de algo que el juego ya lleva (como los logros), así que no se puede desincronizar y lo que el jugador ya hizo cuenta.
//   Always        siempre (el título inicial).
//   Stat          un contador de player_stats llegó a la meta (los de los logros, los duelos, los votos, la XP de los oficios...).
//   Level         el nivel MÁS ALTO que alcanzó alguna vez (el nivel se reinicia con cada Fuego Nuevo; los eventos level_up no).
//   BossRank      venció al jefe de la zona N de la escalera (N = Threshold, por orden de dificultad) en cualquier vuelta.
//   Flag          un hallazgo único (una Corteza, un Meteorito, un encantamiento tier V) que dejó un evento.
//   BlessingColor la bendición Brasa de Color llegó a ese nivel.
public enum TitleRule { Always, Stat, Level, BossRank, Flag, BlessingColor }

// Un título. Source es el contador (Stat) o el nombre del hallazgo (Flag); Hint dice cómo conseguirlo (se muestra mientras está bloqueado).
// Los que vienen de un logro llevan su clave y el tramo: se desbloquean con la misma meta que el tramo, y el aviso de «Logro desbloqueado» cuenta cuál título trae.
public sealed record TitleDefinition(
    string Key, string Name, string PageKey, TitleRule Rule, string Hint, string Source = "", long Threshold = 0, string? AchievementKey = null, int AchievementTier = 0)
{
    public bool FromAchievement => AchievementKey is not null;
}

// Una página de /titles (hasta 25 títulos: el desplegable de equipar admite 25 opciones).
public sealed record TitlePage(string Key, string Name, string Emoji);

// Todo lo que hace falta saber de un jugador para decidir qué títulos tiene, cargado de una vez (ITitleRepository.GetContextAsync).
public sealed record TitleContext(
    IReadOnlyDictionary<string, long> Stats, int MaxLevel, IReadOnlySet<int> BossRanksCleared, IReadOnlySet<string> Flags, int ColorBlessingLevel)
{
    public static TitleContext Empty { get; } = new(new Dictionary<string, long>(), 1, new HashSet<int>(), new HashSet<string>(), 0);
}

// El catálogo de títulos y las cuentas puras para saber cuáles tiene un jugador. 137 títulos en 10 páginas (113 en 9 hasta la v0.40.1; la v0.41.0 sumó la página «Hazañas» con 24):
//   · 93 vienen de los logros (los 31 por 3 tramos: 23 comunes y 8 hazañas), así que su meta es la del tramo y no puede quedar desparejada si se retocan los logros;
//   · 44 son especiales: jefes, nivel, maestro de un oficio, PvP y comunidad, derrotas, hallazgos, y los 5 de la bendición Brasa de Color (antes un texto fijo del perfil).
// Son cosméticos: no dan nada de poder (el principio del repo: nada permanente pesa contra otros jugadores). Desde la v0.23.0 el perfil muestra «Nombre - Título (Clase)».
public static class TitleCatalog
{
    public const string DefaultKey = "recien_llegado";

    // Los hallazgos únicos (TitleRule.Flag).
    public const string FlagCorteza = "corteza";
    public const string FlagMeteorito = "meteorito";
    public const string FlagEnchantV = "encanto_v";

    public static readonly IReadOnlyList<TitlePage> Pages =
    [
        new("combate", "Combate", "⚔️"),
        new("oficios", "Oficios", "🔨"),
        new("economia", "Economía", "💰"),
        new("constancia", "Constancia", "📅"),
        new("fogon", "Mascotas y Fogón", "🐾"),
        new("jefes", "Jefes y niveles", "👑"),
        new("maestros", "Maestros de oficio", "🛠️"),
        new("hazanas", "Hazañas", "🏆"),
        new("pvp", "PvP y comunidad", "🏟️"),
        new("curiosos", "Curiosos", "🎲"),
    ];

    // Los títulos de los logros: (logro, página, qué cuenta la meta, los tres nombres en orden de tramo).
    private static readonly (string Achievement, string Page, string Goal, string[] Names)[] FromAchievements =
    [
        ("cazador", "combate", "Cacerías ganadas", ["Rastreador de Pradera", "Cazador Curtido", "Azote de las Bestias"]),
        ("viajero", "combate", "Viajes ganados", ["Caminante", "Baqueano", "Señor de los Caminos"]),
        ("matajefes", "combate", "Jefes vencidos", ["Desafiante", "Matajefes", "Terror de Jefes"]),
        ("exterminador", "combate", "Enemigos vencidos", ["Espanta Bichos", "Exterminador", "Pesadilla de los Monstruos"]),
        ("gladiador", "combate", "Torneos de la Arena jugados", ["Pisa Arena", "Gladiador", "Fiera del Coliseo"]),
        ("asador", "combate", "Veces que venciste al Asador", ["Rompe Brasas", "Guardián del Fogón", "Señor del Fogón"]),

        ("recolector", "oficios", "Materiales juntados", ["Juntador", "Manos de Obrero", "Dueño del Monte"]),
        ("herrero", "oficios", "Piezas forjadas", ["Aprendiz de Fragua", "Forjador", "Maestro del Yunque"]),
        ("desmantelador", "oficios", "Materiales desmantelados", ["Desarmador", "Chatarrero", "Señor del Polvo"]),
        ("encantador", "oficios", "Intentos de encantar", ["Aprendiz de Brasas", "Encantador de Hierros", "Maestro del Encanto"]),

        ("comerciante", "economia", "Oro gastado en la tienda", ["Cliente Fiel", "Mercachifle", "Magnate del Mostrador"]),
        ("generoso", "economia", "Oro regalado", ["Mano Abierta", "Corazón Generoso", "Mecenas del Fogón"]),
        ("abridor", "economia", "Cajas abiertas", ["Curioso", "Abrecofres", "Rey de las Cajas"]),
        ("afortunado", "economia", "Oro ganado en el casino", ["Suertudo", "Mano Caliente", "Rey del Azar"]),

        ("constante", "constancia", "Días que reclamaste el /daily", ["Madrugador", "De Palabra", "Fiel como el Fogón"]),
        ("racha", "constancia", "Racha más larga del /daily, en días", ["Chispa Constante", "Fuego Parejo", "Brasa que no se Apaga"]),
        ("comandante", "constancia", "Comandos usados", ["Recluta", "Sargento de Fogón", "Comandante"]),
        ("misionero", "constancia", "Misiones reclamadas", ["Mandadero", "Cumplidor", "Hombre de Palabra"]),

        ("domador", "fogon", "Mascotas conseguidas", ["Amigo de las Bestias", "Domador", "Señor de la Manada"]),
        ("criador", "fogon", "Comidas dadas a tus mascotas", ["Alimentador", "Criador", "Maestro Criador"]),
        ("renacido", "fogon", "Fuegos Nuevos hechos", ["Renacido", "Fénix de Cenizas", "Eterno Retorno"]),
        ("companero", "fogon", "Fuegos Nuevos hechos", ["Compañero de Mesa", "Amigo de la Brasa", "Hermano del Fuego"]),
        ("volvio", "fogon", "Fuegos Nuevos hechos", ["El que Vuelve", "Fiel a la Mesa Larga", "El que Volvió"]),

        // v0.41.0: las hazañas (la página «Hazañas»).
        ("hz_cazador", "hazanas", "Cacerías ganadas", ["Pesadilla de la Pradera", "Cazador Sin Descanso", "Leyenda de la Cacería"]),
        ("hz_viajero", "hazanas", "Viajes ganados", ["Pisa Caminos", "Trotamundos", "Dueño del Horizonte"]),
        ("hz_jefes", "hazanas", "Jefes vencidos", ["Rompe Gigantes", "Verdugo de Gigantes", "Azote de los Colosos"]),
        ("hz_recolector", "hazanas", "Materiales juntados", ["Brazo de Hierro", "Titán del Monte", "Montaña Andante"]),
        ("hz_herrero", "hazanas", "Piezas forjadas", ["Martillo Firme", "Yunque Eterno", "Herrero de Leyenda"]),
        ("hz_magnate", "hazanas", "Oro gastado en la tienda", ["Bolsillo Gordo", "Gran Magnate", "Dueño de la Tienda"]),
        ("hz_encantador", "hazanas", "Intentos de encantar", ["Chispa Terca", "Archimago del Polvo", "Señor de los Encantos"]),
        ("hz_explorador", "hazanas", "Horas de expedición de tus mascotas", ["Explorador", "Gran Explorador", "Leyenda de los Caminos"]),
    ];

    // Los jefes de la escalera por orden de zona (los mismos de Lore.Chapters cap1..cap5).
    private static readonly string[] LadderBosses = ["Rey Jabalí", "Lobisón Alfa", "Capataz de Hierro", "Señor del Volcán", "Soberano de la Escoria"];
    private static readonly string[] BossTitles = ["Destronador del Rey Jabalí", "Domador de Lobisones", "Quebrador de Capataces", "Apagavolcanes", "Verdugo del Soberano"];

    public static readonly IReadOnlyList<TitleDefinition> All = BuildAll();

    private static List<TitleDefinition> BuildAll()
    {
        var all = new List<TitleDefinition>();

        // 1) Los 69 de los logros: la meta es la del tramo (una sola fuente de verdad).
        foreach (var (achievementKey, page, goal, names) in FromAchievements)
        {
            var achievement = AchievementCatalog.Find(achievementKey)
                ?? throw new InvalidOperationException($"TitleCatalog: no existe el logro '{achievementKey}'.");
            for (int i = 0; i < achievement.Tiers.Count; i++)
            {
                long threshold = achievement.Tiers[i].Threshold;
                all.Add(new TitleDefinition($"{achievementKey}_{i + 1}", names[i], page, TitleRule.Stat, $"{goal}: {GameHistory.Number(threshold)}", achievement.StatKind, threshold, achievementKey, i + 1));
            }
        }

        // 2) Jefes y niveles: la primera victoria sobre cada jefe de la escalera, el nivel más alto alcanzado y la Brasa de Color.
        for (int rank = 1; rank <= BossTitles.Length; rank++)
        {
            all.Add(new TitleDefinition($"jefe_{rank}", BossTitles[rank - 1], "jefes", TitleRule.BossRank, $"Vencé a {LadderBosses[rank - 1]}, el jefe de la Zona {rank}", Threshold: rank));
        }

        all.Add(new TitleDefinition("jefe_50", "Jefe de Jefes", "jefes", TitleRule.Stat, $"Jefes vencidos: {GameHistory.Number(50)}", GameEventKinds.BossWin, 50));
        foreach (var (key, name, level) in new[] { ("nivel_10", "Fogonero", 10), ("nivel_25", "Parrillero Hecho y Derecho", 25), ("nivel_40", "Asador de Ley", 40), ("nivel_60", "Maestro Parrillero", 60) })
        {
            all.Add(new TitleDefinition(key, name, "jefes", TitleRule.Level, $"Llegá al nivel {level}", Threshold: level));
        }

        string[] colorTitles = ["Brasa Viva", "Llama Dorada", "Fuego Azul", "Ascua Violeta", "Sol del Fogón"];
        for (int level = 1; level <= colorTitles.Length; level++)
        {
            all.Add(new TitleDefinition($"color_{level}", colorTitles[level - 1], "jefes", TitleRule.BlessingColor, $"Bendición Brasa de Color, nivel {level}", Threshold: level));
        }

        // 3) Maestros de oficio: el nivel 25, 50 y 100 de cada oficio (la meta se expresa en el contador de su XP, que es lo que ven los avisos).
        foreach (var (professionKey, names) in new[]
        {
            (ProfessionCatalog.WoodcutterKey, new[] { "Hachero", "Leñador Mayor", "Gran Talador" }),
            (ProfessionCatalog.MinerKey, new[] { "Picapedrero", "Minero de Ley", "Rey del Socavón" }),
            (ProfessionCatalog.EnchanterKey, new[] { "Chispero", "Maestro de Brasas", "Gran Encantador" }),
        })
        {
            var profession = ProfessionCatalog.Get(professionKey);
            int[] levels = [25, 50, 100];
            for (int i = 0; i < levels.Length; i++)
            {
                long threshold = (ProfessionRules.XpToReach(levels[i]) + profession.XpPerAction - 1) / profession.XpPerAction;
                all.Add(new TitleDefinition($"{professionKey}_{levels[i]}", names[i], "maestros", TitleRule.Stat, $"Subí el oficio de {profession.Name} al nivel {levels[i]}", profession.StatKind, threshold));
            }
        }

        // 4) PvP y comunidad.
        AddStatTitles(all, "pvp", GameEventKinds.DuelWin, "duelo", "Duelos amistosos ganados", [(1, "Peleador de Barrio"), (10, "Duelista"), (50, "Espada Libre")]);
        AddStatTitles(all, "pvp", GameEventKinds.ArenaWin, "arena", "Torneos de la Arena ganados", [(1, "Campeón del Día"), (5, "Campeón de Campeones"), (20, "Leyenda de la Arena")]);
        AddStatTitles(all, "pvp", GameEventKinds.Vote, "voto", "Votos cobrados con /vote", [(1, "Votante"), (10, "Voz del Pueblo"), (50, "Padrino de la Casa")]);
        AddStatTitles(all, "pvp", GameEventKinds.Trade, "trueque", "Cambios hechos con otros jugadores", [(1, "Trocador"), (10, "Cambista"), (50, "Rey del Trueque")]);

        // 5) Curiosos.
        AddStatTitles(all, "curiosos", GameEventKinds.FightLost, "derrota", "Peleas perdidas", [(10, "Caído y Levantado"), (50, "Terco como Mula"), (200, "Cabeza de Hierro")]);
        all.Add(new TitleDefinition("hallazgo_corteza", "Ojo de Savia", "curiosos", TitleRule.Flag, "Encontrá una Corteza del Árbol de Vida con /chop", FlagCorteza));
        all.Add(new TitleDefinition("hallazgo_meteorito", "Tocado por el Cielo", "curiosos", TitleRule.Flag, "Encontrá un Fragmento de Meteorito con /mine", FlagMeteorito));
        all.Add(new TitleDefinition("inframundo", "Cliente del Inframundo", "curiosos", TitleRule.Stat, "Comprale algo al Tabernero del Inframundo con /deepshop", GameEventKinds.DeepShopBuy, 1));
        all.Add(new TitleDefinition("encanto_v", "Brasa Soberana", "curiosos", TitleRule.Flag, "Sacá un encantamiento de tier V (Soberano) con /enchant", FlagEnchantV));
        all.Add(new TitleDefinition(DefaultKey, "Recién Llegado", "curiosos", TitleRule.Always, "Lo tenés desde el primer día"));

        return all;
    }

    // Tres títulos por contador: (meta, nombre) en orden. Las claves salen de «prefijo_meta».
    private static void AddStatTitles(List<TitleDefinition> all, string page, string kind, string prefix, string goal, (long Threshold, string Name)[] tiers)
    {
        foreach (var (threshold, name) in tiers)
        {
            all.Add(new TitleDefinition($"{prefix}_{threshold}", name, page, TitleRule.Stat, $"{goal}: {GameHistory.Number(threshold)}", kind, threshold));
        }
    }

    public static TitleDefinition? Find(string? key) => string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(t => t.Key == key);

    public static TitleDefinition Default => Find(DefaultKey)!;

    public static IReadOnlyList<TitleDefinition> OfPage(string pageKey) => All.Where(t => t.PageKey == pageKey).ToList();

    public static TitlePage PageOf(string? pageKey) => Pages.FirstOrDefault(p => p.Key == pageKey) ?? Pages[0];

    // El título que se escribe en el perfil: el que tiene puesto, o el inicial. Una clave que ya no existe (se retiró un título) cae al inicial.
    public static string DisplayName(string? equippedKey) => (Find(equippedKey) ?? Default).Name;

    public static bool IsUnlocked(TitleDefinition title, TitleContext context) => title.Rule switch
    {
        TitleRule.Always => true,
        TitleRule.Stat => context.Stats.TryGetValue(title.Source, out long value) && value >= title.Threshold,
        TitleRule.Level => context.MaxLevel >= title.Threshold,
        TitleRule.BossRank => context.BossRanksCleared.Contains((int)title.Threshold),
        TitleRule.Flag => context.Flags.Contains(title.Source),
        TitleRule.BlessingColor => context.ColorBlessingLevel >= title.Threshold,
        _ => false,
    };

    public static int UnlockedCount(TitleContext context) => All.Count(t => IsUnlocked(t, context));

    // Cuánto lleva en un título bloqueado, en texto corto («vas 87»), o null si no hay un número que mostrar.
    public static string? Progress(TitleDefinition title, TitleContext context)
    {
        switch (title.Rule)
        {
            case TitleRule.Stat:
            {
                // Los de la página de maestros miden el NIVEL del oficio (no los puntos de XP, que no dicen nada).
                if (title.PageKey == "maestros" && ProfessionCatalog.All.FirstOrDefault(p => p.StatKind == title.Source) is { } profession)
                {
                    return $"vas en el nivel {ProfessionRules.LevelFor(ProfessionRules.XpFrom(profession, context.Stats))}";
                }

                return $"vas {GameHistory.Number(Math.Min(context.Stats.GetValueOrDefault(title.Source), Math.Max(0, title.Threshold - 1)))}";
            }

            case TitleRule.Level:
                return $"tu máximo: nivel {context.MaxLevel}";
            default:
                return null;
        }
    }

    // El título que trae un tramo de un logro (para el aviso de «Logro desbloqueado»).
    public static TitleDefinition? ForAchievementTier(string achievementKey, int tier) =>
        All.FirstOrDefault(t => t.AchievementKey == achievementKey && t.AchievementTier == tier);

    // Los títulos ESPECIALES (no los de los logros, que avisa el propio logro) que un evento recién guardado acaba de desbloquear: «antes no llegaba, ahora sí», sin consultar la base.
    //   · un contador (duelos, votos, XP de un oficio...): cruzó la meta con este evento;
    //   · level_up: el nivel nuevo pasó una meta de nivel;
    //   · story_chapter: la primera victoria sobre un jefe de la escalera (el capítulo cap1..cap5) abre el título de ese jefe.
    // Los hallazgos únicos y la Brasa de Color no avisan: aparecen en /titles.
    public static IReadOnlyList<TitleDefinition> SpecialCrossed(string kind, long amount, long newTotal, string? detail)
    {
        var found = new List<TitleDefinition>();

        if (kind == GameEventKinds.StoryChapter && Lore.FindChapter(detail) is { } chapter && chapter.Key.StartsWith("cap", StringComparison.Ordinal))
        {
            found.AddRange(All.Where(t => t.Rule == TitleRule.BossRank && t.Threshold == chapter.Number));
            return found;
        }

        if (amount <= 0)
        {
            return found;
        }

        if (kind == GameEventKinds.LevelUp && int.TryParse(detail, NumberStyles.Integer, CultureInfo.InvariantCulture, out int newLevel))
        {
            int previous = newLevel - (int)amount;
            found.AddRange(All.Where(t => t.Rule == TitleRule.Level && previous < t.Threshold && t.Threshold <= newLevel));
            return found;
        }

        found.AddRange(All.Where(t => t.Rule == TitleRule.Stat && !t.FromAchievement && t.Source == kind && newTotal - amount < t.Threshold && t.Threshold <= newTotal));
        return found;
    }

    // «aa titles 2» o «aa titles jefes» → la página (0-based), o null. Sin tildes ni mayúsculas.
    public static int? ParsePage(string? text)
    {
        string wanted = Normalize(text);
        if (wanted.Length == 0)
        {
            return null;
        }

        if (int.TryParse(wanted, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
        {
            return number >= 1 && number <= Pages.Count ? number - 1 : null;
        }

        for (int i = 0; i < Pages.Count; i++)
        {
            if (Pages[i].Key == wanted || Normalize(Pages[i].Name).StartsWith(wanted, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return null;
    }

    // Busca un título por lo que escribió el jugador: primero el nombre entero (sin tildes ni mayúsculas) y, si no, los que lo contienen.
    public static IReadOnlyList<TitleDefinition> FindByName(string? text)
    {
        string wanted = Normalize(text);
        if (wanted.Length == 0)
        {
            return [];
        }

        var exact = All.Where(t => Normalize(t.Name) == wanted).ToList();
        return exact.Count > 0 ? exact : All.Where(t => Normalize(t.Name).Contains(wanted, StringComparison.Ordinal)).ToList();
    }

    private static string Normalize(string? text) =>
        new string((text ?? string.Empty).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
}
