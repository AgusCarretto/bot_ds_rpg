namespace BotDsRpg.GameData;

public enum BlacksmithLine { Greeting, Success, NotEnough, UnknownRecipe, WrongClass, NoRecipes, SlotTaken, AlreadyEquipped }

public enum ShopkeeperLine { Greeting, BuySuccess, NoGold, BoxWait, SellSuccess, SellAll, NothingToSell, NotForSale, NotOwned, Unsellable, GearConfirm, GearKept, Exchange }

public enum InnkeeperLine { Healed, NothingToEat, FullHp, InCombat }

public enum DeepShopkeeperLine { Greeting, BuySuccess, NoDust, NotFound }

// Lo que dicen los personajes con los que se interactúa: el herrero, el tendero, el tabernero y los jefes de zona. Cada situación tiene
// varias frases y se elige una al azar (así no se lee siempre lo mismo). Puro: se prueba sin Discord (el Random se puede inyectar).
// Para sumar frases basta con agregarlas a la lista de la situación; para un personaje nuevo, un enum y una tabla acá.
public static class NpcDialogue
{
    // "🔨 **El Herrero:** «No tenés lo suficiente, crack...»" — el formato único de todo lo que dice alguien.
    public static string Say(string name, string emoji, string text) => $"{emoji} **{name}:** «{text}»";

    // La frase se elige en español (las tablas están marcadas con Loc.Mark) y se traduce al mostrarla; una sin traducción sale como está.
    private static string Pick(IReadOnlyList<string> lines, Random? rng) => Loc.Tr(lines[(rng ?? Random.Shared).Next(lines.Count)]);

    // ---------------- El Herrero ----------------
    private static readonly Dictionary<BlacksmithLine, string[]> BlacksmithLines = new()
    {
        [BlacksmithLine.Greeting] =
        [
            Loc.Mark("¿Qué necesita, señor? Elija de la lista y yo me encargo."),
            Loc.Mark("Pase, pase. El yunque está caliente. ¿Qué le forjo?"),
            Loc.Mark("Buenas, forastero. Dígame qué necesita y le doy forma."),
        ],
        [BlacksmithLine.Success] =
        [
            Loc.Mark("¡En camino, loco! Queda pronta."),
            Loc.Mark("Perfecto, forjado. Mirá qué belleza te quedó."),
            Loc.Mark("¡Listo el pollo! Ahí tenés, cuidala que me costó sudor."),
        ],
        [BlacksmithLine.NotEnough] =
        [
            Loc.Mark("No tenés lo suficiente, crack. Andá a farmear y después hablamo."),
            Loc.Mark("Con eso no me alcanza ni para encender la fragua. Volvé cuando juntes lo que falta."),
            Loc.Mark("Faltan cosas, che. Salí a juntar y vení que te espero."),
        ],
        [BlacksmithLine.UnknownRecipe] =
        [
            Loc.Mark("Esa receta no la conozco, amigo. Fijate en la lista."),
            Loc.Mark("¿Eso? Nunca lo forjé. Pedime algo de la lista."),
        ],
        [BlacksmithLine.WrongClass] =
        [
            Loc.Mark("Eso es para otro oficio, compa. A vos no te va a servir."),
            Loc.Mark("Esa pieza se hace a medida para otra clase. Elegí otra."),
        ],
        [BlacksmithLine.SlotTaken] =
        [
            Loc.Mark("Ya andás con una pieza puesta, compa. Vendésela al tabernero y hablamos."),
            Loc.Mark("No te puedo poner otra encima. Primero desprendete de la que traés y vuelvo a la fragua."),
            Loc.Mark("Una a la vez, che. Vendé la que tenés puesta y te forjo la nueva."),
        ],
        [BlacksmithLine.AlreadyEquipped] =
        [
            Loc.Mark("Esa ya la llevás puesta, ¿qué más querés? Elegí otra cosa."),
            Loc.Mark("Pero si ya la tenés encima, crack. Pedime otra pieza."),
        ],
        [BlacksmithLine.NoRecipes] =
        [
            Loc.Mark("Por ahora no tengo nada para vos en esta zona. Volvé más adelante."),
            Loc.Mark("Todavía no sé qué hacerte por acá. Pasá por otra zona y hablamos."),
        ],
    };

    // El saludo de quien ya hizo un Fuego Nuevo (la historia, GameData/Lore.cs): el Herrero le confesó lo de la parrilla y duerme mejor.
    private static readonly string[] BlacksmithReturningGreetings =
    [
        Loc.Mark("Pase, pase. Desde que me saqué eso de adentro duermo mejor. ¿Qué le forjo?"),
        Loc.Mark("El yunque está caliente y yo, más liviano. ¿Qué necesita?"),
        Loc.Mark("Otra vuelta, ¿eh? Elija de la lista; el que fue aprendiz del Soberano le da forma."),
    ];

    // fuegoNuevo: con 1 o más, el saludo es el de quien volvió (solo el saludo; el resto de las situaciones no cambia).
    public static string Blacksmith(BlacksmithLine line, Random? rng = null, int fuegoNuevo = 0) =>
        Say("El Herrero", "🔨", Pick(line == BlacksmithLine.Greeting && fuegoNuevo >= 1 ? BlacksmithReturningGreetings : BlacksmithLines[line], rng));

    // ---------------- El Tendero ----------------
    private static readonly Dictionary<ShopkeeperLine, string[]> ShopkeeperLines = new()
    {
        [ShopkeeperLine.Greeting] =
        [
            Loc.Mark("Pase, pase, ¿qué le sirvo?"),
            Loc.Mark("Bienvenido a la tienda. Mirá bien, que todo tiene su precio."),
            Loc.Mark("Buenas. Tengo comida fresca y cajas con sorpresa."),
        ],
        [ShopkeeperLine.BuySuccess] =
        [
            Loc.Mark("¡Trato hecho! Que lo disfrutes."),
            Loc.Mark("Excelente elección. Vuelva cuando quiera."),
            Loc.Mark("Ahí tiene. Cuidalo bien."),
        ],
        [ShopkeeperLine.NoGold] =
        [
            Loc.Mark("No te alcanza la plata, amigo. Juntá y volvé."),
            Loc.Mark("Sin monedas no hay trato. Andá a cazar un poco."),
            Loc.Mark("Uy, estás corto de oro. Vení cuando te sobre."),
        ],
        [ShopkeeperLine.BoxWait] =
        [
            Loc.Mark("Ya te vendí una caja hace poco. Dejame que me reponga el stock."),
            Loc.Mark("Una caja cada dos horas, que si no me vacían el depósito. Volvé en un rato."),
        ],
        [ShopkeeperLine.SellSuccess] =
        [
            Loc.Mark("Bien, te lo compro. Aquí tenés tus monedas."),
            Loc.Mark("Hecho. Un placer hacer negocios."),
            Loc.Mark("Perfecto, lo anoto en el libro."),
        ],
        [ShopkeeperLine.SellAll] =
        [
            Loc.Mark("¡Qué cargamento! Te llevo todo."),
            Loc.Mark("Vaya, vaya, traés la mochila llena. Cerramos trato por todo."),
        ],
        [ShopkeeperLine.NothingToSell] =
        [
            Loc.Mark("No tenés nada para vender, che. Vení cuando traigas algo."),
            Loc.Mark("La mochila vacía, amigo. Salí a juntar y volvé."),
        ],
        [ShopkeeperLine.NotForSale] =
        [
            Loc.Mark("Eso no lo vendo, amigo. Fijate en el mostrador."),
            Loc.Mark("De eso no tengo. Mirá lo que hay en la tienda."),
        ],
        [ShopkeeperLine.NotOwned] =
        [
            Loc.Mark("No tenés eso para vender, che. Mirá bien la mochila."),
            Loc.Mark("Eso no lo veo por ningún lado. No me hagas perder el tiempo."),
        ],
        [ShopkeeperLine.GearConfirm] =
        [
            Loc.Mark("Ojo, que eso es lo que llevás puesto. ¿Seguro que querés desprenderte?"),
            Loc.Mark("¿Vender lo que traés encima? Pensalo bien, que después hay que volver a forjarlo."),
        ],
        [ShopkeeperLine.GearKept] =
        [
            Loc.Mark("Hacés bien, quedate con tu pieza. Cuando quieras cambiar, acá estoy."),
            Loc.Mark("Sensato. Mejor guardarla hasta tener la próxima lista."),
        ],
        [ShopkeeperLine.Exchange] =
        [
            Loc.Mark("Tres por uno, trato hecho. La mala racha se compensa con un buen cambalache."),
            Loc.Mark("Mala suerte con los drops, ¿eh? Tomá, algo es algo."),
            Loc.Mark("Cambalache cerrado: yo me quedo con tres y vos con lo que te faltaba."),
        ],
        [ShopkeeperLine.Unsellable] =
        [
            Loc.Mark("Eso no lo compro, es un premio. Quedátelo."),
            Loc.Mark("Ni loco te lo compro: eso es un regalo, no mercadería."),
        ],
    };

    // El que atiende la tienda ES el tabernero (una sola taberna, un solo personaje).
    // El saludo de quien ya hizo un Fuego Nuevo (la historia, GameData/Lore.cs): el Tabernero lo reconoce, aunque haga como que no.
    private static readonly string[] ShopkeeperReturningGreetings =
    [
        Loc.Mark("Pasá, pasá. Ya te serví el mate; el otro es para nadie, no preguntes."),
        Loc.Mark("Mirá quién volvió. Siempre volvés vos. ¿Algo para comer, o un mate?"),
        Loc.Mark("Buenas. Hoy el fuego del campamento anda más tranquilo. ¿Qué te sirvo?"),
    ];

    // fuegoNuevo: con 1 o más, el saludo es el de quien volvió (solo el saludo; el resto de las situaciones no cambia).
    public static string Shopkeeper(ShopkeeperLine line, Random? rng = null, int fuegoNuevo = 0) =>
        Say("El Tabernero", "🍺", Pick(line == ShopkeeperLine.Greeting && fuegoNuevo >= 1 ? ShopkeeperReturningGreetings : ShopkeeperLines[line], rng));

    // ---------------- El Tabernero del Inframundo (/deepshop, v0.17.0) ----------------
    // Habla como el de arriba pero más seco: acá abajo no se fía y se paga en Polvo.
    private static readonly Dictionary<DeepShopkeeperLine, string[]> DeepShopkeeperLines = new()
    {
        [DeepShopkeeperLine.Greeting] =
        [
            Loc.Mark("Pasá, pasá. Acá abajo no hay mate, pero hay lo que arriba no se consigue."),
            Loc.Mark("Llegaste hasta el fondo. Pocos llegan. ¿Qué te falta?"),
            Loc.Mark("Cerrá la puerta, que se va el calor. Mirá lo que tengo."),
        ],
        [DeepShopkeeperLine.BuySuccess] =
        [
            Loc.Mark("Hecho. No preguntes de dónde sale: acá abajo nadie pregunta."),
            Loc.Mark("Tuyo. Cuidalo, que no hay otro igual en toda la superficie."),
            Loc.Mark("Un placer. El Polvo siempre es bienvenido."),
        ],
        [DeepShopkeeperLine.NoDust] =
        [
            Loc.Mark("Con eso no alcanza ni para la mitad. Volvé con más Polvo."),
            Loc.Mark("Acá abajo no se fía, forastero. Desmantelá algo y volvé."),
            Loc.Mark("Me falta Polvo, y a vos también. Andá a juntar."),
        ],
        [DeepShopkeeperLine.NotFound] =
        [
            Loc.Mark("Eso no lo vendo. Tengo la Corteza y el Meteorito, nada más."),
            Loc.Mark("No sé de qué hablás. Corteza o Meteorito, elegí."),
        ],
    };

    public static string DeepShopkeeper(DeepShopkeeperLine line, Random? rng = null) =>
        Say("El Tabernero del Inframundo", "🕯️", Pick(DeepShopkeeperLines[line], rng));

    // ---------------- El Tabernero (/heal) ----------------
    private static readonly Dictionary<InnkeeperLine, string[]> InnkeeperLines = new()
    {
        [InnkeeperLine.Healed] =
        [
            Loc.Mark("Comé tranquilo, que estás entre amigos. ¡Salud!"),
            Loc.Mark("Buen provecho, forastero. Con esto te ponés de pie."),
            Loc.Mark("Un guiso y una siesta y quedás como nuevo."),
        ],
        [InnkeeperLine.NothingToEat] =
        [
            Loc.Mark("No tenés nada para comer. Comprá algo en la tienda y vení."),
            Loc.Mark("Con la panza vacía no te puedo servir nada. Pasá por la tienda."),
        ],
        [InnkeeperLine.FullHp] =
        [
            Loc.Mark("Estás más sano que un roble. No necesitás nada."),
            Loc.Mark("Pero si estás hecho un toro. Guardá el hambre para después."),
        ],
        [InnkeeperLine.InCombat] =
        [
            Loc.Mark("¡Ahora no, che! Primero terminá la pelea."),
            Loc.Mark("En medio de un combate no te sirvo ni un mate. ¡Pelea!"),
        ],
    };

    public static string Innkeeper(InnkeeperLine line, Random? rng = null) => Say("El Tabernero", "🍺", Pick(InnkeeperLines[line], rng));

    // ---------------- Los jefes de zona ----------------
    public enum BossLine { Intro, Defeated, Victory }

    // Intro: cuando aparece. Defeated: lo que dice al caer (ganó el jugador). Victory: lo que dice cuando te vence (ganó el jefe).
    private static readonly Dictionary<string, Dictionary<BossLine, string[]>> BossLines = new()
    {
        ["Rey Jabalí"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("¡Nadie pisa mis praderas sin pagar tributo!"), Loc.Mark("¡Soy el Rey de las Praderas! ¡Arrodillate!")],
            [BossLine.Defeated] = [Loc.Mark("No… no puede ser… un simple aventurero…"), Loc.Mark("Las praderas… ya no son mías…")],
            [BossLine.Victory] = [Loc.Mark("¡Jajaja! ¡Otro más para mi colección!"), Loc.Mark("Volvé cuando seas digno, plebeyo.")],
        },
        ["Lobisón Alfa"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("La manada ya te olió, forastero. No saldrás vivo."), Loc.Mark("¡Aúúú! Esta luna es tuya... y la última.")],
            [BossLine.Defeated] = [Loc.Mark("La manada... recordará tu nombre..."), Loc.Mark("Ganaste... esta noche.")],
            [BossLine.Victory] = [Loc.Mark("Aullá de dolor, cachorro."), Loc.Mark("La manada se alimenta esta noche.")],
        },
        ["Capataz de Hierro"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("¡Aquí se trabaja, no se pasea! ¡Fuera de mis minas!"), Loc.Mark("Otro intruso. Vas a cargar yunques toda la vida.")],
            [BossLine.Defeated] = [Loc.Mark("Mis... mis minas... el yunque se enfría..."), Loc.Mark("Qué golpe... tenés buen brazo.")],
            [BossLine.Victory] = [Loc.Mark("A trabajar, vago. Esto no es para vos."), Loc.Mark("Tomá nota: hierro duro, cabeza dura.")],
        },
        ["Señor del Volcán"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("El fuego es mi voz y la ceniza mi trono. ¡Arde!"), Loc.Mark("¿Osás desafiar al Señor del Volcán? ¡Que se abra la tierra!")],
            [BossLine.Defeated] = [Loc.Mark("El fuego... se apaga..."), Loc.Mark("Imposible... la lava no me abandona...")],
            [BossLine.Victory] = [Loc.Mark("Reducido a cenizas, como todos."), Loc.Mark("Calentito, ¿no? Eso era solo el principio.")],
        },
        ["Soberano de la Escoria"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("Mi reino es polvo, y vos serás parte de él."), Loc.Mark("De rodillas ante el Soberano. Todo termina en escoria.")],
            [BossLine.Defeated] = [Loc.Mark("Mi reino... se desmorona... sos digno..."), Loc.Mark("Así que existe alguien más fuerte...")],
            [BossLine.Victory] = [Loc.Mark("Ahora sos escoria, como todos."), Loc.Mark("El abismo te reclama.")],
        },
        // El jefe de El Fogón Eterno (zona 0, v0.11.0). Es la VUELTA 1: desde la v0.15.0 es el Asador de la tragedia (GameData/Lore.cs); con los Fuegos Nuevos habla distinto (AsadorStages).
        [FogonRules.BossName] = new()
        {
            [BossLine.Intro] = [Loc.Mark("Pasá, pasá. Todavía no llegaron los demás, pero la mesa está lista."), Loc.Mark("La brasa no se apaga. No se tiene que apagar. ¿Entendés? ¡No se tiene que apagar!")],
            [BossLine.Defeated] = [Loc.Mark("¿Van a venir? Decime que van a venir..."), Loc.Mark("Llevate el carbón. Pero dejame la mesa puesta.")],
            [BossLine.Victory] = [Loc.Mark("Quedate. Hay lugar en la mesa para uno más."), Loc.Mark("Descansá acá, que afuera hace frío. Yo te cocino.")],
        },
    };

    // Lo que dice el Asador Eterno según cuántos Fuegos Nuevos hizo el jugador (la historia, GameData/Lore.cs): cada escalón reemplaza al anterior desde ese Fuego Nuevo. El primero (0) es el de
    // BossLines; los demás empiezan en las vueltas 10, 25, 50 y 90, las mismas en que el fuego cuenta algo nuevo del Asador.
    private static readonly (int FromFuegoNuevo, Dictionary<BossLine, string[]> Lines)[] AsadorStages =
    [
        (10, new()
        {
            [BossLine.Intro] = [Loc.Mark("Vos otra vez. Siempre volvés vos."), Loc.Mark("Ya te conozco la cara. Pasá, que hay para uno más.")],
            [BossLine.Defeated] = [Loc.Mark("Otra vez el carbón... Está bien. Llevalo."), Loc.Mark("Ya no duele tanto como antes. Raro.")],
            [BossLine.Victory] = [Loc.Mark("Quedate un rato. Contame cómo está el mundo."), Loc.Mark("Hoy no. Hoy quedate a comer.")],
        }),
        (25, new()
        {
            [BossLine.Intro] = [Loc.Mark("Levanté diez platos. Todavía me acuerdo de cada nombre."), Loc.Mark("Pasá. Hoy la mesa está más liviana.")],
            [BossLine.Defeated] = [Loc.Mark("Ramona hubiera querido que me ganaras."), Loc.Mark("Un plato menos por cada vez que volvés. Seguí.")],
            [BossLine.Victory] = [Loc.Mark("No te vayas todavía. Falta poco para cenar."), Loc.Mark("Descansá. Yo cuido el fuego.")],
        }),
        (50, new()
        {
            [BossLine.Intro] = [Loc.Mark("Pasá. Ya no hace falta que pelees... pero sé que querés."), Loc.Mark("El fuego se mantiene solo, ¿viste? Tardé años en notarlo.")],
            [BossLine.Defeated] = [Loc.Mark("Gracias por volver. Acá siempre sos bienvenido."), Loc.Mark("No es un adiós. Es un hasta la próxima vuelta.")],
            [BossLine.Victory] = [Loc.Mark("Sentate. Hoy cocino yo y vos mirás."), Loc.Mark("Descansá, que mañana hay otra vuelta.")],
        }),
        (90, new()
        {
            [BossLine.Intro] = [Loc.Mark("Estaba esperándote. No a los demás: a vos."), Loc.Mark("Por fin entendí quién era el que llegaba.")],
            [BossLine.Defeated] = [Loc.Mark("Ya casi. Una vuelta más y me siento."), Loc.Mark("Guardame un lugar en la próxima.")],
            [BossLine.Victory] = [Loc.Mark("No pasa nada. Hay tiempo. Siempre hubo tiempo."), Loc.Mark("Descansá. Yo no me voy a ningún lado.")],
        }),
    ];

    // Las frases del Asador para esa cantidad de Fuegos Nuevos: el escalón más alto que ya alcanzó, o las de la vuelta 1.
    private static string[] AsadorLines(BossLine line, int fuegoNuevo)
    {
        var stage = AsadorStages.LastOrDefault(s => fuegoNuevo >= s.FromFuegoNuevo);
        return stage.Lines is null ? BossLines[FogonRules.BossName][line] : stage.Lines[line];
    }

    // Para un jefe que no está en la tabla (uno nuevo en la base): frases genéricas, así nunca queda mudo.
    private static readonly Dictionary<BossLine, string[]> GenericBossLines = new()
    {
        [BossLine.Intro] = [Loc.Mark("¡Nadie pasa por aquí sin pelear conmigo!"), Loc.Mark("Llegaste lejos, pero hasta acá llegaste.")],
        [BossLine.Defeated] = [Loc.Mark("Imposible... me venciste..."), Loc.Mark("Esto... no termina acá...")],
        [BossLine.Victory] = [Loc.Mark("¿Eso era todo? Volvé cuando seas más fuerte."), Loc.Mark("Otro que cae ante mí.")],
    };

    public static string Boss(string bossName, string emoji, BossLine line, Random? rng = null)
    {
        var lines = BossLines.TryGetValue(bossName, out var own) ? own[line] : GenericBossLines[line];
        return Say(bossName, emoji, Pick(lines, rng));
    }

    // ---------------- Los monstruos de cacería y de viaje ----------------
    // TODOS los monstruos hablan, no solo los jefes: al aparecer (Intro), al caer (Defeated: ganó el jugador) y cuando te vencen (Victory: ganó el monstruo).
    // Mismas tres situaciones que los jefes (por eso se reusa BossLine) y la misma regla: cada tabla con 2 o más frases distintas. Van por nombre, tal cual
    // está en la base (monsters.name).
    private static readonly Dictionary<string, Dictionary<BossLine, string[]>> MonsterLines = new()
    {
        // Zona 1 — Praderas del Mate
        ["Jabalí Rabioso"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("¡Groink! Este pastizal es mío, forastero."), Loc.Mark("Mis colmillos tienen hambre... y vos tenés cara de almuerzo.")],
            [BossLine.Defeated] = [Loc.Mark("Groink... me ganaste... bien jugado..."), Loc.Mark("Mis colmillos... ya no clavan como antes...")],
            [BossLine.Victory] = [Loc.Mark("¡Jajá! Pisoteado como el pasto."), Loc.Mark("Volvé cuando tengas más garra, flaco.")],
        },
        ["Ñandú Salvaje"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("¡Brrruum! ¡Salí de mi pampa o te corro a picotazos!"), Loc.Mark("Soy más rápido que vos y más terco también.")],
            [BossLine.Defeated] = [Loc.Mark("Ay, mis plumas... me ganaste de pura suerte..."), Loc.Mark("Hoy corrí... pero no alcanzó.")],
            [BossLine.Victory] = [Loc.Mark("¡Ja! Te dejé más pelado que una gallina."), Loc.Mark("Corré, corré, que mañana te alcanzo.")],
        },
        ["Toro Bravo"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("¡MUUU! Se acabó el corral: ahora la plaza es mía."), Loc.Mark("Agitá el trapo, que te voy a embestir.")],
            [BossLine.Defeated] = [Loc.Mark("Mu... qué cornada la tuya..."), Loc.Mark("Vas a ser leyenda en el pago... yo me voy a la sombra.")],
            [BossLine.Victory] = [Loc.Mark("¡Embestido y por los aires!"), Loc.Mark("Olé. Eso se llama faena.")],
        },
        // Zona 2 — Bosque de Cenizas
        ["Espíritu del Monte"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("Uuuh... este monte tiene dueño, y no está vivo."), Loc.Mark("Un susurro en la ceniza... y vos sos el próximo.")],
            [BossLine.Defeated] = [Loc.Mark("Me desvanezco... el monte me llama..."), Loc.Mark("Otro que me deja descansar... gracias.")],
            [BossLine.Victory] = [Loc.Mark("Uuuh... ahora el monte te susurra a vos."), Loc.Mark("Un alma más para el humo.")],
        },
        ["Puma de las Cenizas"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("Grrr... te vengo siguiendo desde el último árbol quemado."), Loc.Mark("El silencio es mi garra. Y ya estás a tiro.")],
            [BossLine.Defeated] = [Loc.Mark("Mi garra... se apagó..."), Loc.Mark("Fuiste más rápido que mi sombra...")],
            [BossLine.Victory] = [Loc.Mark("Cayó el cazador, cazado."), Loc.Mark("Ni me oíste venir, ¿no?")],
        },
        ["Ciervo Sagrado"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("La ceniza bendice a quien camina en paz. ¿Venís en paz?"), Loc.Mark("Mis astas guardan este bosque. Retrocedé.")],
            [BossLine.Defeated] = [Loc.Mark("Que la ceniza te sea leve, cazador..."), Loc.Mark("El bosque... recordará esto.")],
            [BossLine.Victory] = [Loc.Mark("Volvé cuando aprendas a respetar el monte."), Loc.Mark("Ni la ceniza bendita te salvó.")],
        },
        // Zona 3 — Minas del Yunque
        ["Excavador Profundo"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("Cavé hasta acá... y no pienso soltar mi veta."), Loc.Mark("Clic, clac. ¿Venís a robarme las gemas?")],
            [BossLine.Defeated] = [Loc.Mark("Mis gemas... mi veta... todo para vos..."), Loc.Mark("Me derrumbaste más rápido que un túnel.")],
            [BossLine.Victory] = [Loc.Mark("¡Enterrado! Ahí te quedás, con las piedras."), Loc.Mark("Cavá más hondo la próxima, minero.")],
        },
        ["Gólem del Yunque"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("CLANG. INTRUSO DETECTADO. FORJA ACTIVA."), Loc.Mark("Mi puño es yunque. Tu cabeza es clavo.")],
            [BossLine.Defeated] = [Loc.Mark("CLANG... sistema... enfriando..."), Loc.Mark("Me desarmaste... pieza por pieza...")],
            [BossLine.Victory] = [Loc.Mark("CLANG. UN CLAVO MENOS."), Loc.Mark("GOLPE EJECUTADO. VOLVÉ CUANDO SEAS DE ACERO.")],
        },
        ["Mole de Escoria"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("Rrrmmm... soy la montaña que se mueve."), Loc.Mark("La mina entera es mi cuerpo. Vas a sentir cada piedra.")],
            [BossLine.Defeated] = [Loc.Mark("Me... desmoronooo..."), Loc.Mark("Rrrm... solo quedan escombros...")],
            [BossLine.Victory] = [Loc.Mark("Rrrmmm. Aplastado como un mineral más."), Loc.Mark("La escoria no perdona, minero.")],
        },
        // Zona 4 — Cordillera del Fuego
        ["Coloso de Magma"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("El monte entero arde en mis venas. ¡Arrodillate o derretite!"), Loc.Mark("BRRRM. Cada paso mío es una erupción.")],
            [BossLine.Defeated] = [Loc.Mark("Mi lava... se enfría..."), Loc.Mark("Qué fuego tenés, mortal...")],
            [BossLine.Victory] = [Loc.Mark("Derretido, como cera de vela."), Loc.Mark("Eso se llama volcán, aventurero.")],
        },
        ["Salamandra Infernal"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("¡Ssss! Tengo las llamas más sabrosas de la cordillera."), Loc.Mark("Ssss... ¿venís a calentarte un poco?")],
            [BossLine.Defeated] = [Loc.Mark("Ssss... se me apagó la cola..."), Loc.Mark("Ahora sí... me enfrié...")],
            [BossLine.Victory] = [Loc.Mark("¡Ssss! Quedaste bien crocante."), Loc.Mark("Chamuscado y servido.")],
        },
        ["Dragón de Lava"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("¡RAAAWR! Mi aliento derrite el acero. ¿Probamos con tu armadura?"), Loc.Mark("Volé sobre estos picos mil años. Nadie sube sin mi permiso.")],
            [BossLine.Defeated] = [Loc.Mark("Mi fuego... se extingue... digno guerrero..."), Loc.Mark("Que los picos... me guarden...")],
            [BossLine.Victory] = [Loc.Mark("Una pizca de aliento y listo. Qué decepción."), Loc.Mark("Chamuscado. Volvé en otros mil años.")],
        },
        // Zona 5 — Cráter de la Escoria
        ["Devorador de Almas"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("Tu alma huele... deliciosa..."), Loc.Mark("Tengo hambre... y vos traés algo para llenar el vacío.")],
            [BossLine.Defeated] = [Loc.Mark("Mi hambre era infinita... y aun así..."), Loc.Mark("Me devolvés las almas... qué cruel.")],
            [BossLine.Victory] = [Loc.Mark("Ñam. Gracias por el alma, caminante."), Loc.Mark("Un bocado más para el vacío.")],
        },
        ["Titán de Escoria"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("Soy el corazón de piedra del cráter. No hay paso."), Loc.Mark("Mil años dormí. ¿Quién me despierta?")],
            [BossLine.Defeated] = [Loc.Mark("Mi corazón de piedra... se agrieta..."), Loc.Mark("Un mortal... derribó a un titán...")],
            [BossLine.Victory] = [Loc.Mark("Aplastado como una hormiga."), Loc.Mark("El cráter no se rinde ante nadie.")],
        },
        ["Quimera del Abismo"] = new()
        {
            [BossLine.Intro] = [Loc.Mark("¡Rraaaw, ssss! Tres bocas, tres hambres, ¡y una sola presa!"), Loc.Mark("Del abismo vengo... y al abismo te llevo.")],
            [BossLine.Defeated] = [Loc.Mark("Tres cabezas... y ninguna ganó..."), Loc.Mark("El abismo... me recibe...")],
            [BossLine.Victory] = [Loc.Mark("Tres bocas para un solo bocado."), Loc.Mark("El abismo siempre cobra.")],
        },
    };

    // Para un monstruo que no está en la tabla (uno nuevo en la base): frases genéricas, así nunca queda mudo.
    private static readonly Dictionary<BossLine, string[]> GenericMonsterLines = new()
    {
        [BossLine.Intro] = [Loc.Mark("¡Grrr! Este es mi territorio."), Loc.Mark("Te estaba esperando... con hambre.")],
        [BossLine.Defeated] = [Loc.Mark("Ugh... me ganaste esta vez..."), Loc.Mark("Qué golpe... me voy a acordar.")],
        [BossLine.Victory] = [Loc.Mark("¡Grrr! Volvé cuando seas más fuerte."), Loc.Mark("Uno menos en mi territorio.")],
    };

    // Lo que dice CUALQUIER monstruo (de cacería, de viaje o jefe) en esa situación. isBoss solo importa si no está en ninguna tabla: elige qué genéricas usar.
    // fuegoNuevo: solo le importa al Asador Eterno, que habla distinto con cada escalón de vueltas (AsadorStages).
    public static string Monster(string name, string emoji, BossLine line, bool isBoss = false, Random? rng = null, int fuegoNuevo = 0)
    {
        string[] lines =
            name == FogonRules.BossName ? AsadorLines(line, fuegoNuevo) :
            BossLines.TryGetValue(name, out var boss) ? boss[line] :
            MonsterLines.TryGetValue(name, out var own) ? own[line] :
            isBoss ? GenericBossLines[line] : GenericMonsterLines[line];
        return Say(name, emoji, Pick(lines, rng));
    }

    // Para las pruebas: toda tabla de frases (nombre -> lista).
    public static IEnumerable<(string Key, IReadOnlyList<string> Lines)> AllTables() =>
        BlacksmithLines.Select(kv => ($"herrero:{kv.Key}", (IReadOnlyList<string>)kv.Value))
            .Concat([("herrero:Greeting@FN1", (IReadOnlyList<string>)BlacksmithReturningGreetings), ("tendero:Greeting@FN1", ShopkeeperReturningGreetings)])
            .Concat(ShopkeeperLines.Select(kv => ($"tendero:{kv.Key}", (IReadOnlyList<string>)kv.Value)))
            .Concat(InnkeeperLines.Select(kv => ($"tabernero:{kv.Key}", (IReadOnlyList<string>)kv.Value)))
            .Concat(DeepShopkeeperLines.Select(kv => ($"inframundo:{kv.Key}", (IReadOnlyList<string>)kv.Value)))
            .Concat(BossLines.SelectMany(b => b.Value.Select(kv => ($"{b.Key}:{kv.Key}", (IReadOnlyList<string>)kv.Value))))
            .Concat(AsadorStages.SelectMany(s => s.Lines.Select(kv => ($"{FogonRules.BossName}@FN{s.FromFuegoNuevo}:{kv.Key}", (IReadOnlyList<string>)kv.Value))))
            .Concat(GenericBossLines.Select(kv => ($"jefe-genérico:{kv.Key}", (IReadOnlyList<string>)kv.Value)))
            .Concat(MonsterLines.SelectMany(m => m.Value.Select(kv => ($"{m.Key}:{kv.Key}", (IReadOnlyList<string>)kv.Value))))
            .Concat(GenericMonsterLines.Select(kv => ($"monstruo-genérico:{kv.Key}", (IReadOnlyList<string>)kv.Value)));

    public static IReadOnlyCollection<string> KnownBosses => BossLines.Keys;

    // Los monstruos de cacería y de viaje con frases propias (los jefes están en KnownBosses).
    public static IReadOnlyCollection<string> KnownMonsters => MonsterLines.Keys;
}

// La imagen de un personaje. Reference es lo que va en el embed: una URL, o "attachment://archivo.jpg" si la imagen viaja como adjunto
// del mismo mensaje (LocalPath = el archivo a adjuntar).
public sealed record NpcImage(string Reference, string? LocalPath);

// Imágenes de los personajes. De dónde salen, en este orden: (1) una URL en el .env (Images__Blacksmith / Images__Innkeeper = https://...), que gana si está; (2) el archivo que viene con el bot en Assets/npc/ (blacksmith.jpg, innkeeper.jpg),
// que se ADJUNTA al mensaje: así no hace falta hospedar nada (los links de adjuntos de Discord vencen al día o dos, por eso no sirven
// como URL). Sin ninguna de las dos el personaje se muestra igual, solo con su texto. Son chicas (256x256) para que cada mensaje suba
// unos 25 KB.
public static class NpcImages
{
    public static NpcImage? Blacksmith => Find("Images__Blacksmith", "blacksmith.jpg");
    public static NpcImage? Innkeeper => Find("Images__Innkeeper", "innkeeper.jpg");

    // El Tabernero del Inframundo (v0.17.0): su foto es un emoji de la APLICACIÓN (tabernero_deep, subido al portal el 2026-10-19), así que no hay que alojar nada: la miniatura sale de la URL del CDN de ese emoji, igual
    // que las caras de los monstruos y los íconos de los ítems. Una variable Images__DeepShopkeeper (URL) o un Assets/npc/deepshop.jpg (256x256) ganan si existen.
    public const string DeepShopkeeperEmoji = "<:tabernero_deep:1561756257898209311>";

    public static NpcImage? DeepShopkeeper =>
        Find("Images__DeepShopkeeper", "deepshop.jpg")
        ?? (ItemDisplay.ImageUrl(DeepShopkeeperEmoji, size: 256) is { } emojiUrl ? new NpcImage(emojiUrl, null) : null);

    private static NpcImage? Find(string urlKey, string? fileName)
    {
        string? url = Environment.GetEnvironmentVariable(urlKey);
        if (!string.IsNullOrWhiteSpace(url) && Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            return new NpcImage(url, null);
        }

        if (fileName is null)
        {
            return null;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "npc", fileName);
        return File.Exists(path) ? new NpcImage($"attachment://{fileName}", path) : null;
    }

    // Le pone la imagen del personaje a un embed ya armado (como miniatura), o lo devuelve igual si no hay imagen.
    public static Discord.Embed Decorate(Discord.Embed embed, NpcImage? image) =>
        image is null ? embed : Discord.EmbedBuilderExtensions.ToEmbedBuilder(embed).WithThumbnailUrl(image.Reference).Build();

    // Si el embed lleva una imagen local (attachment://), el archivo que hay que mandar JUNTO con el mensaje; null si no hace falta.
    public static string? AttachmentPathFor(Discord.Embed embed)
    {
        string? url = embed.Thumbnail?.Url;
        if (url is null || !url.StartsWith("attachment://", StringComparison.Ordinal))
        {
            return null;
        }

        return new[] { Blacksmith, Innkeeper }.FirstOrDefault(image => image?.Reference == url)?.LocalPath;
    }

    // Manda el embed al canal, con su imagen adjunta si la tiene (comandos de texto).
    public static async Task SendAsync(Discord.IMessageChannel channel, Discord.Embed embed, Discord.MessageComponent? components = null)
    {
        string? file = AttachmentPathFor(embed);
        if (file is null)
        {
            await channel.SendMessageAsync(embed: embed, components: components);
        }
        else
        {
            await channel.SendFileAsync(file, embed: embed, components: components);
        }
    }
}
