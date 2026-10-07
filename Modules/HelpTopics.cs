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
public sealed record HelpTopic(string Key, string Emoji, string Name, string Summary, IReadOnlyList<string> Aliases, Func<Embed> Build);

public static class HelpTopics
{
    public static readonly IReadOnlyList<HelpTopic> All =
    [
        new("enchant", "✨", "Encantamientos", "tiers, chances y bonus para tu arma y tu amuleto", ["encantar", "encantamientos", "encantamiento", "encanto", "encantos", "enchants"], Enchant),
        new("dismantle", "🧰", "Desmantelar y Polvo", "romper materiales para juntar Polvo", ["polvo", "dust", "desmantelar", "desarmar"], Dismantle),
        new("bank", "🏦", "El banco", "guardar oro a salvo de la penalidad", ["banco", "guardar"], Bank),
        new("death", "☠️", "Perder un combate", "la penalidad por morir y cómo evitarla", ["muerte", "morir", "penalidad", "derrota", "perder"], Death),
        new("play", "🎰", "El casino", "coinflip y slots: cuánto paga cada uno", ["casino", "slots", "coinflip", "apostar", "juego"], Play),
        new("hunt", "🏹", "Combate", "cazar, viajar, huir, curarte y el modo automático", ["combate", "cazar", "caceria", "travel", "viaje", "autohunt", "pelea", "pelear", "combat"], Hunt),
        new("boss", "👑", "Jefes y raids", "el jefe de cada zona y cómo hacerlo con amigos", ["jefe", "jefes", "raid", "raids"], Boss),
        new("forge", "🔨", "Herrería y equipo", "forjar armas y amuletos, y cambiarlos", ["herrero", "herreria", "forja", "forjar", "recetas", "receta", "equipo", "arma", "armas", "amuleto"], Forge),
        new("zone", "🗺️", "Zonas", "cómo se avanza por el mundo", ["zona", "zonas", "mundo", "mapa"], Zone),
        new("boxes", "📦", "Cajas", "qué dan, cómo se consiguen y cada cuánto", ["caja", "cajas", "open", "abrir", "cofre", "cofres"], Boxes),
        new("shop", "🍺", "Tienda, taberna y curarte", "comida, banquetes, /heal y vender", ["tienda", "taberna", "comida", "consumibles", "consumible", "heal", "curar", "curarse", "vender", "comprar"], Shop),
        new("missions", "📋", "Misiones y logros", "qué se reclama, cuándo se reinicia y cuánto pagan", ["mision", "misiones", "logro", "logros", "achievements", "missions"], Missions),
        new("arena", "🏟️", "Arena y duelos", "el torneo diario y los duelos con amigos", ["pvp", "duelo", "duelos", "fight", "torneo"], Arena),
        new("trade", "🤝", "Intercambio", "cambiar materiales con otro jugador", ["cambio", "cambiar", "trueque", "intercambio"], Trade),
        new("exchange", "🔁", "Cambiar drops con el tabernero", "3 drops de una zona por 1 de la misma", ["canje", "canjear", "swap", "tabernero", "cambalache"], Exchange),
        new("fogon", "🔥", "El Fogón Eterno", "la puerta del final del mundo y su equipo", ["fogón", "asador", "puerta", "zona0", "zona 0", "gate"], Fogon),
        new("fuego", "♻️", "Fuego Nuevo", "volver a empezar con bonus permanentes y bendiciones", ["fuegonuevo", "fuego nuevo", "fn", "reinicio", "reiniciar", "renacer", "reset", "vuelta", "bendicion", "bendiciones", "blessing", "blessings", "prestigio"], Fuego),
        new("professions", "🛠️", "Oficios", "Leñador, Minero y Encantador: nivel, XP y la versión avanzada", ["oficios", "oficio", "profesion", "profesiones", "prof", "lenador", "leñador", "woodcutter"], Professions),
        new("pets", "🐾", "Mascotas", "huevos, bonus pasivos y cómo alimentarlas", ["mascota", "mascotas", "pet", "huevo", "huevos", "egg", "eggs"], Pets),
        new("classes", "🎭", "Clases y habilidades", "qué hace cada clase", ["clase", "clases", "class", "habilidad", "habilidades"], Classes),
        new("gather", "🪓", "Recolección", "talar y minar: unidades y rarezas", ["chop", "mine", "talar", "minar", "recolectar", "recoleccion", "madera", "mineral"], Gather),
        new("daily", "🎁", "Diario y regalos", "la recompensa diaria, la racha y regalar oro", ["diario", "racha", "give", "dar", "regalo", "regalar"], Daily),
        new("stats", "📊", "Nivel y estadísticas", "EXP, vida, ataque y defensa", ["nivel", "exp", "xp", "ataque", "defensa", "vida", "hp", "perfil", "level"], Stats),
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

        return Find(text)?.Build() ?? BuildIndexEmbed(text.Trim());
    }

    // La lista de temas (con una línea de qué explica cada uno). unknown: lo que escribió quien pidió un tema que no existe.
    public static Embed BuildIndexEmbed(string? unknown = null)
    {
        string intro = unknown is null
            ? "Elegí un tema con **/info tema:<tema>** (o `aa info <tema>`) y te explico cómo funciona."
            : $"No encontré el tema **{unknown}**. Estos son los que hay: **/info tema:<tema>** (o `aa info <tema>`).";

        var lines = All.Select(t => $"{t.Emoji} **{t.Key}** — {t.Summary}").ToList();
        var embed = new EmbedBuilder()
            .WithTitle("📚 Ayuda por tema")
            .WithColor(OnboardingModule.BrandColor)
            .WithDescription(intro + "\n\nLa lista completa de comandos sigue en **/info** (sin tema).");

        int half = (lines.Count + 1) / 2;
        embed.AddField("Temas", string.Join('\n', lines.Take(half)), false);
        embed.AddField("Más temas", string.Join('\n', lines.Skip(half)), false);
        return embed.Build();
    }

    // Una línea con las claves, para el pie de /info: "enchant, dismantle, bank...".
    public static string KeysLine() => string.Join(", ", All.Select(t => t.Key));

    // ---- Armado de cada tema ----

    private static Embed Topic(HelpTopic topic, string description, params (string Name, string Value)[] fields)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"{topic.Emoji} {topic.Name}")
            .WithColor(OnboardingModule.BrandColor)
            .WithDescription(description);

        foreach (var (name, value) in fields)
        {
            embed.AddField(name, value, false);
        }

        return embed.WithFooter("Más temas: /info (sin tema muestra los comandos; con tema, cómo funciona algo)").Build();
    }

    private static HelpTopic Self(string key) => All.First(t => t.Key == key);

    // "5 h", "30 min", "1 min": un cooldown como lo diría una persona.
    private static string Dur(TimeSpan span) =>
        span.TotalMinutes >= 60 && span.TotalMinutes % 60 == 0 ? $"{(int)span.TotalHours} h" : $"{(int)span.TotalMinutes} min";

    // Decimal con coma: 1,5 / 3,5.
    private static string Dec(double value) => value.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string Num(long value) => GameHistory.Number(value);

    private static Embed Enchant() => DustModule.BuildOptionsEmbed();

    private static Embed Dismantle() => Topic(Self("dismantle"),
        "Desmantelar rompe materiales para sacarles **Polvo**, y el Polvo sirve para **encantar** tu arma y tu amuleto.",
        ("🛠️ Cómo se hace",
            $"**/dismantle** y elegís el ítem de la lista (solo aparecen los que tenés) y la cantidad, de 1 a {Dismantling.MaxPerCommand} por vez.\nEn texto: `aa desmantelar 30 Piedra`."),
        ("🧱 Qué se puede romper",
            "Madera, minerales, drops de monstruos y trofeos de las cajas. **No** las cajas, la comida ni el equipo. Es de una sola mano: el Polvo no vuelve a convertirse en material."),
        ("✨ Cuánto Polvo da cada unidad",
            string.Join(" · ", new[] { "Común", "Raro", "Épico", "Legendario", "Mítico" }.Select(r => $"{r} {Num(Dismantling.DustPerUnit(r))}")) +
            "\nRinde parecido por minuto de farmeo en todas las rarezas, así que no hay una «mejor» para romper."),
        ("🎯 Para qué", "Cada intento de **/enchant** gasta Polvo y oro. Mirá cómo funciona con **/info tema:enchant**. Tu Polvo figura en **/profile**."));

    private static Embed Bank() => Topic(Self("bank"),
        $"Guardás oro **a salvo de la penalidad por morir**. La cuenta se compra una sola vez por **{Num(BankRules.AccountPrice)}** de oro.",
        ("🏦 Comandos",
            "**/bank open** abre la cuenta · **/bank deposit** guarda · **/bank withdraw** saca · **/bank view** muestra tu billetera y tu banco.\nEn deposit y withdraw podés escribir una cantidad o **all**. En texto: `aa bank deposit 500`."),
        ("🛡️ Para qué sirve",
            $"Si perdés un combate perdés el {DeathPenalty.GoldPercent} % del oro de la **billetera**; lo que está en el banco no se toca. Mirá **/info tema:death**."),
        ("⚠️ Ojo", "Sin interés ni tope por ahora. Las compras, `/give` y las apuestas usan la billetera: sacá el oro del banco antes de gastarlo."));

    private static Embed Death() => Topic(Self("death"),
        "Perder un combate tiene un costo. **No** bajás de nivel.",
        ("☠️ Qué perdés",
            $"La **EXP del nivel actual vuelve a 0** y el **{DeathPenalty.GoldPercent} % del oro de tu billetera** (con menos de {100 / DeathPenalty.GoldPercent} de oro no se pierde oro). " +
            "Una muerte promedio son unos 15 a 20 minutos de juego en EXP."),
        ("📍 Cuándo pasa",
            "Si te vencen en **/hunt**, **/travel**, **/boss**, **/autohunt**, si te rematan mientras te curás con **/use**, o si cae todo el grupo de un **/raid** (a cada caído). " +
            "Huir, que se acabe el tiempo, los duelos (**/fight**) y la **Arena** no te quitan nada."),
        ("🛡️ Cómo protegerte",
            $"Guardá oro en el **banco** (**/info tema:bank**), curate con **/heal** antes de pelear, y recordá que **/autohunt** no te deja pelear con menos del {AutoHuntRules.MinHpPercent} % de vida " +
            "porque no puede huir. En **/hunt** a mano ves tu vida y podés huir."));

    private static Embed Play() => Topic(Self("play"),
        $"**/play** apuesta tu oro. Apuesta mínima: **{CasinoModule.MinBet}**. Podés escribir **all** para apostar todo lo que llevás en la billetera. En texto: `aa play slots 100`.",
        ("🪙 Coinflip",
            $"Elegís cara (**Heads**) o cruz (**Tails**) y sale al azar: 50 % de acertar. Si acertás cobrás **×{CasinoService.CoinflipMultiplier}** lo apostado."),
        ("🎰 Slots",
            $"Salen tres símbolos al azar ({string.Join(' ', CasinoService.SlotsSymbolList)}): tres iguales ({Dec(CasinoService.SlotsThreeMatchChance * 100)} % de las veces) pagan **×{CasinoService.SlotsThreeMatchMultiplier}**, " +
            $"dos iguales ({Dec(CasinoService.SlotsPairChance * 100)} %) pagan **×{Dec(CasinoService.SlotsPairMultiplier)}** y tres distintos ({Dec(CasinoService.SlotsAllDifferentChance * 100)} %) pierden la apuesta.\n" +
            $"A la larga la casa se queda con el **{Dec((1 - CasinoService.SlotsReturnToPlayer) * 100)} %** de lo que se apuesta: es un juego de suerte, no una forma de juntar oro."),
        ("📈 Tus números", "**/history** muestra cuánto ganaste y perdiste en el casino, y la diferencia."));

    private static Embed Hunt() => Topic(Self("hunt"),
        $"Peleás por turnos contra los monstruos de tu zona. Cada turno elegís **Atacar**, la **habilidad** de tu clase (se recarga {AbilityTuning.CooldownTurns} turnos) o **huir** (perdés la recompensa).",
        ("🏹 /hunt",
            $"Cooldown de {Dur(CooldownCatalog.Hunt.Duration)}. Da oro y EXP (más cuanto más nivel y zona) y {CombatRewardCalculator.HuntDropChancePercent} % de chance de soltar el material de ese monstruo. Mirá qué suelta cada uno con **/drops**."),
        ("🗺️ /travel",
            $"Un monstruo élite de tu zona, más duro. Cooldown de {Dur(CooldownCatalog.Travel.Duration)}. Paga {CombatRewardCalculator.TravelRewardMultiplier} cacerías juntas y suelta su material el {CombatRewardCalculator.TravelDropChancePercent} % de las veces."),
        ("🤖 /autohunt (aa ah)",
            $"Resuelve una cacería de una sola vez: solo ataque básico, sin habilidad y sin poder huir. Comparte cooldown con /hunt y pide al menos **{AutoHuntRules.MinHpPercent} %** de vida."),
        ("❤️ Tu vida",
            "La vida no se recupera sola: curate con **/heal**, **/use** o en la **/taberna**. Con 0 HP no podés pelear. En /travel y /boss solo podés comer **una vez por pelea**."),
        ("☠️ Si perdés", $"Perdés la EXP del nivel y el {DeathPenalty.GoldPercent} % del oro: **/info tema:death**."));

    private static Embed Boss() => Topic(Self("boss"),
        "Cada zona tiene un jefe. Vencerlo la **primera vez** abre la zona siguiente.",
        ("👑 /boss",
            $"El jefe de tu zona, solo. Pide el nivel de la zona siguiente. Cooldown de **{Dur(CooldownCatalog.Boss.Duration)}** si ganás y de **{Dur(CooldownCatalog.Boss.RetryAfterFailure ?? CooldownCatalog.Boss.Duration)}** si perdés, huís o se acaba el tiempo. " +
            $"Paga {CombatRewardCalculator.BossRewardMultiplier} cacerías (más el bono del jefe) y suelta el **cofre** de su zona: siempre la primera vez que lo vencés (en cada vuelta). Las veces siguientes puede caer una caja más chica (mirá **/drops**). " +
            "Esa primera vez también te llega el **huevo** de la mascota de la zona (**/info tema:pets**)."),
        ("🛡️ /raid",
            $"El mismo jefe pero **cooperativo**, de {RaidSettings.MinParticipants} a {RaidSettings.MaxParticipants} jugadores. Quien lo arranca abre una sala de {(int)RaidModule.LobbyDuration.TotalSeconds} segundos y los demás tocan **Unirse**. " +
            "El jefe es mucho más duro y su vida crece con cada jugador; cada uno que pelea cobra la recompensa entera. Comparte cooldown con /boss."),
        ("🎒 Antes de ir", "Llevá el arma y el amuleto de tu zona (**/info tema:forge**) y la vida llena. Si perdés, pagás la penalidad: **/info tema:death**."));

    private static Embed Forge() => Topic(Self("forge"),
        "En la herrería forjás armas y amuletos con materiales y oro. Todo está en la base de datos y cada uno ve las recetas de **su** zona.",
        ("🔨 Cómo",
            "**/forge** (en texto `aa herrero`): el herrero te muestra un menú con lo que podés forjar (✅ lo que ya podés pagar, ❌ lo que te falta). Cada zona tiene 6 recetas pero ves 3: **el arma de tu clase**, **un arma general** y **el amuleto**."),
        ("⚔️ Arma de clase o general",
            $"Las dos tienen el mismo ataque base, pero el arma de **tu clase** rinde ×{Dec(ClassWeaponSynergy.Multiplier)}. La general sirve a cualquiera y es la más fácil de juntar: buena para empezar."),
        ("🎽 Se equipa sola",
            "Lo forjado queda **puesto**: no va a la mochila ni hace falta equiparlo. Para cambiarlo, vendés la pieza que llevás (en la **/taberna** o con **/shop sell**) y forjás la siguiente. Ojo: al venderla pierde su encantamiento."),
        ("🧱 Los materiales", "Madera y minerales con **/chop** y **/mine**; los drops, de los monstruos (**/drops**). **/tips** te dice qué te falta para tu próxima forja."));

    private static Embed Zone() => Topic(Self("zone"),
        "El mundo se divide en zonas, cada una más difícil que la anterior. **/zonas** las lista con su nivel mínimo.",
        ("🧭 Cómo avanzás",
            "**/zona** te mueve a otra. Hace falta tener el nivel mínimo **y** haber vencido al jefe de la zona anterior (**/info tema:boss**). Tu perfil muestra tu zona y hasta dónde llegaste (máx. Zona N)."),
        ("🎯 Qué depende de tu zona",
            "Los monstruos de **/hunt**, **/travel** y **/boss**, las recetas de la herrería, el valor de los premios de misiones, logros y Arena, y qué cajas podés comprar."),
        ("🧬 Qué hay en cada una", "Dos monstruos de cacería, un élite (para /travel) y un jefe. Con **/drops** ves qué suelta cada uno."));

    private static Embed Boxes() => Topic(Self("boxes"),
        "Las cajas dan varios ítems de una. Hay cinco, de Común a Mítica, y cada una dice cuántos da: **/shop view** muestra los rangos y los precios. Además hay dos que no se compran: el **Cofre de Escoria** (jefe de la Zona 5) y el **Brasero del Fogón** (el Asador Eterno).",
        ("📦 Abrirlas", $"**/open** elegís la caja y cuántas (de 1 a {BoxModule.MaxOpenAtOnce} por vez). En texto: `aa open <caja> 3`."),
        ("🛒 Conseguirlas",
            $"Se compran en la **/taberna** o con **/shop buy**: **{ShopCatalog.BoxesPerPurchase} por compra y una compra cada {Dur(CooldownCatalog.BoxBuy.Duration)}**, y solo de las zonas que ya desbloqueaste (🔒 si todavía no). " +
            "Los jefes sueltan el cofre de su zona, y las misiones y logros también dan cajas. **No se pueden revender.**"),
        ("🎁 Qué traen",
            "Madera y minerales, drops de las zonas que ya desbloqueaste, **trofeos** (materiales que ningún monstruo suelta), comida, cajas más chicas y, muy de vez en cuando, un poco de oro. La **Arca del Soberano** (Mítica) no se compra: la dan solo los logros más difíciles."));

    private static Embed Shop() => Topic(Self("shop"),
        "Acá se compra y se vende todo lo que no se forja, y se cura la vida.",
        ("🍺 /taberna (aa taberna)", "El tabernero te atiende con menús: comés algo de tu mochila, comprás comida y cajas, y vendés. Cada elección es de **una** unidad. También vende la **Comida para Mascotas** (**/info tema:pets**)."),
        ("🛒 /shop", "**view** muestra los precios, **buy** y **sell** compran y venden por nombre y cantidad, **sellall** vende todo lo vendible de la mochila (no lo que llevás puesto)."),
        ("🍖 Consumibles",
            "La comida cura HP y los **banquetes** (los Míticos) además dan un bonus de ataque por un rato: se comen con **/use** (el valor figura en **/shop view** y en tu **/profile** mientras dura; uno nuevo reemplaza al anterior)."),
        ("💊 /heal", "Te cura **todo** lo que te falta comiendo lo justo de tu mochila, sin pasarte (no sirve en combate ni gasta banquetes). Con **/heal comida:<nombre>** usás una sola comida."));

    private static Embed Missions() => Topic(Self("missions"),
        "Dos formas de ganar oro, EXP y cajas jugando normalmente. El progreso se cuenta solo; vos reclamás.",
        ("📋 /missions",
            $"{MissionCatalog.DailyCount} misiones por día y {MissionCatalog.WeeklyCount} por semana, iguales para todos. Se reinician a la medianoche de Uruguay (la semana, el lunes). Reclamás con el botón."),
        ("🏆 /achievements",
            $"{AchievementCatalog.All.Count} logros de 3 tramos, en {AchievementCatalog.Categories.Count} páginas por tema ({string.Join(", ", AchievementCatalog.Categories.Select(c => c.Name))}); elegís la página con el desplegable y reclamás con el botón. " +
            "Cuentan desde que se activó el registro de eventos."),
        ("💰 Los premios", "Crecen con tu zona: oro como cacerías de oro de tu zona, un % de EXP y a veces una caja. Los logros nuevos pagan solo oro y EXP."));

    private static Embed Arena() => Topic(Self("arena"),
        "Dos formas de pelear contra otros jugadores.",
        ("🏟️ /arena (el torneo diario)",
            $"Un torneo por día, hora de Uruguay. Te anotás con **/arena join** durante el día y a la medianoche se juega solo, en llave de eliminación ({ArenaRules.MinPlayers} a {ArenaRules.MaxPlayers} anotados; con menos de {ArenaRules.MinPlayers} se cancela). " +
            $"El campeón cobra {ArenaRules.ChampionReward.GoldUnits} cacerías de oro de su zona, {ArenaRules.ChampionReward.XpPercent} % de EXP y una caja. **/arena listplayers** muestra los anotados, **/arena results** la última llave, y **/cd** te recuerda si ya te anotaste."),
        ("⚔️ /fight @jugador",
            $"Duelo amistoso por turnos (Atacar, habilidad o rendirte; {(int)DuelService.TurnTimeout.TotalSeconds} segundos por turno). No se juega nada: ni vida, ni oro, ni EXP. **/duels** muestra tu récord."),
        ("🛡️ Con qué se pelea", "Con tu nivel, tu arma, tu amuleto y tus encantamientos de ese momento. Las clases tienen un ajuste de vida propio para el PvP."));

    private static Embed Trade() => Topic(Self("trade"),
        "Cambiás materiales con otro jugador, de a uno por uno.",
        ("🤝 Cómo", "**/trade @jugador** y elegís lo que das y lo que querés. En texto: `aa trade @jugador \"Hierro\" \"Carbón\"`."),
        ("📏 Las reglas",
            "Solo **madera y minerales**, de la **misma rareza** y distintos entre sí (Roble por Hierro, Pino por Piedra...). Nada de drops, comida ni equipo."),
        ("⏱️ La oferta", $"El otro jugador acepta o rechaza con botones. Dura {(int)TradeOfferService.Lifetime.TotalMinutes} minutos y tenés una abierta a la vez."),
        ("🍺 ¿Y con el tabernero?", $"Los **drops de monstruos** se cambian con él, de a {DropExchange.GiveAmount} por {DropExchange.GetAmount}: **/info tema:exchange**."));

    private static Embed Exchange() => Topic(Self("exchange"),
        $"El tabernero te cambia **{DropExchange.GiveAmount} drops de una zona por {DropExchange.GetAmount} de esa misma zona**: para cuando te sobra de uno y te falta otro, o la suerte no te acompañó.",
        ("🔁 Cómo",
            $"**/exchange** con **dar** (el drop que entregás, de a {DropExchange.GiveAmount}), **recibir** (otro drop de la misma zona) y **veces** (de 1 a {DropExchange.MaxTimes} cambios juntos). Las listas te muestran solo lo que podés hacer.\n" +
            "En texto: `aa exchange \"Pluma de Ñandú\" \"Cuero Grueso\" 2`. En la **/taberna** hay una lista \"Cambiar drops\" para hacer un cambio de una."),
        ("📏 Las reglas",
            "Solo **drops de monstruos** (los de cacería y de viaje de cada zona), siempre de la **misma zona** y a uno **distinto** del que das. No sirve para madera ni minerales (eso es entre jugadores: **/info tema:trade**), ni para trofeos ni cajas."),
        ("⚖️ ¿Conviene?",
            $"Es {DropExchange.GiveAmount} por {DropExchange.GetAmount}: compensa la mala racha, no reemplaza al farmeo. Si el drop que te falta lo podés conseguir jugando, sale más barato jugar."));

    private static Embed Fogon() => Topic(Self("fogon"),
        "Al vencer al jefe de la **última zona** se abre **El Fogón Eterno**, la «Zona 0»: una puerta al final del mundo y el último paso antes del **Fuego Nuevo** (volver a empezar con más velocidad, tus mascotas y tu oro).",
        ("🚪 Cómo se entra",
            $"Con **/zona 0** (en texto `aa zona 0`). Hace falta haber vencido al jefe de la última zona, tener el nivel de la puerta (figura en **/zonas**) y llevar **puestos** el **{FogonRules.WeaponName}** y la **{FogonRules.AmuletName}**: uno solo de cada uno, iguales para todas las clases (sin sinergia de clase)."),
        ("⚒️ El equipo",
            "Se forja en la herrería (**/forge**) y es lo más caro del juego: pide los **drops de cacería de las 5 zonas**, bastante madera y mineral raros y mucho oro. **/forge-special** te muestra las dos recetas DESDE EL PRINCIPIO (con lo que ya tenés de cada cosa) para que no desmanteles ni vendas lo que vas a necesitar; lo que te falta primero lo ves en **/tips**. " +
            "Como el equipo va en su casillero, hay que **vender primero el de la zona anterior** (sus encantamientos se pierden: es el último equipo, el Fuego Nuevo lo borra de todos modos)."),
        ("⚔️ Adentro",
            $"Solo **/boss**, contra **{FogonRules.BossName}**: no hay cacería, viajes ni raid. Usa el cooldown del jefe (**{Dur(CooldownCatalog.Boss.Duration)}** si ganás y **{Dur(CooldownCatalog.Boss.RetryAfterFailure ?? CooldownCatalog.Boss.Duration)}** si perdés, huís o se acaba el tiempo) y si perdés pagás la penalidad de siempre (**/info tema:death**). " +
            "Está calibrado como cada jefe: con el equipo de la zona anterior casi siempre perdés, con el del Fogón es un desafío parejo."),
        ("🏆 Al ganar", "Volvés a la última zona, se habilita el **Fuego Nuevo** (**/fuegonuevo**, y cómo funciona en **/info tema:fuego**) y sumás el logro **Asador**. Para salir sin pelear, viajá a cualquier zona con **/zona**."));

    private static Embed Professions()
    {
        var woodcutter = ProfessionCatalog.Get(ProfessionCatalog.WoodcutterKey);
        var miner = ProfessionCatalog.Get(ProfessionCatalog.MinerKey);
        var enchanter = ProfessionCatalog.Get(ProfessionCatalog.EnchanterKey);
        long total = ProfessionRules.TotalXpToMax;

        return Topic(Self("professions"),
            "Los **oficios** suben solos cuando usás su comando: cuanto más talás, minás o encantás, mejor te sale. Cada uno va del nivel 0 al " + ProfessionRules.MaxLevel + ".",
            ("📈 Cómo suben",
                $"{woodcutter.Emoji} **{woodcutter.Name}** con **{woodcutter.Command}** ({woodcutter.XpPerAction} XP por uso), {miner.Emoji} **{miner.Name}** con **{miner.Command}** ({miner.XpPerAction}) y " +
                $"{enchanter.Emoji} **{enchanter.Name}** con cada intento de **{enchanter.Command}** ({enchanter.XpPerAction}).\n" +
                $"Llegar al {ProfessionRules.MaxLevel} son ~{GameHistory.Number(total)} de XP: unos {GameHistory.Number(total / woodcutter.XpPerAction)} usos de tala o minería, o {GameHistory.Number(total / enchanter.XpPerAction)} intentos de encantamiento. " +
                "Lo que hiciste antes de que existieran los oficios también cuenta."),
            ("🎁 Lo que da cada nivel",
                $"{woodcutter.Emoji} {miner.Emoji} **{woodcutter.Name} y {miner.Name}**: {ProfessionRules.DescribePerLevel(woodcutter)}. Lo de la rareza sube un escalón (Común → Raro → Épico → Legendario); el Mítico nunca se mejora.\n" +
                $"{enchanter.Emoji} **{enchanter.Name}**: {ProfessionRules.DescribePerLevel(enchanter)}.\n" +
                "Todo suma a lo de Fuego Nuevo y las bendiciones."),
            ($"⭐ Al nivel {ProfessionRules.MaxLevel}",
                $"{ProfessionRules.DescribeAdvanced(woodcutter)}.\n{ProfessionRules.DescribeAdvanced(miner)}.\n{ProfessionRules.DescribeAdvanced(enchanter)}."),
            ("🔄 Y el Fuego Nuevo", "Los oficios **se quedan** cuando hacés un Fuego Nuevo: tu nivel no se pierde."),
            ("📍 Comandos", "**/professions** (en texto `aa professions` o `aa oficios`) muestra tu nivel, tu XP y lo que ya te dan. También figuran en **/profile** y el avance sale al pie de cada **/chop**, **/mine** y **/enchant**."));
    }

    private static Embed Fuego() => Topic(Self("fuego"),
        "El **Fuego Nuevo** es volver a empezar, pero más rápido: perdés el nivel, el equipo y los materiales y ganás **porcentajes permanentes** y una **bendición** por cada vuelta. " +
        "Es voluntario y se habilita al vencer al **Asador Eterno** en **El Fogón Eterno** (**/info tema:fogon**).",
        ("🔥 Cómo se hace",
            "**/fuegonuevo** (en texto `aa fn`) muestra lo que se va, lo que se queda y lo que ganás, con el botón para hacerlo. Después elegís la **clase** de la vuelta nueva: tocarla confirma y no se deshace. " +
            "No se puede en pleno combate o raid, y cada vuelta pide volver a vencer al Asador (el equipo del Fogón se forja de nuevo)."),
        ("📈 Lo que ganás con cada uno",
            $"🏹 Chance de drop en **/hunt**: **{FuegoNuevoRules.PercentText(1 + FuegoNuevoRules.HuntDropStep)}** sobre la base por cada Fuego Nuevo (lineal: el 10.º es {FuegoNuevoRules.PercentText(FuegoNuevoRules.HuntDropMultiplier(10))}, no se compone)\n" +
            $"🗺️ Chance de drop en **/travel**: arranca en **{FuegoNuevoRules.PercentText(1 + FuegoNuevoRules.TravelStep(1))}** y cada vuelta suma un poco menos (el 10.º acumula {FuegoNuevoRules.PercentText(FuegoNuevoRules.TravelDropMultiplier(10))}, con piso de {FuegoNuevoRules.PercentText(1 + FuegoNuevoRules.TravelStep(1000))} por vuelta)\n" +
            $"🪓⛏️ Cantidad por **/chop** y **/mine**: **{FuegoNuevoRules.PercentText(1 + FuegoNuevoRules.GatherStep)}** por Fuego Nuevo (el 10.º es ×{FuegoNuevoRules.GatherMultiplier(10).ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',')})\n" +
            $"📊 EXP de las peleas: **{FuegoNuevoRules.PercentText(1 + FuegoNuevoRules.XpStep)}** por Fuego Nuevo\n" +
            $"Con la chance base de /hunt ({CombatRewardCalculator.HuntDropChancePercent} %) y de /travel ({CombatRewardCalculator.TravelDropChancePercent} %). El oro y el cofre del jefe no cambian."),
        ("💨 Lo que se va",
            "Nivel y EXP, la zona y los jefes vencidos, el arma y el amuleto (con sus encantamientos), todos los materiales, drops, cajas y consumibles, el Polvo, los banquetes y los enfriamientos. Sin reembolso."),
        ("🔒 Lo que se queda",
            "El oro (billetera y banco), las mascotas con sus huevos y su comida, los logros, las misiones cobradas, los trofeos, la racha diaria, las bendiciones y tus Fuegos Nuevos."),
        ("🙏 Bendiciones",
            $"Una por vuelta, elegida entre {BlessingCatalog.OfferSize} sorteadas ({BlessingCatalog.All.Count} en total); si repetís una sube de nivel hasta el {BlessingCatalog.RomanLevel(BlessingCatalog.MaxLevel)}. " +
            "Las hay de velocidad (drop, cantidad, EXP), de poder chico (ataque, defensa y vida, solo contra monstruos), de mascotas, de oro, de ítems y cosméticas. **/blessings** las muestra todas."),
        ("⚖️ Reglas", "Los duelos y la Arena no usan nada de esto: se juegan con nivel, equipo y clase. La clase solo se elige empezando de cero y con cada Fuego Nuevo (**/class**)."));

    private static Embed Pets() => Topic(Self("pets"),
        "Cada zona tiene su **mascota**. Las que tengas valen **todas a la vez**: no ocupan lugar, no se pierden y no hay que sacarlas a pasear.",
        ("🥚 Cómo se consiguen",
            "La **primera vez** que vencés al jefe de una zona (**/boss** o **/raid**) te llega un **huevo** junto con el cofre. Lo abrís con **/open** y nace la mascota de esa zona: una por zona, y la sexta es la del **Fogón Eterno** (la trae el Asador, la primera vez de cada vuelta que no la tengas)."),
        ("🎁 Qué dan",
            "Cada especie da un bonus distinto: **más oro**, **más EXP** o **más defensa** en las peleas contra monstruos, **más chances de drop** en cacería y viaje, o **más recolección** en /chop y /mine. " +
            $"Es relativo: un +6 % sobre un {CombatRewardCalculator.HuntDropChancePercent} % de drop da {Dec(CombatRewardCalculator.HuntDropChancePercent * 1.06)} %. El cofre del jefe no cambia, y en duelos y Arena no cuentan. " +
            "Mirá cuál da cada una (y cuánto) con **/pet view**."),
        ("🍖 Cómo crecen",
            $"Comen **{PetRules.FoodItemName}** (se compra en la **/taberna**). Cada mascota puede comer **una vez por hora**; **/pet feed** alimenta a todas las que estén listas o elegís una. " +
            $"Tienen nivel de 1 a {PetRules.MaxLevel} y el bonus crece con el nivel (nivel 1 = el 10 % de su tope, nivel {PetRules.MaxLevel} = el tope entero). Llegar al máximo lleva {PetRules.TotalFeedsToMax} comidas por mascota."),
        ("📍 Comandos", "**/pet view** las muestra, **/pet feed** las alimenta y **/open** abre el huevo. En texto: `aa pet` y `aa pet feed`. También figuran en **/profile**."));

    private static Embed Classes()
    {
        var embed = new EmbedBuilder()
            .WithTitle($"{Self("classes").Emoji} {Self("classes").Name}")
            .WithColor(OnboardingModule.BrandColor)
            .WithDescription("Elegís tu clase con **/class** (solo empezando de cero, nivel 1: después se vuelve a elegir con cada **Fuego Nuevo**). Cada una tiene una **pasiva** que siempre está y una **habilidad** que se usa con un botón en la pelea manual.");

        foreach (var cls in ClassCatalog.All)
        {
            var passive = ClassPassives.For(cls.Name);
            var ability = ClassAbilities.For(cls.Name);
            string passiveText = cls.Name switch
            {
                "Guerrero" => $"+{(int)Math.Round((passive.MaxHpMultiplier - 1) * 100)} % de vida y {(int)Math.Round((1 - passive.DamageTakenMultiplier) * 100)} % menos de daño recibido",
                "Ninja" => $"+{(int)Math.Round(passive.DodgeChanceBonus * 100)} % de esquive",
                "Arquero" => $"+{(int)Math.Round(passive.CritChanceBonus * 100)} % de golpe crítico",
                "Hechicero" => $"{(int)Math.Round(passive.LifestealChance * 100)} % de chance de curarte el {(int)Math.Round(passive.LifestealRatio * 100)} % del daño que hacés (Sifón de Almas)",
                _ => "—",
            };

            embed.AddField(
                $"{cls.Emoji} {cls.Name} — arma: {cls.WeaponType}",
                $"{cls.Description}\n**Pasiva:** {passiveText}.\n**Habilidad:** {(ability is null ? "—" : $"{ability.Emoji} {ability.Name} — {ability.Description}")}",
                false);
        }

        return embed.WithFooter($"El arma de tu clase rinde ×{Dec(ClassWeaponSynergy.Multiplier)}. La habilidad se recarga {AbilityTuning.CooldownTurns} turnos y /autohunt no la usa.").Build();
    }

    private static Embed Gather() => Topic(Self("gather"),
        $"**/chop** junta madera y **/mine** minerales. Cooldown de {Dur(CooldownCatalog.Chop.Duration)} cada uno. Son la base de las recetas de la herrería.",
        ("🎲 Qué te toca",
            "Primero sale una rareza al azar y después un ítem de esa rareza: " +
            string.Join(" · ", RarityCatalog.GatheringChances.Select(c => $"{c.Rarity} {Dec(c.Percent)} %"))),
        ("📦 Cuántas unidades",
            "Cuanto más rara, menos unidades: " +
            string.Join(" · ", new[] { "Común", "Raro", "Épico", "Legendario", "Mítico" }.Select(r =>
            {
                var (min, max) = GatheringYield.RangeFor(r);
                return min == max ? $"{r} {min}" : $"{r} {min} a {max}";
            }))),
        ("🔁 Después", "Lo que juntás sirve para forjar (**/info tema:forge**), para cambiar con otros jugadores (**/info tema:trade**) o para desmantelar y sacar Polvo (**/info tema:dismantle**)."));

    private static Embed Daily() => Topic(Self("daily"),
        "Una recompensa por día y la posibilidad de regalar oro.",
        ("🎁 /daily",
            $"Una vez cada {Dur(DailyRewardCalculator.MinInterval)}. Cada día seguido suma a la **racha**: el día N paga {DailyRewardCalculator.GoldPerMultiplier} × N de oro y {DailyRewardCalculator.XpPerMultiplier} × N de EXP, hasta el día {DailyRewardCalculator.MaxStreakMultiplier} " +
            $"({Num(DailyRewardCalculator.GoldPerMultiplier * DailyRewardCalculator.MaxStreakMultiplier)} de oro y {Num(DailyRewardCalculator.XpPerMultiplier * DailyRewardCalculator.MaxStreakMultiplier)} de EXP). " +
            $"Si pasan más de {Dur(DailyRewardCalculator.StreakGraceWindow)} sin reclamar, la racha vuelve al día 1. **/cd** te dice cuándo podés volver."),
        ("🤝 /give @jugador cantidad", "Le regalás oro a otro jugador (una cantidad o **all**). Sale de tu billetera; no a vos mismo. En texto: `aa give @jugador 100`."));

    private static Embed Stats() => Topic(Self("stats"),
        "Tu **/profile** muestra todo esto junto.",
        ("📊 EXP y nivel",
            $"Para pasar de nivel hace falta 100 × nivel^1,5 de EXP (nivel 5: {Num(LevelingCalculator.RequiredXpForLevel(5))}, nivel 10: {Num(LevelingCalculator.RequiredXpForLevel(10))}, nivel 20: {Num(LevelingCalculator.RequiredXpForLevel(20))}). " +
            "La EXP es la del nivel actual: se reinicia cuando subís (y también si perdés un combate: **/info tema:death**)."),
        ("⬆️ Al subir de nivel",
            $"+{LevelingCalculator.HpGainedPerLevel} de vida máxima (y te curás entera), +{CombatStats.BaseAttack(2) - CombatStats.BaseAttack(1)} de ataque y +{CombatStats.BaseDefense(2) - CombatStats.BaseDefense(1)} de defensa."),
        ("⚔️ Ataque y defensa",
            $"Ataque = {CombatStats.BaseAttack(0)} + {CombatStats.BaseAttack(2) - CombatStats.BaseAttack(1)} por nivel + tu arma (×{Dec(ClassWeaponSynergy.Multiplier)} si es de tu clase, más su encantamiento). Defensa = tu nivel + tu amuleto (con su encantamiento)."));
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
