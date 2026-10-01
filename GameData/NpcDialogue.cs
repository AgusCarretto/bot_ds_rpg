namespace BotDsRpg.GameData;

public enum BlacksmithLine { Greeting, Success, NotEnough, UnknownRecipe, WrongClass, NoRecipes }

public enum ShopkeeperLine { Greeting, BuySuccess, NoGold, BoxWait, SellSuccess, SellAll, NothingToSell, NotForSale, NotOwned, Unsellable }

public enum InnkeeperLine { Healed, NothingToEat, FullHp, InCombat }

// Lo que dicen los personajes con los que se interactúa: el herrero, el tendero, el tabernero y los jefes de zona. Cada situación tiene
// varias frases y se elige una al azar (así no se lee siempre lo mismo). Puro: se prueba sin Discord (el Random se puede inyectar).
// Para sumar frases basta con agregarlas a la lista de la situación; para un personaje nuevo, un enum y una tabla acá.
public static class NpcDialogue
{
    // "🔨 **El Herrero:** «No tenés lo suficiente, crack...»" — el formato único de todo lo que dice alguien.
    public static string Say(string name, string emoji, string text) => $"{emoji} **{name}:** «{text}»";

    private static string Pick(IReadOnlyList<string> lines, Random? rng) => lines[(rng ?? Random.Shared).Next(lines.Count)];

    // ---------------- El Herrero ----------------
    private static readonly Dictionary<BlacksmithLine, string[]> BlacksmithLines = new()
    {
        [BlacksmithLine.Greeting] =
        [
            "¿Qué necesita, señor? Elija de la lista y yo me encargo.",
            "Pase, pase. El yunque está caliente. ¿Qué le forjo?",
            "Buenas, forastero. Dígame qué necesita y le doy forma.",
        ],
        [BlacksmithLine.Success] =
        [
            "¡En camino, loco! Queda pronta.",
            "Perfecto, forjado. Mirá qué belleza te quedó.",
            "¡Listo el pollo! Ahí tenés, cuidala que me costó sudor.",
        ],
        [BlacksmithLine.NotEnough] =
        [
            "No tenés lo suficiente, crack. Andá a farmear y después hablamo.",
            "Con eso no me alcanza ni para encender la fragua. Volvé cuando juntes lo que falta.",
            "Faltan cosas, che. Salí a juntar y vení que te espero.",
        ],
        [BlacksmithLine.UnknownRecipe] =
        [
            "Esa receta no la conozco, amigo. Fijate en la lista.",
            "¿Eso? Nunca lo forjé. Pedime algo de la lista.",
        ],
        [BlacksmithLine.WrongClass] =
        [
            "Eso es para otro oficio, compa. A vos no te va a servir.",
            "Esa pieza se hace a medida para otra clase. Elegí otra.",
        ],
        [BlacksmithLine.NoRecipes] =
        [
            "Por ahora no tengo nada para vos en esta zona. Volvé más adelante.",
            "Todavía no sé qué hacerte por acá. Pasá por otra zona y hablamos.",
        ],
    };

    public static string Blacksmith(BlacksmithLine line, Random? rng = null) => Say("El Herrero", "🔨", Pick(BlacksmithLines[line], rng));

    // ---------------- El Tendero ----------------
    private static readonly Dictionary<ShopkeeperLine, string[]> ShopkeeperLines = new()
    {
        [ShopkeeperLine.Greeting] =
        [
            "Pase, pase, ¿qué le sirvo?",
            "Bienvenido a la tienda. Mirá bien, que todo tiene su precio.",
            "Buenas. Tengo comida fresca y cajas con sorpresa.",
        ],
        [ShopkeeperLine.BuySuccess] =
        [
            "¡Trato hecho! Que lo disfrutes.",
            "Excelente elección. Vuelva cuando quiera.",
            "Ahí tiene. Cuidalo bien.",
        ],
        [ShopkeeperLine.NoGold] =
        [
            "No te alcanza la plata, amigo. Juntá y volvé.",
            "Sin monedas no hay trato. Andá a cazar un poco.",
            "Uy, estás corto de oro. Vení cuando te sobre.",
        ],
        [ShopkeeperLine.BoxWait] =
        [
            "Ya te vendí una caja hace poco. Dejame que me reponga el stock.",
            "Una caja por hora, que si no me vacían el depósito. Volvé en un rato.",
        ],
        [ShopkeeperLine.SellSuccess] =
        [
            "Bien, te lo compro. Aquí tenés tus monedas.",
            "Hecho. Un placer hacer negocios.",
            "Perfecto, lo anoto en el libro.",
        ],
        [ShopkeeperLine.SellAll] =
        [
            "¡Qué cargamento! Te llevo todo.",
            "Vaya, vaya, traés la mochila llena. Cerramos trato por todo.",
        ],
        [ShopkeeperLine.NothingToSell] =
        [
            "No tenés nada para vender, che. Vení cuando traigas algo.",
            "La mochila vacía, amigo. Salí a juntar y volvé.",
        ],
        [ShopkeeperLine.NotForSale] =
        [
            "Eso no lo vendo, amigo. Fijate en el mostrador.",
            "De eso no tengo. Mirá lo que hay en la tienda.",
        ],
        [ShopkeeperLine.NotOwned] =
        [
            "No tenés eso para vender, che. Mirá bien la mochila.",
            "Eso no lo veo por ningún lado. No me hagas perder el tiempo.",
        ],
        [ShopkeeperLine.Unsellable] =
        [
            "Eso no lo compro, es un premio. Quedátelo.",
            "Ni loco te lo compro: eso es un regalo, no mercadería.",
        ],
    };

    public static string Shopkeeper(ShopkeeperLine line, Random? rng = null) => Say("El Tendero", "🛒", Pick(ShopkeeperLines[line], rng));

    // ---------------- El Tabernero (/heal) ----------------
    private static readonly Dictionary<InnkeeperLine, string[]> InnkeeperLines = new()
    {
        [InnkeeperLine.Healed] =
        [
            "Comé tranquilo, que estás entre amigos. ¡Salud!",
            "Buen provecho, forastero. Con esto te ponés de pie.",
            "Un guiso y una siesta y quedás como nuevo.",
        ],
        [InnkeeperLine.NothingToEat] =
        [
            "No tenés nada para comer. Comprá algo en la tienda y vení.",
            "Con la panza vacía no te puedo servir nada. Pasá por la tienda.",
        ],
        [InnkeeperLine.FullHp] =
        [
            "Estás más sano que un roble. No necesitás nada.",
            "Pero si estás hecho un toro. Guardá el hambre para después.",
        ],
        [InnkeeperLine.InCombat] =
        [
            "¡Ahora no, che! Primero terminá la pelea.",
            "En medio de un combate no te sirvo ni un mate. ¡Pelea!",
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
            [BossLine.Intro] = ["¡Nadie pisa mis praderas sin pagar tributo!", "¡Soy el Rey de las Praderas! ¡Arrodillate!"],
            [BossLine.Defeated] = ["No… no puede ser… un simple aventurero…", "Las praderas… ya no son mías…"],
            [BossLine.Victory] = ["¡Jajaja! ¡Otro más para mi colección!", "Volvé cuando seas digno, plebeyo."],
        },
        ["Lobisón Alfa"] = new()
        {
            [BossLine.Intro] = ["La manada ya te olió, forastero. No saldrás vivo.", "¡Aúúú! Esta luna es tuya... y la última."],
            [BossLine.Defeated] = ["La manada... recordará tu nombre...", "Ganaste... esta noche."],
            [BossLine.Victory] = ["Aullá de dolor, cachorro.", "La manada se alimenta esta noche."],
        },
        ["Capataz de Hierro"] = new()
        {
            [BossLine.Intro] = ["¡Aquí se trabaja, no se pasea! ¡Fuera de mis minas!", "Otro intruso. Vas a cargar yunques toda la vida."],
            [BossLine.Defeated] = ["Mis... mis minas... el yunque se enfría...", "Qué golpe... tenés buen brazo."],
            [BossLine.Victory] = ["A trabajar, vago. Esto no es para vos.", "Tomá nota: hierro duro, cabeza dura."],
        },
        ["Señor del Volcán"] = new()
        {
            [BossLine.Intro] = ["El fuego es mi voz y la ceniza mi trono. ¡Arde!", "¿Osás desafiar al Señor del Volcán? ¡Que se abra la tierra!"],
            [BossLine.Defeated] = ["El fuego... se apaga...", "Imposible... la lava no me abandona..."],
            [BossLine.Victory] = ["Reducido a cenizas, como todos.", "Calentito, ¿no? Eso era solo el principio."],
        },
        ["Soberano de la Escoria"] = new()
        {
            [BossLine.Intro] = ["Mi reino es polvo, y vos serás parte de él.", "De rodillas ante el Soberano. Todo termina en escoria."],
            [BossLine.Defeated] = ["Mi reino... se desmorona... sos digno...", "Así que existe alguien más fuerte..."],
            [BossLine.Victory] = ["Ahora sos escoria, como todos.", "El abismo te reclama."],
        },
    };

    // Para un jefe que no está en la tabla (uno nuevo en la base): frases genéricas, así nunca queda mudo.
    private static readonly Dictionary<BossLine, string[]> GenericBossLines = new()
    {
        [BossLine.Intro] = ["¡Nadie pasa por aquí sin pelear conmigo!", "Llegaste lejos, pero hasta acá llegaste."],
        [BossLine.Defeated] = ["Imposible... me venciste...", "Esto... no termina acá..."],
        [BossLine.Victory] = ["¿Eso era todo? Volvé cuando seas más fuerte.", "Otro que cae ante mí."],
    };

    public static string Boss(string bossName, string emoji, BossLine line, Random? rng = null)
    {
        var lines = BossLines.TryGetValue(bossName, out var own) ? own[line] : GenericBossLines[line];
        return Say(bossName, emoji, Pick(lines, rng));
    }

    // Para las pruebas: toda tabla de frases (nombre -> lista).
    public static IEnumerable<(string Key, IReadOnlyList<string> Lines)> AllTables() =>
        BlacksmithLines.Select(kv => ($"herrero:{kv.Key}", (IReadOnlyList<string>)kv.Value))
            .Concat(ShopkeeperLines.Select(kv => ($"tendero:{kv.Key}", (IReadOnlyList<string>)kv.Value)))
            .Concat(InnkeeperLines.Select(kv => ($"tabernero:{kv.Key}", (IReadOnlyList<string>)kv.Value)))
            .Concat(BossLines.SelectMany(b => b.Value.Select(kv => ($"{b.Key}:{kv.Key}", (IReadOnlyList<string>)kv.Value))))
            .Concat(GenericBossLines.Select(kv => ($"jefe-genérico:{kv.Key}", (IReadOnlyList<string>)kv.Value)));

    public static IReadOnlyCollection<string> KnownBosses => BossLines.Keys;
}

// Imágenes de los personajes. No se pueden guardar en el repo (el bot no hospeda archivos): se ponen como URL en el .env
// (Images__Blacksmith=https://...). Sin URL el personaje se muestra igual, solo con su texto.
public static class NpcImages
{
    public static string? Blacksmith => Read("Images__Blacksmith");

    private static string? Read(string key)
    {
        string? value = Environment.GetEnvironmentVariable(key);
        return !string.IsNullOrWhiteSpace(value) && Uri.IsWellFormedUriString(value, UriKind.Absolute) ? value : null;
    }
}
