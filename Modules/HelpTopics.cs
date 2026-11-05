using System.Globalization;
using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// "/info tema:<tema>" y "aa info <tema>": una explicación corta de CÓMO FUNCIONA cada cosa importante del juego (encantar, el casino, el banco, la muerte, los jefes...),
// con sus comandos y sus números. Pedido del dueño: "info enchant, info play, info xxx, y así".
//
// Los números NO están escritos a mano: salen de las mismas constantes que usa el juego (CooldownCatalog, DeathPenalty, BankRules, CasinoService, Enchantments, RaidSettings...),
// así que si se retocan la ayuda se actualiza sola. Cada tema es pura (sin base de datos): un embed armado con constantes. Lo que vive en la base y el dueño ajusta a mano
// (precios, rangos de las cajas, los niveles de las zonas) NO se repite acá: la ayuda manda a /shop view, /zonas, etc.
// Para sumar un tema: una fila en All (clave en inglés como los comandos, alias en español) y su método Build; la prueba revisa que entre en Discord, que no choque con otro
// y que todos los comandos que nombra existan.
// InEnglish: el tema ya tiene su texto en inglés (v0.26.0 empezó por los cinco de primer contacto: hunt, gather, shop, daily y missions). Los demás salen en español aun para quien juega en inglés, enteros
// (título y pie incluidos): un tema a medias, con el cuerpo en un idioma y el marco en otro, se lee peor que uno entero en español. Al traducir un tema: marcá Name y Summary con Loc.Mark y poné InEnglish: true.
public sealed record HelpTopic(string Key, string Emoji, string Name, string Summary, IReadOnlyList<string> Aliases, Func<Embed> Build, bool InEnglish = false);

public static class HelpTopics
{
    public static readonly IReadOnlyList<HelpTopic> All =
    [
        new("enchant", "✨", Loc.Mark("Encantamientos"), Loc.Mark("tiers, chances y bonus para tu arma y tu amuleto"), ["encantar", "encantamientos", "encantamiento", "encanto", "encantos", "enchants"], Enchant, InEnglish: true),
        new("dismantle", "🧰", Loc.Mark("Desmantelar y Polvo"), Loc.Mark("romper materiales para juntar Polvo"), ["polvo", "dust", "desmantelar", "desarmar"], Dismantle, InEnglish: true),
        new("bank", "🏦", Loc.Mark("El banco"), Loc.Mark("guardar oro a salvo de la penalidad"), ["banco", "guardar"], Bank, InEnglish: true),
        new("death", "☠️", Loc.Mark("Perder un combate"), Loc.Mark("la penalidad por morir y cómo evitarla"), ["muerte", "morir", "penalidad", "derrota", "perder"], Death, InEnglish: true),
        new("play", "🎰", Loc.Mark("El casino"), Loc.Mark("coinflip y slots: cuánto paga cada uno"), ["casino", "slots", "coinflip", "apostar", "juego"], Play, InEnglish: true),
        new("hunt", "🏹", Loc.Mark("Combate"), Loc.Mark("cazar, viajar, huir, curarte y el modo automático"), ["combate", "cazar", "caceria", "travel", "viaje", "autohunt", "pelea", "pelear", "combat"], Hunt, InEnglish: true),
        new("boss", "👑", Loc.Mark("Jefes y equipo"), Loc.Mark("el jefe de cada zona y cómo hacerlo con amigos"), ["jefe", "jefes", "raid", "raids", "grupo"], Boss, InEnglish: true),
        new("miniboss", "🌍", Loc.Mark("El jefe mundial"), Loc.Mark("el jefe del día que pelean todos juntos a la medianoche"), ["worldboss", "world boss", "wb", "jefe mundial", "jefemundial", "mundial", "minijefe"], Miniboss, InEnglish: true),
        new("forge", "🔨", Loc.Mark("Herrería y equipo"), Loc.Mark("forjar armas y amuletos, y las herramientas del Carpintero"), ["herrero", "herreria", "forja", "forjar", "recetas", "receta", "equipo", "arma", "armas", "amuleto", "carpintero", "herramientas", "herramienta", "hacha", "pico", "tools", "tool", "equipamiento"], Forge, InEnglish: true),
        new("zone", "🗺️", Loc.Mark("Zonas"), Loc.Mark("cómo se avanza por el mundo"), ["zona", "zonas", "mundo", "mapa"], Zone, InEnglish: true),
        new("boxes", "📦", Loc.Mark("Cajas"), Loc.Mark("qué dan, cómo se consiguen y cada cuánto"), ["caja", "cajas", "open", "abrir", "cofre", "cofres"], Boxes, InEnglish: true),
        new("shop", "🍺", Loc.Mark("Tienda, taberna y curarte"), Loc.Mark("comida, banquetes, /heal y vender"), ["tienda", "taberna", "comida", "consumibles", "consumible", "heal", "curar", "curarse", "vender", "comprar"], Shop, InEnglish: true),
        new("missions", "📋", Loc.Mark("Misiones y logros"), Loc.Mark("qué se reclama, cuándo se reinicia y cuánto pagan"), ["mision", "misiones", "logro", "logros", "achievements", "missions"], Missions, InEnglish: true),
        new("arena", "🏟️", Loc.Mark("Arena y duelos"), Loc.Mark("el torneo diario y los duelos con amigos"), ["pvp", "duelo", "duelos", "fight", "torneo"], Arena, InEnglish: true),
        new("trade", "🤝", Loc.Mark("Intercambio"), Loc.Mark("cambiar materiales con otro jugador"), ["cambio", "cambiar", "trueque", "intercambio"], Trade, InEnglish: true),
        new("exchange", "🔁", Loc.Mark("Cambiar drops con el tabernero"), Loc.Mark("3 drops de una zona por 1 de la misma"), ["canje", "canjear", "swap", "tabernero", "cambalache"], Exchange, InEnglish: true),
        new("fogon", "🔥", Loc.Mark("El Fogón Eterno"), Loc.Mark("la puerta del final del mundo y su equipo"), ["fogón", "asador", "puerta", "zona0", "zona 0", "gate"], Fogon, InEnglish: true),
        new("fuego", "♻️", Loc.Mark("Fuego Nuevo"), Loc.Mark("volver a empezar con bonus permanentes y bendiciones"), ["fuegonuevo", "fuego nuevo", "fn", "reinicio", "reiniciar", "renacer", "reset", "vuelta", "bendicion", "bendiciones", "blessing", "blessings", "prestigio"], Fuego, InEnglish: true),
        new("story", "📖", Loc.Mark("Las Crónicas del Fogón"), Loc.Mark("la historia del juego: capítulos, escenas y cómo se abren"), ["historia", "cronicas", "crónicas", "lore", "cuento"], Story, InEnglish: true),
        new("professions", "🛠️", Loc.Mark("Oficios"), Loc.Mark("Leñador, Minero, Pescador y Encantador: nivel, XP y la versión avanzada"), ["oficios", "oficio", "profesion", "profesiones", "prof", "lenador", "leñador", "woodcutter", "pescador"], Professions, InEnglish: true),
        new("pets", "🐾", Loc.Mark("Mascotas"), Loc.Mark("huevos, bonus pasivos y cómo alimentarlas"), ["mascota", "mascotas", "pet", "huevo", "huevos", "egg", "eggs"], Pets, InEnglish: true),
        new("dragon", "🐲", Loc.Mark("Dragón"), Loc.Mark("comprarlo, entrenarlo y criarlo: tipo, tiers y comida"), ["dragones", "dragons", "criar", "cria", "cría", "breed", "horse", "caballo", "comida de dragon", "comida de dragón"], Dragon, InEnglish: true),
        new("classes", "🎭", Loc.Mark("Clases y habilidades"), Loc.Mark("qué hace cada clase"), ["clase", "clases", "class", "habilidad", "habilidades"], Classes, InEnglish: true),
        new("gather", "🪓", Loc.Mark("Recolección"), Loc.Mark("talar y minar: unidades y rarezas"), ["chop", "mine", "fish", "talar", "minar", "pescar", "pesca", "cook", "cocinar", "cocina", "plato", "platos", "recolectar", "recoleccion", "madera", "mineral"], Gather, InEnglish: true),
        new("daily", "🎁", Loc.Mark("Diario y regalos"), Loc.Mark("la recompensa diaria, la racha y regalar oro"), ["diario", "racha", "give", "dar", "regalo", "regalar"], Daily, InEnglish: true),
        new("stats", "📊", Loc.Mark("Nivel y estadísticas"), Loc.Mark("EXP, vida, ataque y defensa"), ["nivel", "exp", "xp", "ataque", "defensa", "vida", "hp", "perfil", "level"], Stats, InEnglish: true),
        new("reminders", "⏰", Loc.Mark("Recordatorios"), Loc.Mark("los avisos del bot cuando termina una espera"), ["recordatorio", "recordatorios", "reminder", "reminders", "aviso", "avisos", "alarma", "alarmas"], Reminders, InEnglish: true),
        // OJO: Discord muestra como máximo 25 sugerencias, así que con 28 temas la lista desplegable SIN escribir nada deja afuera los tres últimos (stats, reminders y vote). Desde la v0.46.0 el jefe mundial tiene tema propio. Se encuentran escribiendo («votar», «avisos») y figuran en /info;
        // el próximo tema que se sume no tiene lugar sin sacar otro o fundirlo con uno existente (las herramientas del Carpintero, por eso, son un campo del tema forge y no un tema aparte).
        new("vote", "🗳️", Loc.Mark("Votar en top.gg"), Loc.Mark("qué da cada voto y cómo se cobra"), ["votar", "voto", "votos", "votacion", "votación", "topgg", "top.gg"], Vote, InEnglish: true),
    ];

    // Lo que se escribe para elegir un tema: su clave, su nombre y sus alias.
    private static IEnumerable<string> Terms(HelpTopic topic) => new[] { topic.Key, topic.Name }.Concat(topic.Aliases);

    // El tema que corresponde a lo escrito (sin importar mayúsculas ni tildes): primero el nombre exacto (clave, nombre o alias); si no, el único tema que EMPIEZA con eso
    // ("ench" → encantamientos). Con dos palabras ("enchant info") prueba la primera. Null si no hay ninguno o hay más de uno posible.
    public static HelpTopic? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string trimmed = text.Trim();
        var exact = All.FirstOrDefault(t => Terms(t).Any(term => AutocompleteText.SameName(term, trimmed)));
        if (exact is not null)
        {
            return exact;
        }

        var starting = All.Where(t => Terms(t).Any(term => AutocompleteText.Relevance(term, trimmed) == 0)).ToList();
        if (starting.Count == 1)
        {
            return starting[0];
        }

        string first = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return first.Length < trimmed.Length ? Find(first) : null;
    }

    // Para la lista desplegable de /info: los temas que contienen lo escrito (en su clave, nombre o alias), primero los que empiezan con eso.
    public static IReadOnlyList<HelpTopic> Search(string? typed) =>
        All.Where(t => string.IsNullOrWhiteSpace(typed) || Terms(t).Any(term => AutocompleteText.Matches(term, typed)))
            .OrderBy(t => string.IsNullOrWhiteSpace(typed) ? 0 : Terms(t).Min(term => AutocompleteText.Relevance(term, typed)))
            .ToList();

    // /info y "aa info": sin tema, la lista de comandos de siempre; con un tema, su explicación; con uno que no existe, la lista de temas avisando que no lo encontró.
    public static Embed Resolve(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return HelpModule.BuildInfoEmbed();
        }

        return Find(text) is { } topic ? Render(topic) : BuildIndexEmbed(text.Trim());
    }

    // El tema en el idioma que le toca: uno traducido, en el de quien lo pide; uno todavía sin traducir, ENTERO en español (hasta los números: «1.000», no «1,000»), no a medias.
    public static Embed Render(HelpTopic topic)
    {
        if (topic.InEnglish)
        {
            return topic.Build();
        }

        using (Loc.Use(Loc.Spanish))
        {
            return topic.Build();
        }
    }

    // La lista de temas (con una línea de qué explica cada uno). unknown: lo que escribió quien pidió un tema que no existe.
    public static Embed BuildIndexEmbed(string? unknown = null)
    {
        string intro = unknown is null
            ? Loc.T("Elegí un tema con **/info tema:<tema>** (o `aa info <tema>`) y te explico cómo funciona.")
            : Loc.F($"No encontré el tema **{unknown}**. Estos son los que hay: **/info tema:<tema>** (o `aa info <tema>`).");

        var lines = All.Select(t => $"{t.Emoji} **{t.Key}** — {Loc.Tr(t.Summary)}").ToList();
        var embed = new EmbedBuilder()
            .WithTitle(Loc.T("📚 Ayuda por tema"))
            .WithColor(OnboardingModule.BrandColor)
            .WithDescription(intro + Loc.T("\n\nLa lista completa de comandos sigue en **/info** (sin tema)."));

        int half = (lines.Count + 1) / 2;
        embed.AddField(Loc.T("Temas"), string.Join('\n', lines.Take(half)), false);
        embed.AddField(Loc.T("Más temas"), string.Join('\n', lines.Skip(half)), false);
        return embed.Build();
    }

    // Una línea con las claves, para el pie de /info: "enchant, dismantle, bank...".
    public static string KeysLine() => string.Join(", ", All.Select(t => t.Key));

    // ---- Armado de cada tema ----

    private static Embed Topic(HelpTopic topic, string description, params (string Name, string Value)[] fields)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"{topic.Emoji} {Loc.Tr(topic.Name)}")
            .WithColor(OnboardingModule.BrandColor)
            .WithDescription(description);

        foreach (var (name, value) in fields)
        {
            embed.AddField(name, value, false);
        }

        // El pie va en el idioma del cuerpo: un tema todavía sin traducir lo lleva en español.
        string footer = topic.InEnglish
            ? Loc.T("Más temas: /info (sin tema muestra los comandos; con tema, cómo funciona algo)")
            : "Más temas: /info (sin tema muestra los comandos; con tema, cómo funciona algo)";
        return embed.WithFooter(footer).Build();
    }

    private static HelpTopic Self(string key) => All.First(t => t.Key == key);

    // "5 h", "30 min", "1 min": un cooldown como lo diría una persona.
    private static string Dur(TimeSpan span) =>
        span.TotalMinutes >= 60 && span.TotalMinutes % 60 == 0 ? $"{(int)span.TotalHours} h" : $"{(int)span.TotalMinutes} min";

    // Decimal con coma: 1,5 / 3,5.
    private static string Dec(double value) => Loc.Current == Loc.English ? value.ToString("0.##", CultureInfo.InvariantCulture) : value.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    // Lo mismo que Dec, pero en el idioma de quien lee (coma en español, punto en inglés): lo usan los temas traducidos. Dec queda como estaba para los que todavía no.
    private static string LDec(double value) => Loc.Current == Loc.English ? value.ToString("0.##", CultureInfo.InvariantCulture) : Dec(value);

    private static string Num(long value) => GameHistory.Number(value);

    private static Embed Enchant() => DustModule.BuildOptionsEmbed();

    private static Embed Dismantle() => Topic(Self("dismantle"),
        Loc.T("Desmantelar rompe materiales para sacarles **Polvo**, y el Polvo sirve para **encantar** tu arma y tu amuleto."),
        (Loc.T("🛠️ Cómo se hace"),
            Loc.T("**/dismantle** y elegís el ítem de la lista (solo aparecen los que tenés) y la cantidad: la que quieras, sin tope, o `todo` para todas las que tengas.\nEn texto: `aa desmantelar 30 Piedra` o `aa desmantelar todo Piedra`.")),
        (Loc.T("🧱 Qué se puede romper"),
            Loc.T("Madera, minerales y drops de monstruos. **No** las cajas, la comida ni el equipo. Es de una sola mano: el Polvo no vuelve a convertirse en material.")),
        (Loc.T("✨ Cuánto Polvo da cada unidad"),
            string.Join(" · ", new[] { Loc.T("Común"), "Raro", Loc.T("Épico"), "Legendario", Loc.T("Mítico") }.Select(r => $"{r} {Num(Dismantling.DustPerUnit(r))}")) +
            Loc.F($"\nLos **drops de monstruos** no tienen rareza (caen igual en la Zona 1 que en la 5): dan **{Dismantling.HuntDropDust}** cada uno.") +
            Loc.T("\nRinde parecido por minuto de farmeo en todas las rarezas, así que no hay una «mejor» para romper.")),
        (Loc.T("🎯 Para qué"), Loc.T("Cada intento de **/enchant** gasta Polvo y oro. Mirá cómo funciona con **/info tema:enchant**. Tu Polvo figura en **/profile**.")));

    private static Embed Bank() => Topic(Self("bank"),
        Loc.F($"Guardás oro **a salvo de la penalidad por morir**. La cuenta se compra una sola vez por **{Num(BankRules.AccountPrice)}** de oro."),
        (Loc.T("🏦 Comandos"),
            Loc.T("**/bank open** abre la cuenta · **/bank deposit** guarda · **/bank withdraw** saca · **/bank view** muestra tu billetera y tu banco.\nEn deposit y withdraw podés escribir una cantidad (**500**, **10k** de mil, **1m** de millón) o **all**. En texto, sin escribir bank: `aa deposit 500` y `aa withdraw 10k`.")),
        (Loc.T("🛡️ Para qué sirve"),
            Loc.F($"Si perdés un combate perdés el {DeathPenalty.GoldPercent} % del oro de la **billetera**; lo que está en el banco no se toca. Mirá **/info tema:death**.")),
        (Loc.T("⚠️ Ojo"), Loc.T("Sin interés ni tope de capacidad. Las compras, `/give` y las apuestas usan la billetera: sacá el oro del banco antes de gastarlo.")),
        (Loc.T("📈 El minibuff de oro"),
            Loc.F($"Lo que guardás da hasta **+{BankRules.MaxGoldBonusPercent:0} %** de oro en las peleas (cacería, viaje, jefe y /autohunt), con una curva: tener más siempre suma, pero cada vez menos. ") +
            Loc.T("Se mide según **tu zona** (una cacería de oro es la unidad), así que sirve igual al principio que al final. Mirá cuánto rinde en **/bank view**.")));

    private static Embed Death() => Topic(Self("death"),
        Loc.T("Perder un combate tiene un costo. **No** bajás de nivel."),
        (Loc.T("☠️ Qué perdés"),
            Loc.F($"La **EXP del nivel actual vuelve a 0** y el **{DeathPenalty.GoldPercent} % del oro de tu billetera** (con menos de {100 / DeathPenalty.GoldPercent} de oro no se pierde oro). ") +
            Loc.T("Una muerte promedio son unos 15 a 20 minutos de juego en EXP.")),
        (Loc.T("📍 Cuándo pasa"),
            Loc.T("Si te vencen en **/hunt**, **/travel**, **/boss**, **/autohunt**, si te rematan mientras te curás con **/use**, o si cae todo el grupo del jefe en equipo (a cada caído). ") +
            Loc.T("Huir, que se acabe el tiempo, los duelos (**/fight**) y la **Arena** no te quitan nada.")),
        (Loc.T("🛡️ Cómo protegerte"),
            Loc.F($"Guardá oro en el **banco** (**/info tema:bank**), curate con **/heal** antes de pelear, y recordá que **/autohunt** no te deja pelear con menos del {AutoHuntRules.MinHpPercent} % de vida ") +
            Loc.T("porque no puede huir. En **/hunt** a mano ves tu vida y podés huir.")));

    private static Embed Play() => Topic(Self("play"),
        Loc.F($"**/play** apuesta tu oro. Apuesta mínima: **{CasinoModule.MinBet}**. Podés escribir **all** para apostar todo lo que llevás en la billetera, o **10k** / **1m** para mil / millón. En texto: `aa cf h 100` (h = heads, t = tails) y `aa slots 100`."),
        (Loc.T("🪙 Coinflip"),
            Loc.F($"Elegís cara (**Heads**) o cruz (**Tails**) y sale al azar: 50 % de acertar. Si acertás cobrás **×{CasinoService.CoinflipMultiplier}** lo apostado. Y a veces (1 de cada {1000 / CasinoService.CoinflipEdgeChancePerMille}) la moneda cae **de canto**: sorpresa, cobrás **×{CasinoService.CoinflipEdgeMultiplier}** elijas lo que elijas.")),
        (Loc.T("🎰 Slots"),
            Loc.F($"Salen tres símbolos al azar ({string.Join(' ', CasinoService.SlotsSymbolList)}): tres iguales ({Dec(CasinoService.SlotsThreeMatchChance * 100)} % de las veces) pagan **×{CasinoService.SlotsThreeMatchMultiplier}**, ") +
            Loc.F($"dos iguales ({Dec(CasinoService.SlotsPairChance * 100)} %) pagan **×{Dec(CasinoService.SlotsPairMultiplier)}** y tres distintos ({Dec(CasinoService.SlotsAllDifferentChance * 100)} %) pierden la apuesta.\n") +
            Loc.F($"A la larga la casa se queda con el **{Dec((1 - CasinoService.SlotsReturnToPlayer) * 100)} %** de lo que se apuesta: es un juego de suerte, no una forma de juntar oro.")),
        (Loc.T("🃏 Blackjack"),
            Loc.F($"**/play game:blackjack** o `aa bj 10k` reparte una mano: te acercás a **21** sin pasarte y le ganás a la banca. Con los botones **Pedir** (otra carta), **Plantarte** o **Doblar** (solo con las dos primeras cartas: una carta más y la apuesta se duplica); por texto `aa bj hit` / `aa bj stand` / `aa bj double`. ") +
            Loc.F($"La banca pide hasta 17 y se planta en todos los 17. Ganar paga 1 a 1, el **blackjack** (As + 10 de entrada) paga **3 a 2**, el empate devuelve la apuesta. No se divide ni hay seguro. Si la banca tiene blackjack la mano **sigue**: si llegás a 21, empatás. La apuesta se cobra al repartir y, si dejás la mano sin tocar {BlackjackService.IdleSeconds} segundos, la banca te planta. ") +
            Loc.T("Jugando bien la casa se queda con ~1 %; jugando mal, con más.")),
        (Loc.T("📈 Tus números"), Loc.T("**/history** muestra cuánto ganaste y perdiste en el casino, y la diferencia.")));

    private static Embed Hunt() => Topic(Self("hunt"),
        Loc.F($"Peleás por turnos contra los monstruos de tu zona. Cada turno elegís **Atacar**, la **habilidad** de tu clase (se recarga {AbilityTuning.CooldownTurns} turnos) o **huir** (perdés la recompensa)."),
        (Loc.T("🏹 /hunt"),
            Loc.F($"Cooldown de {Dur(CooldownCatalog.Hunt.Duration)}. Da oro y EXP (más cuanto más nivel y zona) y {CombatRewardCalculator.HuntDropChancePercent} % de chance de soltar el material de ese monstruo. Mirá qué suelta cada uno con **/drops**.")),
        (Loc.T("🗺️ /travel"),
            Loc.F($"Un monstruo élite de tu zona, más duro. Cooldown de {Dur(CooldownCatalog.Travel.Duration)}. Paga {CombatRewardCalculator.TravelRewardMultiplier} cacerías juntas de oro y EXP y a veces suelta una caja de tu zona (la chance de cada zona está en **/drops**).")),
        (Loc.T("🤖 /autohunt (aa ah)"),
            Loc.F($"Resuelve una cacería de una sola vez: solo ataque básico, sin habilidad y sin poder huir. Comparte cooldown con /hunt y pide al menos **{AutoHuntRules.MinHpPercent} %** de vida.")),
        (Loc.T("⌨️ Si un botón se traba"),
            Loc.T("Escribí **`aa attack`**, **`aa ability`** (la habilidad de tu clase) o **`aa flee`** (huir): juegan el mismo turno que el botón y el resultado sale en un mensaje nuevo con botones nuevos. " +
            "Sirve en /hunt, /travel y /boss (también en equipo). Tenés **60 segundos** por turno: si no actuás en ese tiempo el combate se abandona (en equipo perdés el turno, y al segundo seguido te retirás).")),
        (Loc.T("❤️ Tu vida"),
            Loc.T("La vida no se recupera sola: curate con **/heal**, **/use** o en la **/taberna**. Con 0 HP no podés pelear. En plena pelea (/hunt, /travel y /boss) solo podés comer **una vez por pelea**.")),
        (Loc.T("🔒 Mientras peleás"),
            Loc.T("Con una pelea en curso (también un jefe en equipo o un duelo) solo valen **/use**, atacar, la habilidad y huir, y lo que solo mira: **/profile**, **/inventory**, **/cd**, **/info** y **/tutorial**. Lo demás (recolectar, forjar, jugar, etc.) espera a que termine.")),
        (Loc.T("☠️ Si perdés"), Loc.F($"Perdés la EXP del nivel y el {DeathPenalty.GoldPercent} % del oro: **/info tema:death**.")));

    private static Embed Boss() => Topic(Self("boss"),
        Loc.T("Cada zona tiene un jefe. Vencerlo la **primera vez** abre la zona siguiente."),
        (Loc.T("👑 /boss"),
            Loc.F($"El jefe de tu zona, solo. Pide el nivel de la zona siguiente. Cooldown de **{Dur(CooldownCatalog.Boss.Duration)}** si ganás y de **{Dur(CooldownCatalog.Boss.RetryAfterFailure ?? CooldownCatalog.Boss.Duration)}** si perdés, huís o se acaba el tiempo. ") +
            Loc.F($"Paga {CombatRewardCalculator.BossRewardMultiplier} cacerías (más el bono del jefe) y suelta el **cofre** de su zona: siempre la primera vez que lo vencés (en cada vuelta). Las veces siguientes puede caer una caja más chica (mirá **/drops**). ") +
            Loc.T("Esa primera vez también te llega el **huevo** de la mascota de la zona (**/info tema:pets**).")),
        (Loc.T("👥 /boss @amigo (aa boss @amigo)"),
            Loc.F($"El mismo jefe **en equipo**, de {RaidSettings.MinParticipants} a {RaidSettings.MaxParticipants} jugadores: nombrás a uno o más compañeros y ellos aceptan con un botón (tienen {(int)RaidModule.LobbyDuration.TotalSeconds} segundos). ") +
            Loc.T("Pegan **por turnos**: pega uno, el jefe le responde a ese jugador y le toca al siguiente (el bot te menciona cuando es tu turno); si alguien no juega en 60 segundos pierde el turno y, si pasa dos veces seguidas, se retira. ") +
            Loc.T("**Todos tienen que tener la misma zona máxima** (la que ves en tu perfil como \"máx. Zona N\"): si no, el grupo se rechaza. ") +
            Loc.T("Cada uno puede **comer una vez** por pelea con el botón 🍖 **Comer** (o con **/use**): cura, pero gasta tu turno. Los banquetes también valen (curan y te dan su +% de ataque en el acto). ") +
            Loc.T("La vida del jefe crece con cada jugador y cada uno que le pega cobra la recompensa entera. Comparte cooldown con el jefe en solitario.")),
        (Loc.T("🌍 /miniboss (el jefe mundial)"),
            Loc.T("Un jefe del día que pelean **todos los anotados juntos** a las 00:00. No pide nivel y **no gasta el cooldown del jefe**. Todo en **/info tema:miniboss**.")),
        (Loc.T("🎒 Antes de ir"), Loc.T("Llevá el arma y el amuleto de tu zona (**/info tema:forge**) y la vida llena. Si perdés, pagás la penalidad: **/info tema:death**.")));

    private static Embed Miniboss() => Topic(Self("miniboss"),
        Loc.T("Todos los días aparece **un jefe para todo el servidor**. Durante el día te anotás y a la medianoche lo pelean **todos los anotados juntos**: mientras más gente, más vida le sacan y más cobra cada uno."),
        (Loc.T("📝 Anotarte"),
            Loc.T("**/miniboss** (en texto `aa miniboss` o `aa wb`). **No pide nivel** (sirve aunque todavía no llegues al de **/boss**) y **no gasta ningún cooldown**: el jefe de tu zona lo seguís haciendo igual. Te anotás **una vez por día**; escribir **/miniboss** otra vez te muestra cuántos van. En **/cd** ves si ya estás anotado, justo debajo del jefe.")),
        (Loc.T("🕛 La pelea"),
            Loc.T("A las **00:00** (hora de Uruguay) el bot pelea solo: no tenés que estar conectado. Cada anotado le pega según **su ataque comparado con lo esperable en su zona** (arma, nivel y encantamiento), con un poco de suerte del día; así un jugador de la Zona 1 bien equipado pega tanto como uno de la Zona 5 bien equipado. ") +
            Loc.T("Se cuenta en el canal donde se anotó el primero: quiénes fueron, los golpes más fuertes, cuánta vida le sacaron y el MVP.")),
        (Loc.T("💰 El premio"),
            Loc.F($"Cada anotado cobra lo mismo, en **cacerías de oro de SU zona**: **{WorldBossRules.MaxHunts}** si el jefe cae y, si no, de **{WorldBossRules.MinHunts}** a {WorldBossRules.MaxHunts} según cuánta vida le sacaron entre todos. ") +
            Loc.F($"Con unos **{WorldBossRules.FullCrowd:0} jugadores** bien equipados el jefe cae. Solo, cobrás cerca de {WorldBossRules.MinHunts}: **traé amigos**.")),
        (Loc.T("💡 Truco"), Loc.T("Anotate apenas empieza el día y avisale a tus amigos: no cuesta nada y el premio sube con cada uno que se suma.")));

    private static Embed Forge() => Topic(Self("forge"),
        Loc.T("En la herrería forjás armas y amuletos con materiales y oro. Todo está en la base de datos y cada uno ve las recetas de **su** clase."),
        (Loc.T("🔨 Cómo"),
            Loc.T("**/forge** (en texto `aa herrero`): el herrero te muestra un menú con lo que podés forjar (✅ lo que ya podés pagar, ❌ lo que te falta). Cada zona tiene 5 recetas pero ves 2: **el arma de tu clase** y **el amuleto**. Con la segunda lista elegís una zona y ves sus recetas con lo que tenés de cada material (o `aa forge recipes [zona]`). Si preferís ver todas las zonas juntas, una línea cada una, activá el **recetario completo** en **/config**.")),
        (Loc.T("⚔️ Tu arma y la Hoja"),
            Loc.T("Cada clase tiene su arma en cada zona (espada, daga, arco o grimorio) con un ataque FIJO que sube de zona en zona: lo que dice la receta es lo que pega, sin multiplicadores. Además hay una sola arma general, la **Hoja de Acero Puro**, sin zona y hecha solo con materiales de recolección: sirve para empezar.")),
        (Loc.T("🎽 Se equipa sola"),
            Loc.T("Lo forjado queda **puesto**: no va a la mochila ni hace falta equiparlo. Para cambiarlo, vendés la pieza que llevás (en la **/taberna** o con **/shop sell**) y forjás la siguiente. Ojo: al venderla pierde su encantamiento.")),
        (Loc.T("🪓 El Carpintero (hacha, pico y caña)"),
            Loc.T("**/tools** (en texto `aa herramientas`): el Carpintero te arma el **hacha** (multiplica lo que sacás con **/chop**), el **pico** (con **/mine**) y la **caña** (con **/fish**) **solo con materiales** de recolección, sin oro. ") +
            Loc.T("Hay dos de cada uno, el de la **Zona 3** y el de la **Zona 5**: se arma cuando tenés esa zona desbloqueada y reemplaza al anterior (sin herramienta rendís ×1). La lista de **/tools** muestra qué pide cada una y cuánto te falta. ") +
            Loc.T("Multiplican lo Común, Raro y Épico; los Legendarios y Míticos quedan en 1. Es equipo: se vuelve a armar después de cada **Fuego Nuevo**.")),
        (Loc.T("🧱 Los materiales"), Loc.T("Madera y minerales con **/chop** y **/mine**; los drops, de los monstruos (**/drops**). **/tips** te dice qué te falta para tu próxima forja.")));

    private static Embed Zone() => Topic(Self("zone"),
        Loc.T("El mundo se divide en zonas, cada una más difícil que la anterior. **/zonas** las lista con su nivel mínimo."),
        (Loc.T("🧭 Cómo avanzás"),
            Loc.T("**/zona** te mueve a otra. Hace falta tener el nivel mínimo **y** haber vencido al jefe de la zona anterior (**/info tema:boss**). Tu perfil muestra tu zona y hasta dónde llegaste (máx. Zona N).")),
        (Loc.T("🎯 Qué depende de tu zona"),
            Loc.T("Los monstruos de **/hunt**, **/travel** y **/boss**, las recetas de la herrería, el valor de los premios de misiones, logros y Arena, y qué cajas podés comprar.")),
        (Loc.T("🧬 Qué hay en cada una"), Loc.T("Dos monstruos de cacería, un élite (para /travel) y un jefe. Con **/drops** ves qué suelta cada uno.")),
        (Loc.T("🏛️ Las zonas que abre el Fuego Nuevo"),
            Loc.F($"Desde el **Fuego Nuevo {ZoneSixFn}** aparecen zonas nuevas, una cada 3 Fuegos Nuevos, hasta la **Zona 10**. Cada una tiene **un** monstruo de cacería con **un** drop y un élite, y **no tiene jefe**: se sale por una **mazmorra**, un acertijo distinto en cada zona, que se hace solo y sin comida. ") +
            Loc.T("Se entra con **/boss** y la **Reliquia** de la zona (se forja; pide un **Cofre de Escoria**, que sale del **/travel** de esas zonas). Cada error saca vida; si te quedás sin vida perdés la Reliquia. Ganarla abre la zona siguiente, y la de tu última zona te lleva directo contra el **Asador**, más fuerte y sin poder curarte.")));

    // El Fuego Nuevo que abre la Zona 6 (Database/add_zones_6_10.sql: 5, 8, 11, 14, 17).
    private const int ZoneSixFn = 5;

    private static Embed Boxes() => Topic(Self("boxes"),
        Loc.T("Las cajas dan varios ítems de una. Hay cinco, de Común a Mítica, y cada una dice cuántos da: **/shop view** muestra los rangos y los precios. Además hay cajas especiales que no se compran: las sueltan los jefes."),
        (Loc.T("📦 Abrirlas"), Loc.F($"**/open** elegís la caja y cuántas (de 1 a {BoxModule.MaxOpenAtOnce} por vez). En texto: `aa open <caja> 3`.")),
        (Loc.T("🛒 Conseguirlas"),
            Loc.F($"Se compran en la **/taberna** o con **/shop buy**: **{ShopCatalog.BoxesPerPurchase} por compra y una compra cada {Dur(CooldownCatalog.BoxBuy.Duration)}**, y solo de las zonas que ya desbloqueaste (🔒 si todavía no). ") +
            Loc.T("Los jefes sueltan el cofre de su zona, y las misiones y logros también dan cajas. **No se pueden revender.**")),
        (Loc.T("🎁 Qué traen"),
            Loc.T("Madera y minerales, drops de las zonas que ya desbloqueaste, comida, cajas más chicas y, muy de vez en cuando, un poco de oro. La **Arca del Soberano** (Mítica) no se compra: la dan solo los logros más difíciles.")));

    private static Embed Shop() => Topic(Self("shop"),
        Loc.T("Acá se compra y se vende todo lo que no se forja, y se cura la vida."),
        (Loc.T("🍺 /taberna (aa taberna)"), Loc.T("El tabernero te atiende con menús: comés algo de tu mochila, comprás comida y cajas, y vendés. Cada elección es de **una** unidad. También vende la **Comida para Mascotas** (**/info tema:pets**).")),
        (Loc.T("🛒 /shop"), Loc.T("**view** muestra los precios, **buy** y **sell** compran y venden por nombre y cantidad, **sellall** vende todo lo vendible de la mochila (no lo que llevás puesto). En texto, sin escribir shop: `aa sell all`, `aa sell arma`, `aa sell amuleto` (lo que llevás puesto) o `aa sell <ítem>`.")),
        (Loc.T("🍖 Consumibles"),
            Loc.T("La comida cura HP y los **banquetes** (los Míticos) además dan un bonus de ataque por un rato: se comen con **/use** (el valor figura en **/shop view** y en tu **/profile** mientras dura; uno nuevo reemplaza al anterior).")),
        (Loc.T("💊 /heal"), Loc.T("Te cura **todo** lo que te falta comiendo lo justo de tu mochila, sin pasarte (no sirve en combate ni gasta banquetes). Con **/heal comida:<nombre>** usás una sola comida.")));

    private static Embed Missions() => Topic(Self("missions"),
        Loc.T("Dos formas de ganar oro, EXP y cajas jugando normalmente. El progreso se cuenta solo; vos reclamás."),
        (Loc.T("📋 /missions"),
            Loc.F($"{MissionCatalog.DailyCount} misiones por día y {MissionCatalog.WeeklyCount} por semana, iguales para todos. Se reinician a la medianoche de Uruguay (la semana, el lunes). Reclamás con el botón.")),
        (Loc.T("🏆 /achievements"),
            Loc.F($"{AchievementCatalog.All.Count} logros de 3 tramos, en {AchievementCatalog.Categories.Count} páginas por tema ({string.Join(", ", AchievementCatalog.Categories.Select(c => c.Name))}); elegís la página con el desplegable y reclamás con el botón. ") +
            Loc.T("Cuentan desde que se activó el registro de eventos.")),
        (Loc.T("💰 Los premios"), Loc.T("Crecen con tu zona: oro como cacerías de oro de tu zona, un % de EXP y a veces una caja. Los logros nuevos pagan solo oro y EXP.")));

    private static Embed Arena() => Topic(Self("arena"),
        Loc.T("Dos formas de pelear contra otros jugadores."),
        (Loc.T("🏟️ /arena (el torneo diario)"),
            Loc.F($"Un torneo por día, hora de Uruguay. Te anotás con **/arena join** durante el día y a la medianoche se juega solo, en llave de eliminación ({ArenaRules.MinPlayers} a {ArenaRules.MaxPlayers} anotados; con menos de {ArenaRules.MinPlayers} se cancela). ") +
            Loc.F($"El campeón cobra {ArenaRules.ChampionReward.GoldUnits} cacerías de oro de su zona, {ArenaRules.ChampionReward.XpPercent} % de EXP y una caja. **/arena listplayers** muestra los anotados, **/arena results** la última llave, y **/cd** te recuerda si ya te anotaste.")),
        (Loc.T("🍀 El azar de la Arena"),
            Loc.F($"El nivel y el equipo pesan pero **no deciden**: cada pelea tiene su **forma del día** (entre −{(int)Math.Round(ArenaTuning.FormSwing * 100)} % y +{(int)Math.Round(ArenaTuning.FormSwing * 100)} % de daño y vida) y en {(int)Math.Round(ArenaTuning.MiracleChance * 100)} de cada 100 peleas hay un **milagro** que iguala a los dos. ") +
            Loc.T("Un nivel 1 puede ganarle a un nivel 20 (pasa pocas veces, pero pasa). El torneo se cuenta en el canal pelea por pelea, y las semifinales y la final, golpe por golpe. Los duelos de **/fight** no tienen azar extra.")),
        (Loc.T("⚔️ /fight @jugador"),
            Loc.F($"Duelo amistoso por turnos (Atacar, habilidad o rendirte; {(int)DuelService.TurnTimeout.TotalSeconds} segundos por turno). No se juega nada: ni vida, ni oro, ni EXP. **/duels** muestra tu récord.")),
        (Loc.T("🛡️ Con qué se pelea"), Loc.T("Con tu nivel, tu arma, tu amuleto y tus encantamientos de ese momento. Las clases tienen un ajuste de vida propio para el PvP.")));

    private static Embed Trade() => Topic(Self("trade"),
        Loc.T("Cambiás materiales con otro jugador, de a uno por uno."),
        (Loc.T("🤝 Cómo"), Loc.T("**/trade @jugador** y elegís lo que das y lo que querés. En texto: `aa trade @jugador \"Hierro\" \"Carbón\"`.")),
        (Loc.T("📏 Las reglas"),
            Loc.T("Solo **madera y minerales**, de la **misma rareza** y distintos entre sí (Roble por Hierro, Pino por Piedra...). Nada de drops, comida ni equipo.")),
        (Loc.T("⏱️ La oferta"), Loc.F($"El otro jugador acepta o rechaza con botones. Dura {(int)TradeOfferService.Lifetime.TotalMinutes} minutos y tenés una abierta a la vez.")),
        (Loc.T("🍺 ¿Y con el tabernero?"), Loc.F($"Los **drops de monstruos** se cambian con él, de a {DropExchange.GiveAmount} por {DropExchange.GetAmount}: **/info tema:exchange**.")));

    private static Embed Exchange() => Topic(Self("exchange"),
        Loc.F($"El tabernero te cambia **{DropExchange.GiveAmount} drops de una zona por {DropExchange.GetAmount} de esa misma zona**: para cuando te sobra de uno y te falta otro, o la suerte no te acompañó."),
        (Loc.T("🔁 Cómo"),
            Loc.F($"**/exchange** con **dar** (el drop que entregás, de a {DropExchange.GiveAmount}), **recibir** (otro drop de la misma zona) y **veces** (de 1 a {DropExchange.MaxTimes} cambios juntos). Las listas te muestran solo lo que podés hacer.\n") +
            Loc.T("En texto: `aa exchange \"Pluma de Ñandú\" \"Cuero Grueso\" 2`. En la **/taberna** hay una lista \"Cambiar drops\" para hacer un cambio de una.")),
        (Loc.T("📏 Las reglas"),
            Loc.T("Solo **drops de monstruos** (los de cacería y de viaje de cada zona), siempre de la **misma zona** y a uno **distinto** del que das. No sirve para madera ni minerales (eso es entre jugadores: **/info tema:trade**), ni para cajas.")),
        (Loc.T("⚖️ ¿Conviene?"),
            Loc.F($"Es {DropExchange.GiveAmount} por {DropExchange.GetAmount}: compensa la mala racha, no reemplaza al farmeo. Si el drop que te falta lo podés conseguir jugando, sale más barato jugar.")));

    private static Embed Fogon() => Topic(Self("fogon"),
        Loc.T("Al vencer al jefe de la **última zona** se abre **El Fogón Eterno**, la «Zona 0»: una puerta al final del mundo y el último paso antes del **Fuego Nuevo** (volver a empezar con más velocidad, tus mascotas y tu oro)."),
        (Loc.T("🚪 Cómo se entra"),
            Loc.F($"Con **/zona 0** (en texto `aa zona 0`). Hace falta haber vencido al jefe de la última zona, tener el nivel de la puerta (figura en **/zonas**) y llevar **puestos** el **{FogonRules.WeaponName}** y la **{FogonRules.AmuletName}**: uno solo de cada uno, iguales para todas las clases (sin sinergia de clase).")),
        (Loc.T("⚒️ El equipo"),
            Loc.T("Se forja en la herrería (**/forge**) y es lo más caro del juego: pide los **drops de cacería de las 5 zonas**, bastante madera y mineral raros, un material **Mítico** de recolección en cada pieza (la **Corteza del Árbol de Vida** en el arma y el **Fragmento de Meteorito** en el amuleto: salen con 0,5 % al **/chop** y al **/mine**, así que andá juntándolos desde el principio y no los desmanteles) y mucho oro. **/forge-special** te muestra las dos recetas DESDE EL PRINCIPIO (con lo que ya tenés de cada cosa) para que no desmanteles ni vendas lo que vas a necesitar; lo que te falta primero lo ves en **/tips**. ") +
            Loc.T("Como el equipo va en su casillero, hay que **vender primero el de la zona anterior** (sus encantamientos se pierden: es el último equipo, el Fuego Nuevo lo borra de todos modos).")),
        (Loc.T("⚔️ Adentro"),
            Loc.F($"Solo **/boss**, contra **{FogonRules.BossName}**: no hay cacería, viajes ni grupo. Usa el cooldown del jefe (**{Dur(CooldownCatalog.Boss.Duration)}** si ganás y **{Dur(CooldownCatalog.Boss.RetryAfterFailure ?? CooldownCatalog.Boss.Duration)}** si perdés, huís o se acaba el tiempo) y si perdés pagás la penalidad de siempre (**/info tema:death**). ") +
            Loc.T("Está calibrado como cada jefe: con el equipo de la zona anterior casi siempre perdés, con el del Fogón es un desafío parejo.")),
        (Loc.T("🕯️ ¿Te falta un Mítico?"),
            Loc.F($"La Corteza y el Meteorito salen al {Dec(RarityCatalog.GatheringChances.Last(c => c.Rarity == "Mítico").Percent)} % por **/chop** y **/mine**, y hace falta uno de cada uno por vuelta. Al vencer al jefe de la última zona se conoce al **Tabernero del Inframundo** (**/deepshop**, en texto `aa deepshop`): vende cada uno por **{Num(DeepShop.PricePerUnit)}** de Polvo (el Polvo sale de **/dismantle**; desmantelar un Mítico da solo {Num(Dismantling.DustPerUnit("Mítico"))}). Una vez que hacés un Fuego Nuevo ya lo conocés para siempre.")),
        (Loc.T("🏛️ Desde el Fuego Nuevo 5"),
            Loc.T("El Fogón **se corre**: queda pasando tu última zona (la 6, la 7… según tus Fuegos Nuevos). Ya no se entra con **/zona 0** ni hace falta el Trinche y la Brasa: se llega ganando la **mazmorra** de tu última zona, y del otro lado te espera el Asador, **más fuerte** (cuanto más lejos, más), con la vida que te dejó la mazmorra, **sin comida** y con su **llamarada** (a veces pega mucho más). Más en **/info tema:zone**.")),
        (Loc.T("🏆 Al ganar"), Loc.T("Volvés a la última zona, se habilita el **Fuego Nuevo** (**/fuegonuevo**, y cómo funciona en **/info tema:fuego**) y sumás el logro **Asador**. Para salir sin pelear, viajá a cualquier zona con **/zona**.")));

    private static Embed Professions()
    {
        var woodcutter = ProfessionCatalog.Get(ProfessionCatalog.WoodcutterKey);
        var miner = ProfessionCatalog.Get(ProfessionCatalog.MinerKey);
        var fisher = ProfessionCatalog.Get(ProfessionCatalog.FisherKey);
        var enchanter = ProfessionCatalog.Get(ProfessionCatalog.EnchanterKey);
        long total = ProfessionRules.TotalXpToMax;

        return Topic(Self("professions"),
            Loc.T("Los **oficios** suben solos cuando usás su comando: cuanto más talás, minás, pescás o encantás, mejor te sale. Cada uno empieza en el nivel 0 y lo que da llega al tope en el ") + ProfessionRules.MaxLevel + Loc.T(", pero el nivel **sigue subiendo** después."),
            (Loc.T("📈 Cómo suben"),
                Loc.F($"{woodcutter.Emoji} **{woodcutter.DisplayName}** con **{woodcutter.Command}** y {miner.Emoji} **{miner.DisplayName}** con **{miner.Command}**: cada tirada da XP según la rareza del material que te sale ({ProfessionRules.DescribeGatherXp()}; una por tirada, no por unidad).\n") +
                Loc.F($"{fisher.Emoji} **{fisher.DisplayName}** con **{fisher.Command}**: lo mismo, pero con los tres peces ({ProfessionRules.DescribeFishXp()}).\n") +
                Loc.F($"{enchanter.Emoji} **{enchanter.DisplayName}** con cada intento de **{enchanter.Command}** ({enchanter.XpPerAction} XP).\n") +
                Loc.F($"Llegar al {ProfessionRules.MaxLevel} son ~{GameHistory.Number(total)} de XP: unas {GameHistory.Number((long)Math.Round(total / ProfessionRules.AverageGatherXp()))} tiradas de tala o minería (en promedio), o {GameHistory.Number(total / enchanter.XpPerAction)} intentos de encantamiento. ") +
                Loc.T("Lo que hiciste antes de que existieran los oficios también cuenta.")),
            (Loc.T("🎁 Lo que da cada nivel"),
                Loc.F($"{woodcutter.Emoji} {miner.Emoji} {fisher.Emoji} **{woodcutter.DisplayName}, {miner.DisplayName} y {fisher.DisplayName}**: {ProfessionRules.DescribePerLevel(woodcutter)}. Lo de la rareza sube un escalón (Común → Raro → Épico → Legendario); el Mítico nunca se mejora.\n") +
                Loc.F($"{enchanter.Emoji} **{enchanter.DisplayName}**: {ProfessionRules.DescribePerLevel(enchanter)}.\n") +
                Loc.T("Todo suma a lo de Fuego Nuevo y las bendiciones.")),
            (Loc.F($"⭐ Al nivel {ProfessionRules.MaxLevel}"),
                $"{ProfessionRules.DescribeAdvanced(woodcutter)}.\n{ProfessionRules.DescribeAdvanced(miner)}.\n{ProfessionRules.DescribeAdvanced(fisher)}.\n{ProfessionRules.DescribeAdvanced(enchanter)}."),
            (Loc.F($"📈 Después del {ProfessionRules.MaxLevel}"),
                Loc.F($"Los efectos ya no crecen, pero el nivel sí: cada uno cuesta un **{ProfessionRules.DescribePostCapGrowth()}** más que el anterior (el {ProfessionRules.MaxLevel + 1} pide {GameHistory.Number(ProfessionRules.XpForLevel(ProfessionRules.MaxLevel + 1))} de XP, el {ProfessionRules.MaxLevel + 50} pide {GameHistory.Number(ProfessionRules.XpForLevel(ProfessionRules.MaxLevel + 50))}). Por ahora ese avance no da ningún efecto nuevo.")),
            (Loc.T("🔄 Y el Fuego Nuevo"), Loc.T("Los oficios **se quedan** cuando hacés un Fuego Nuevo: tu nivel no se pierde.")),
            (Loc.T("📍 Comandos"), Loc.T("**/professions** (en texto `aa professions` o `aa oficios`) muestra tu nivel, tu XP y lo que ya te dan. También figuran en **/profile** y el avance sale al pie de cada **/chop**, **/mine**, **/fish** y **/enchant**.")));
    }

    private static Embed Fuego() => Topic(Self("fuego"),
        Loc.T("El **Fuego Nuevo** es volver a empezar, pero más rápido: perdés el nivel, el equipo y los materiales y ganás **porcentajes permanentes** y una **bendición** por cada vuelta. ") +
        Loc.T("Es voluntario y se habilita al vencer al **Asador Eterno** en **El Fogón Eterno** (**/info tema:fogon**)."),
        (Loc.T("🔥 Cómo se hace"),
            Loc.T("**/fuegonuevo** (en texto `aa fn`) muestra lo que se va, lo que se queda y lo que ganás, con el botón para hacerlo. Después elegís la **clase** de la vuelta nueva: tocarla confirma y no se deshace. ") +
            Loc.T("No se puede en pleno combate o con un grupo armado, y cada vuelta pide volver a vencer al Asador (el equipo del Fogón se forja de nuevo).")),
        (Loc.T("📈 Lo que ganás con cada uno"),
            Loc.F($"🏹 Chance de drop en **/hunt**: **{FuegoNuevoRules.PercentText(1 + FuegoNuevoRules.HuntDropStep)}** sobre la base por cada Fuego Nuevo (lineal: el 10.º es {FuegoNuevoRules.PercentText(FuegoNuevoRules.HuntDropMultiplier(10))}, no se compone)\n") +
            Loc.F($"🗺️ Chance de caja en **/travel**: arranca en **{FuegoNuevoRules.PercentText(1 + FuegoNuevoRules.TravelStep(1))}** y cada vuelta suma un poco menos (el 10.º acumula {FuegoNuevoRules.PercentText(FuegoNuevoRules.TravelDropMultiplier(10))}, con piso de {FuegoNuevoRules.PercentText(1 + FuegoNuevoRules.TravelStep(1000))} por vuelta)\n") +
            Loc.F($"🪓⛏️ Cantidad por **/chop** y **/mine**: **{FuegoNuevoRules.PercentText(1 + FuegoNuevoRules.GatherStep)}** por Fuego Nuevo (el 10.º es ×{FuegoNuevoRules.GatherMultiplier(10).ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',')})\n") +
            Loc.F($"📊 EXP de las peleas: **{FuegoNuevoRules.PercentText(FuegoNuevoRules.XpMultiplier(1))}** el primero y cada vuelta suma más que la anterior (el 5.º acumula {FuegoNuevoRules.PercentText(FuegoNuevoRules.XpMultiplier(5))}, el 10.º {FuegoNuevoRules.PercentText(FuegoNuevoRules.XpMultiplier(10))})\n") +
            Loc.F($"Con la chance base de /hunt ({CombatRewardCalculator.HuntDropChancePercent} %); la de la caja de /travel sube en la misma proporción. El oro y el cofre del jefe no cambian.")),
        (Loc.T("💨 Lo que se va"),
            Loc.T("Nivel y EXP, la zona y los jefes vencidos, el arma y el amuleto (con sus encantamientos), todos los materiales, drops, cajas y consumibles, el Polvo y los banquetes. Sin reembolso (los cooldowns siguen como estaban).")),
        (Loc.T("🔒 Lo que se queda"),
            Loc.T("El oro (billetera y banco), las mascotas con sus huevos y su comida, los logros, las misiones cobradas, la racha diaria, las bendiciones y tus Fuegos Nuevos.")),
        (Loc.T("🙏 Bendiciones"),
            Loc.F($"Una por vuelta, elegida entre {BlessingCatalog.OfferSize} sorteadas ({BlessingCatalog.All.Count} en total); si repetís una sube de nivel hasta el {BlessingCatalog.RomanLevel(BlessingCatalog.MaxLevel)}. ") +
            Loc.T("Las hay de velocidad (drop, cantidad, EXP), de poder chico (ataque, defensa y vida, solo contra monstruos), de mascotas, de oro, de ítems y cosméticas. **/blessings** las muestra todas.")),
        (Loc.T("⚖️ Reglas"), Loc.T("Los duelos y la Arena no usan nada de esto: se juegan con nivel, equipo y clase. La clase solo se elige empezando de cero y con cada Fuego Nuevo (**/class**).")));

    private static Embed Story() => Topic(Self("story"),
        Loc.T("Las **Crónicas del Fogón** son la historia del juego: la cuenta el Tabernero junto al fuego y se va abriendo mientras jugás. No da poder (ni stats, ni oro, ni drops): da sentido. ") +
        Loc.T("**/story** (en texto `aa historia`) abre el índice con una lista para leer lo que ya abriste."),
        (Loc.T("📖 Acto I · Subir"),
            Loc.F($"El prólogo y un capítulo por cada jefe de zona (los {Lore.LadderChapters} de la escalera) más el del Asador Eterno. Cada uno se abre la **primera vez** que lo vencés (**/boss**, solo o en equipo) y te llega un aviso aparte. ") +
            Loc.T("Lo que todavía no abriste sale 🔒 sellado, sin título, para que nadie se spoilee.")),
        (Loc.T("🔥 Acto II · Soltar"),
            Loc.F($"El Asador carga con un pesar enorme. Cada **Fuego Nuevo** (**/info tema:fuego**) le saca un 1 %: con {Lore.FinalFuegoNuevo} vueltas queda libre. En ciertas vueltas se abre una escena nueva ") +
            Loc.T("y, desde la segunda, el Herrero, el Tabernero y el propio Asador hablan distinto. Hay logros en esas vueltas (**/achievements**).")),
        (Loc.T("🪑 Acto III"),
            Loc.F($"Se abre en el Fuego Nuevo **{Lore.FinalFuegoNuevo}**. Qué es, lo descubrís llegando.")));

    private static Embed Pets() => Topic(Self("pets"),
        Loc.T("Cada zona tiene su **mascota**. Las que tengas valen **todas a la vez**: no ocupan lugar, no se pierden y no hay que sacarlas a pasear."),
        (Loc.T("🥚 Cómo se consiguen"),
            Loc.T("La **primera vez** que vencés al jefe de una zona (**/boss**, solo o en equipo) te llega un **huevo** junto con el cofre. Lo abrís con **/open** y nace la mascota de esa zona: una por zona. Cuáles son y qué dan lo descubrís jugando.")),
        (Loc.T("🎁 Qué dan"),
            Loc.T("Cada especie da un bonus distinto, por ejemplo **más oro**, **más EXP**, **más defensa** en las peleas contra monstruos o **más chances de drop** en cacería y viaje. ") +
            Loc.F($"Es relativo: un +6 % sobre un {CombatRewardCalculator.HuntDropChancePercent} % de drop da {Dec(CombatRewardCalculator.HuntDropChancePercent * 1.06)} %. El cofre del jefe no cambia, y en duelos y Arena no cuentan. ") +
            Loc.T("Mirá cuál da cada una (y cuánto) con **/pet view**.")),
        (Loc.T("🍖 Cómo crecen"),
            Loc.F($"Comen **{PetRules.FoodItemName}** (se compra en la **/taberna**). Cada mascota puede comer **una vez por hora**; **/pet feed** alimenta a todas las que estén listas o elegís una. ") +
            Loc.F($"Tienen nivel de 1 a {PetRules.MaxLevel} y el bonus crece con el nivel (nivel 1 = el 10 % de su tope, nivel {PetRules.MaxLevel} = el tope entero). Llegar al máximo lleva {PetRules.TotalFeedsToMax} comidas por mascota. ") +
            Loc.T("Y en el máximo todavía hay más: una mascota puede **evolucionar** (**/pet evolve**) y seguir creciendo. Descubrí qué hace falta cuando llegue el momento.")),
        (Loc.T("🧭 Expediciones"),
            Loc.F($"Una mascota en el **nivel {PetExpeditions.RequiredLevel}** puede salir de expedición por **{string.Join(", ", PetExpeditions.Options.Select(o => o.Hours))} horas** mientras hacés otra cosa (**/pet expedition**). Gasta **{string.Join(" / ", PetExpeditions.Options.Select(o => o.FoodCost))}** {PetRules.FoodItemName} según la duración, y sale de a **una por vez**. ") +
            Loc.T("Cuando vuelve, el mismo comando recoge lo que trajo: cuanto más tiempo está afuera, más trae. Qué trae exactamente, descubrilo vos. Te avisa cuando vuelve si tenés los avisos prendidos (**/reminders**).")),
        (Loc.T("📍 Comandos"), Loc.T("**/pet view** las muestra, **/pet feed** las alimenta, **/pet expedition** las manda de expedición y **/open** abre el huevo. En texto: `aa pet`, `aa pet feed` y `aa pet expedition`. También figuran en **/profile**.")));

    private static Embed Dragon()
    {
        string kinds = string.Join('\n', DragonRules.Kinds.Select(k =>
            Loc.F($"{DragonRules.KindEmoji(k)} **{DragonRules.KindName(k)}**: hasta **+{DragonRules.Percent(DragonRules.MaxPercent(k))} %** {DragonRules.KindEffect(k)} en el tier {DragonRules.MaxTier}")));
        string chances = string.Join(" / ", Enumerable.Range(1, DragonRules.MaxTier - 1).Select(t => DragonRules.Percent(Math.Round(DragonRules.BreedChance(t) * 100, 2))));
        string pity = string.Join(" / ", Enumerable.Range(1, DragonRules.MaxTier - 1).Select(DragonRules.PityCap));

        return Topic(Self("dragon"),
            Loc.T("Un **dragón** por cuenta que te mejora **contra los monstruos**. Se compra, se entrena con oro y se cría (solo o con un amigo) para subirlo de tier. Dura toda la vida de la cuenta: el **Fuego Nuevo** no lo toca."),
            (Loc.T("🥚 Conseguirlo"),
                Loc.F($"En la **/taberna** o con **/shop buy dragon**: **{GameHistory.Number(DragonRules.ShopPrice)}** de oro, una sola vez. Nace de tier 1 (**{DragonRules.TierName(1)}**) con un tipo al azar:\n{kinds}\n") +
                Loc.F($"El **Especial** es el raro ({DragonRules.ShopWeight(DragonRules.Special)} %); los otros salen {DragonRules.ShopWeight(DragonRules.Strength)} % cada uno. No cuenta en duelos ni en la Arena.")),
            (Loc.T("📈 Tiers y mejoras"),
                Loc.F($"Tiene **{DragonRules.MaxTier} tiers** (de **{DragonRules.TierName(1)}** a **{DragonRules.TierName(DragonRules.MaxTier)}**). El bonus de su tipo crece en cada uno (poco al principio, mucho al final) y **cada tier le suma una mejora nueva**: más drops, más recolección, el **Aliento final** (una vez por pelea, un golpe que te mataría te deja en 1 de vida), más vida, EXP u oro... Las ves todas, con las que ya desbloqueó, en **/dragon**.")),
            (Loc.T("🏋️ Entrenarlo"),
                Loc.F($"**/dragon-train** (o el botón de **/dragon**; en texto `aa dragon train [n]`): gastás oro y sube de nivel. Cada nivel cuesta más que el anterior (el primero {GameHistory.Number(DragonRules.TrainCost(0))}, el décimo unos {GameHistory.Number(DragonRules.TrainCost(9))}) y suma **{DragonRules.Percent(DragonRules.LevelShare * 100)} %** del máximo de su tipo. Llega hasta el nivel **tier × 10**: subirlo de tier abre 10 niveles más.")),
            (Loc.T("💞 Criarlo"),
                Loc.F($"**/dragon-breed @amigo** (en texto `aa dragon breed @amigo`): los dos con el **mismo tier**; el otro acepta con un botón. Cada intento los hace descansar **{Dur(DragonRules.BreedCooldown)}** (**/cd** dice cuánto falta). **Cada dragón tira su propia suerte**: puede subir uno y el otro no (ya no pueden criar entre ellos hasta que el otro lo alcance).\n") +
                Loc.F($"Chance de subir del tier 1 al 10: **{chances} %**. Con la **piedad**, tras {pity} fallos seguidos (uno por tier) el siguiente intento sale **seguro**.\n") +
                Loc.T("Solo, sin amigo (**/dragon-breed** a secas): **un tercio** de la chance y el **triple** de fallos para el intento seguro.")),
            (Loc.T("🍖 Tipo y comida"),
                Loc.F($"En **cada cría** (salga bien o mal) tu dragón **cambia de tipo al azar**: puede tocarte el mismo, y los niveles entrenados se quedan y rinden según el tipo nuevo. Para **conservar el tipo**, tené **{ItemEmojiRegistry.Label(DragonRules.FoodItemName, string.Empty)}** en la mochila: se gasta **una por cría**, sola. Se compra en la **/taberna**. En la invitación a criar, el 🍖 al lado del nombre dice quién tiene.")),
            (Loc.T("📌 Tablón y aviso"),
                Loc.F($"**/dragon-board** (en texto `aa dragon board`) es el tablón de crías de tu servidor: **anotate** para que los demás de tu mismo tier sepan que buscás pareja (dura {(int)DragonBoardService.Lifetime.TotalHours} horas o hasta que críes) y mirá quién busca; elegís a alguien de la lista y le llega la invitación a criar. Solo aparecen los que pueden criar ahora: para anotarte tu dragón tiene que poder criar ya (si descansa, te dice cuánto falta). ") +
                Loc.F($"Además el bot te **avisa** cuando termina el descanso de la cría ({Dur(DragonRules.BreedCooldown)}); se apaga con **/reminders**.")),
            (Loc.T("✏️ Nombre"),
                Loc.F($"**/dragon-name** le pone (o le cambia) el nombre por **{GameHistory.Number(DragonRules.RenamePrice)}** de oro cada vez; en texto `aa dragon name <nombre>`. Sale en tu **/profile**.")));
    }

    private static Embed Reminders()
    {
        var waits = ReminderCatalog.All.Select(k => ReminderCatalog.WaitOf(k.Key) is { } w
            ? Loc.F($"{k.Emoji} **{Loc.Tr(k.Name)}** — cada {ReminderCatalog.WaitLabel(w)}")
            : Loc.F($"{k.Emoji} **{Loc.Tr(k.Name)}** — cuando vuelve (1, 4 u 8 h)"));
        int shortMinutes = (int)ReminderCatalog.ShortLived.TotalMinutes;

        return Topic(Self("reminders"),
            Loc.T("El bot te **avisa solo** cuando termina una espera, así no tenés que andar mirando **/cd**."),
            (Loc.T("🔔 Cuándo avisa"),
                Loc.T("Después de usar un comando que deja una espera, el bot te escribe en **ese mismo canal** cuando termina:\n") + string.Join('\n', waits)),
            (Loc.T("🧹 Sin ensuciar el canal"),
                Loc.F($"Los de **cacería, talar y minar** (esperas cortas) se borran solos a los {shortMinutes} minutos, y no avisan si justo estás peleando o acabás de usar un comando. Los demás se quedan. ") +
                Loc.T("Si terminan varias esperas juntas, llegan en un solo mensaje. Si peleaste el jefe en equipo, el aviso les llega a todos los que pelearon.")),
            (Loc.T("⚙️ Elegir cuáles"),
                Loc.T("**/reminders** (en texto `aa recordatorios` o `aa avisos`) abre una lista donde marcás cuáles querés recibir, con botones para recibir o silenciar todos. Al apagar uno se borra el aviso que estaba pendiente.")),
            (Loc.T("📌 Cosas a saber"),
                Loc.T("El aviso se arma cuando usás el comando y sobrevive a un reinicio del bot; si el bot estuvo apagado y el aviso llega muy tarde, se descarta. Para las **mascotas** avisa cuando la primera que pueda subir de nivel puede comer de nuevo. Para el **dragón**, cuando termina el descanso de la cría.")));
    }

    private static Embed Vote() => Topic(Self("vote"),
        Loc.T("Votar en **top.gg** ayuda a que más gente encuentre el juego, y te devolvemos el favor."),
        (Loc.T("🎁 Qué da cada voto"),
            Loc.F($"**{VoteRules.RewardBoxes}× {ItemEmojiRegistry.Label(VoteRules.RewardBoxName, string.Empty)}** (se abre con **/open**) y **el jefe se resetea**: si el cooldown de **/boss** (solo o en equipo) ({Dur(CooldownCatalog.Boss.Duration)} tras ganar) estaba corriendo, queda listo al instante.")),
        (Loc.T("🗳️ Cómo se cobra"),
            Loc.T("**/vote** (en texto `aa vote`) te muestra el link para votar. Después de votar, escribí **/vote** otra vez: el bot le pregunta a top.gg y te paga. Cada voto se cobra **una sola vez**, y si no lo cobrás antes de que venza (unas 12 horas) se pierde.")),
        (Loc.T("⏳ Cada cuánto"),
            Loc.F($"Se puede votar cada **{Dur(VoteRules.Interval)}**; **/cd** te dice cuánto falta y el bot te avisa cuando ya podés (**/reminders**). Mientras no esté habilitado el voto, **/vote** solo cuenta qué te llevarías.")),
        (Loc.T("💡 Truco"), Loc.T("El reseteo rinde más si cobrás el voto justo después de ganarle al jefe: así te ahorrás la espera. Si el jefe ya estaba listo, igual te llevás la caja.")));

    private static Embed Classes()
    {
        var embed = new EmbedBuilder()
            .WithTitle($"{Self("classes").Emoji} {Self("classes").Name}")
            .WithColor(OnboardingModule.BrandColor)
            .WithDescription(Loc.T("Elegís tu clase con **/class** (solo empezando de cero, nivel 1: después se vuelve a elegir con cada **Fuego Nuevo**). Cada una tiene una **pasiva** que siempre está y una **habilidad** que se usa con un botón en la pelea manual."));

        foreach (var cls in ClassCatalog.All)
        {
            var passive = ClassPassives.For(cls.Name);
            var ability = ClassAbilities.For(cls.Name);
            string passiveText = cls.Name switch
            {
                "Guerrero" => Loc.F($"+{(int)Math.Round((passive.MaxHpMultiplier - 1) * 100)} % de vida y {(int)Math.Round((1 - passive.DamageTakenMultiplier) * 100)} % menos de daño recibido"),
                "Ninja" => Loc.F($"+{(int)Math.Round(passive.DodgeChanceBonus * 100)} % de esquive"),
                "Arquero" => Loc.F($"+{(int)Math.Round(passive.CritChanceBonus * 100)} % de golpe crítico"),
                "Hechicero" => Loc.F($"{(int)Math.Round(passive.LifestealChance * 100)} % de chance de curarte el {(int)Math.Round(passive.LifestealRatio * 100)} % del daño que hacés (Sifón de Almas)"),
                _ => "—",
            };

            embed.AddField(
                Loc.F($"{cls.Emoji} {cls.Name} — arma: {cls.WeaponType}"),
                Loc.F($"{Loc.Tr(cls.Description)}\n**Pasiva:** {passiveText}.\n**Habilidad:** {(ability is null ? "—" : $"{ability.Emoji} {ability.Name} — {ClassAbilities.DescribeLocalized(ability)}")}"),
                false);
        }

        return embed.WithFooter(Loc.F($"La habilidad se recarga {AbilityTuning.CooldownTurns} turnos y /autohunt no la usa.")).Build();
    }

    private static Embed Gather() => Topic(Self("gather"),
        Loc.F($"**/chop** junta madera y **/mine** minerales. Cooldown de {Dur(CooldownCatalog.Chop.Duration)} cada uno. Son la base de las recetas de la herrería."),
        (Loc.T("🎲 Qué te toca"),
            Loc.T("Primero sale una rareza al azar y después un ítem de esa rareza: ") +
            string.Join(" · ", RarityCatalog.GatheringChances.Select(c => Loc.F($"{RarityCatalog.DisplayName(c.Rarity)} {LDec(c.Percent)} %")))),
        (Loc.T("📦 Cuántas unidades"),
            Loc.T("Cuanto más rara, menos unidades: ") +
            string.Join(" · ", new[] { "Común", "Raro", "Épico", "Legendario", "Mítico" }.Select(r =>
            {
                var (min, max) = GatheringYield.RangeFor(r);
                string rarityName = RarityCatalog.DisplayName(r);
                return min == max ? $"{rarityName} {min}" : Loc.F($"{rarityName} {min} a {max}");
            }))),
        (Loc.T("🎣 Pesca"),
            Loc.F($"**/fish** pesca cada {Dur(CooldownCatalog.Fish.Duration)} y sube el oficio **Pescador**.") + "\n" +
            Loc.T("Solo hay tres peces; la rareza sale así: ") +
            string.Join(" · ", RarityCatalog.FishChances.Select(c => Loc.F($"{RarityCatalog.DisplayName(c.Rarity)} {LDec(c.Percent)} %"))) + "\n" +
            Loc.T("La caña del Carpintero (**/tools**) multiplica lo que sacás. Los peces se venden o se desmantelan para sacar Polvo.")),
        (Loc.T("🍳 Cocina"),
            Loc.T("Con los peces y los materiales raros cocinás platos con **/cook** (las recetas: **aa cook recipes**). El plato se cocina y se come en el momento: suma vida máxima, defensa o ataque y se **apila** hasta el próximo Fuego Nuevo. Solo cuenta contra monstruos.")),
        (Loc.T("🔁 Después"), Loc.T("Lo que juntás sirve para forjar (**/info tema:forge**), para cambiar con otros jugadores (**/info tema:trade**) o para desmantelar y sacar Polvo (**/info tema:dismantle**).")));

    private static Embed Daily() => Topic(Self("daily"),
        Loc.T("Una recompensa por día y la posibilidad de regalar oro."),
        (Loc.T("🎁 /daily"),
            Loc.F($"Una vez cada {Dur(DailyRewardCalculator.MinInterval)}. Cada día seguido suma a la **racha**, y el premio **escala con tu zona y tu nivel**: el oro se mide en cacerías de tu zona y la EXP en % de lo que te falta para subir. ") +
            Loc.F($"Del día 1 al 7 va de {DailyRewardCalculator.BaseGoldUnitsFor(1)} a {DailyRewardCalculator.BaseGoldUnitsFor(DailyRewardCalculator.WeekLength)} cacerías de oro y de {DailyRewardCalculator.XpPercentFor(1)} % a {DailyRewardCalculator.XpPercentFor(DailyRewardCalculator.WeekLength)} % de EXP, y desde el día {DailyRewardCalculator.WeekLength + 1} se queda en el del día {DailyRewardCalculator.WeekLength}. ") +
            Loc.F($"Si pasan más de {Dur(DailyRewardCalculator.StreakGraceWindow)} sin reclamar, la racha vuelve al día 1. **/cd** te dice cuándo podés volver. Tu racha más larga cuenta para el logro **Racha** (**/achievements**).")),
        (Loc.T("🏆 Hitos de la racha"),
            Loc.F($"**Día {DailyRewardCalculator.ThreeDayStreak}**: +{DailyRewardCalculator.ThreeDayBonusUnits} cacerías de oro extra.\n") +
            Loc.F($"**Día {DailyRewardCalculator.IronBoxFromDay}** y todos los días que siguen: la **caja diaria** de tu zona.\n") +
            Loc.F($"**Día {DailyRewardCalculator.WeekLength}** y cada {DailyRewardCalculator.WeekLength} días seguidos ({DailyRewardCalculator.WeekLength * 2}, {DailyRewardCalculator.WeekLength * 3}…): la **caja grande** de tu zona y +{DailyRewardCalculator.WeekBonusUnits} cacerías de oro. ") +
            Loc.T("Las cajas se abren con **/open**; cuáles son depende de tu zona (las mismas que dan las misiones diarias y semanales).")),
        (Loc.T("🤝 /give @jugador cantidad"), Loc.T("Le regalás oro a otro jugador (una cantidad o **all**). Sale de tu billetera; no a vos mismo. En texto: `aa give @jugador 100`.")));

    private static Embed Stats() => Topic(Self("stats"),
        Loc.T("Tu **/profile** muestra todo esto junto."),
        (Loc.T("📊 EXP y nivel"),
            Loc.F($"Para pasar de nivel hace falta {LevelingCalculator.XpPerLevelSquared} × nivel² de EXP (nivel 5: {Num(LevelingCalculator.RequiredXpForLevel(5))}, nivel 10: {Num(LevelingCalculator.RequiredXpForLevel(10))}, nivel 20: {Num(LevelingCalculator.RequiredXpForLevel(20))}). ") +
            Loc.T("La EXP es la del nivel actual: se reinicia cuando subís (y también si perdés un combate: **/info tema:death**).")),
        (Loc.T("⬆️ Al subir de nivel"),
            Loc.F($"+{LevelingCalculator.HpGainedPerLevel} de vida máxima (y te curás entera), +{CombatStats.BaseAttack(2) - CombatStats.BaseAttack(1)} de ataque y +{CombatStats.BaseDefense(2) - CombatStats.BaseDefense(1)} de defensa.")),
        (Loc.T("⚔️ Ataque y defensa"),
            Loc.F($"Ataque = {CombatStats.BaseAttack(0)} + {CombatStats.BaseAttack(2) - CombatStats.BaseAttack(1)} por nivel + tu arma (más su encantamiento). Defensa = tu nivel + tu amuleto (con su encantamiento).")));
}

// La lista desplegable de /info tema: los temas que contienen lo escrito (en su clave, su nombre o sus alias). El valor es la clave, así que escribirla a mano también anda.
public sealed class InfoTopicAutocompleteHandler : SafeAutocompleteHandler
{
    protected override Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services)
    {
        IReadOnlyList<AutocompleteResult> results = HelpTopics.Search(typed)
            .Take(AutocompleteText.MaxChoices)
            .Select(t => new AutocompleteResult(AutocompleteText.Truncate($"{t.Emoji} {t.Name} ({t.Key}) — {t.Summary}"), t.Key))
            .ToList();
        return Task.FromResult(results);
    }
}
