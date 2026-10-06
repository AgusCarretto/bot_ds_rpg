namespace BotDsRpg.GameData;

public enum BlacksmithLine { Greeting, Success, NotEnough, UnknownRecipe, WrongClass, NoRecipes, SlotTaken, AlreadyEquipped }

public enum ShopkeeperLine { Greeting, BuySuccess, NoGold, BoxWait, SellSuccess, SellAll, NothingToSell, NotForSale, NotOwned, Unsellable, GearConfirm, GearKept, Exchange }

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
        [BlacksmithLine.SlotTaken] =
        [
            "Ya andás con una pieza puesta, compa. Vendésela al tabernero y hablamos.",
            "No te puedo poner otra encima. Primero desprendete de la que traés y vuelvo a la fragua.",
            "Una a la vez, che. Vendé la que tenés puesta y te forjo la nueva.",
        ],
        [BlacksmithLine.AlreadyEquipped] =
        [
            "Esa ya la llevás puesta, ¿qué más querés? Elegí otra cosa.",
            "Pero si ya la tenés encima, crack. Pedime otra pieza.",
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
            "Una caja cada dos horas, que si no me vacían el depósito. Volvé en un rato.",
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
        [ShopkeeperLine.GearConfirm] =
        [
            "Ojo, que eso es lo que llevás puesto. ¿Seguro que querés desprenderte?",
            "¿Vender lo que traés encima? Pensalo bien, que después hay que volver a forjarlo.",
        ],
        [ShopkeeperLine.GearKept] =
        [
            "Hacés bien, quedate con tu pieza. Cuando quieras cambiar, acá estoy.",
            "Sensato. Mejor guardarla hasta tener la próxima lista.",
        ],
        [ShopkeeperLine.Exchange] =
        [
            "Tres por uno, trato hecho. La mala racha se compensa con un buen cambalache.",
            "Mala suerte con los drops, ¿eh? Tomá, algo es algo.",
            "Cambalache cerrado: yo me quedo con tres y vos con lo que te faltaba.",
        ],
        [ShopkeeperLine.Unsellable] =
        [
            "Eso no lo compro, es un premio. Quedátelo.",
            "Ni loco te lo compro: eso es un regalo, no mercadería.",
        ],
    };

    // El que atiende la tienda ES el tabernero (una sola taberna, un solo personaje).
    public static string Shopkeeper(ShopkeeperLine line, Random? rng = null) => Say("El Tabernero", "🍺", Pick(ShopkeeperLines[line], rng));

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

    // ---------------- Los monstruos de cacería y de viaje ----------------
    // TODOS los monstruos hablan, no solo los jefes: al aparecer (Intro), al caer (Defeated: ganó el jugador) y cuando te vencen (Victory: ganó el monstruo).
    // Mismas tres situaciones que los jefes (por eso se reusa BossLine) y la misma regla: cada tabla con 2 o más frases distintas. Van por nombre, tal cual
    // está en la base (monsters.name).
    private static readonly Dictionary<string, Dictionary<BossLine, string[]>> MonsterLines = new()
    {
        // Zona 1 — Praderas del Mate
        ["Jabalí Rabioso"] = new()
        {
            [BossLine.Intro] = ["¡Groink! Este pastizal es mío, forastero.", "Mis colmillos tienen hambre... y vos tenés cara de almuerzo."],
            [BossLine.Defeated] = ["Groink... me ganaste... bien jugado...", "Mis colmillos... ya no clavan como antes..."],
            [BossLine.Victory] = ["¡Jajá! Pisoteado como el pasto.", "Volvé cuando tengas más garra, flaco."],
        },
        ["Ñandú Salvaje"] = new()
        {
            [BossLine.Intro] = ["¡Brrruum! ¡Salí de mi pampa o te corro a picotazos!", "Soy más rápido que vos y más terco también."],
            [BossLine.Defeated] = ["Ay, mis plumas... me ganaste de pura suerte...", "Hoy corrí... pero no alcanzó."],
            [BossLine.Victory] = ["¡Ja! Te dejé más pelado que una gallina.", "Corré, corré, que mañana te alcanzo."],
        },
        ["Toro Bravo"] = new()
        {
            [BossLine.Intro] = ["¡MUUU! Se acabó el corral: ahora la plaza es mía.", "Agitá el trapo, que te voy a embestir."],
            [BossLine.Defeated] = ["Mu... qué cornada la tuya...", "Vas a ser leyenda en el pago... yo me voy a la sombra."],
            [BossLine.Victory] = ["¡Embestido y por los aires!", "Olé. Eso se llama faena."],
        },
        // Zona 2 — Bosque de Cenizas
        ["Espíritu del Monte"] = new()
        {
            [BossLine.Intro] = ["Uuuh... este monte tiene dueño, y no está vivo.", "Un susurro en la ceniza... y vos sos el próximo."],
            [BossLine.Defeated] = ["Me desvanezco... el monte me llama...", "Otro que me deja descansar... gracias."],
            [BossLine.Victory] = ["Uuuh... ahora el monte te susurra a vos.", "Un alma más para el humo."],
        },
        ["Puma de las Cenizas"] = new()
        {
            [BossLine.Intro] = ["Grrr... te vengo siguiendo desde el último árbol quemado.", "El silencio es mi garra. Y ya estás a tiro."],
            [BossLine.Defeated] = ["Mi garra... se apagó...", "Fuiste más rápido que mi sombra..."],
            [BossLine.Victory] = ["Cayó el cazador, cazado.", "Ni me oíste venir, ¿no?"],
        },
        ["Ciervo Sagrado"] = new()
        {
            [BossLine.Intro] = ["La ceniza bendice a quien camina en paz. ¿Venís en paz?", "Mis astas guardan este bosque. Retrocedé."],
            [BossLine.Defeated] = ["Que la ceniza te sea leve, cazador...", "El bosque... recordará esto."],
            [BossLine.Victory] = ["Volvé cuando aprendas a respetar el monte.", "Ni la ceniza bendita te salvó."],
        },
        // Zona 3 — Minas del Yunque
        ["Excavador Profundo"] = new()
        {
            [BossLine.Intro] = ["Cavé hasta acá... y no pienso soltar mi veta.", "Clic, clac. ¿Venís a robarme las gemas?"],
            [BossLine.Defeated] = ["Mis gemas... mi veta... todo para vos...", "Me derrumbaste más rápido que un túnel."],
            [BossLine.Victory] = ["¡Enterrado! Ahí te quedás, con las piedras.", "Cavá más hondo la próxima, minero."],
        },
        ["Gólem del Yunque"] = new()
        {
            [BossLine.Intro] = ["CLANG. INTRUSO DETECTADO. FORJA ACTIVA.", "Mi puño es yunque. Tu cabeza es clavo."],
            [BossLine.Defeated] = ["CLANG... sistema... enfriando...", "Me desarmaste... pieza por pieza..."],
            [BossLine.Victory] = ["CLANG. UN CLAVO MENOS.", "GOLPE EJECUTADO. VOLVÉ CUANDO SEAS DE ACERO."],
        },
        ["Mole de Escoria"] = new()
        {
            [BossLine.Intro] = ["Rrrmmm... soy la montaña que se mueve.", "La mina entera es mi cuerpo. Vas a sentir cada piedra."],
            [BossLine.Defeated] = ["Me... desmoronooo...", "Rrrm... solo quedan escombros..."],
            [BossLine.Victory] = ["Rrrmmm. Aplastado como un mineral más.", "La escoria no perdona, minero."],
        },
        // Zona 4 — Cordillera del Fuego
        ["Coloso de Magma"] = new()
        {
            [BossLine.Intro] = ["El monte entero arde en mis venas. ¡Arrodillate o derretite!", "BRRRM. Cada paso mío es una erupción."],
            [BossLine.Defeated] = ["Mi lava... se enfría...", "Qué fuego tenés, mortal..."],
            [BossLine.Victory] = ["Derretido, como cera de vela.", "Eso se llama volcán, aventurero."],
        },
        ["Salamandra Infernal"] = new()
        {
            [BossLine.Intro] = ["¡Ssss! Tengo las llamas más sabrosas de la cordillera.", "Ssss... ¿venís a calentarte un poco?"],
            [BossLine.Defeated] = ["Ssss... se me apagó la cola...", "Ahora sí... me enfrié..."],
            [BossLine.Victory] = ["¡Ssss! Quedaste bien crocante.", "Chamuscado y servido."],
        },
        ["Dragón de Lava"] = new()
        {
            [BossLine.Intro] = ["¡RAAAWR! Mi aliento derrite el acero. ¿Probamos con tu armadura?", "Volé sobre estos picos mil años. Nadie sube sin mi permiso."],
            [BossLine.Defeated] = ["Mi fuego... se extingue... digno guerrero...", "Que los picos... me guarden..."],
            [BossLine.Victory] = ["Una pizca de aliento y listo. Qué decepción.", "Chamuscado. Volvé en otros mil años."],
        },
        // Zona 5 — Cráter de la Escoria
        ["Devorador de Almas"] = new()
        {
            [BossLine.Intro] = ["Tu alma huele... deliciosa...", "Tengo hambre... y vos traés algo para llenar el vacío."],
            [BossLine.Defeated] = ["Mi hambre era infinita... y aun así...", "Me devolvés las almas... qué cruel."],
            [BossLine.Victory] = ["Ñam. Gracias por el alma, caminante.", "Un bocado más para el vacío."],
        },
        ["Titán de Escoria"] = new()
        {
            [BossLine.Intro] = ["Soy el corazón de piedra del cráter. No hay paso.", "Mil años dormí. ¿Quién me despierta?"],
            [BossLine.Defeated] = ["Mi corazón de piedra... se agrieta...", "Un mortal... derribó a un titán..."],
            [BossLine.Victory] = ["Aplastado como una hormiga.", "El cráter no se rinde ante nadie."],
        },
        ["Quimera del Abismo"] = new()
        {
            [BossLine.Intro] = ["¡Rraaaw, ssss! Tres bocas, tres hambres, ¡y una sola presa!", "Del abismo vengo... y al abismo te llevo."],
            [BossLine.Defeated] = ["Tres cabezas... y ninguna ganó...", "El abismo... me recibe..."],
            [BossLine.Victory] = ["Tres bocas para un solo bocado.", "El abismo siempre cobra."],
        },
    };

    // Para un monstruo que no está en la tabla (uno nuevo en la base): frases genéricas, así nunca queda mudo.
    private static readonly Dictionary<BossLine, string[]> GenericMonsterLines = new()
    {
        [BossLine.Intro] = ["¡Grrr! Este es mi territorio.", "Te estaba esperando... con hambre."],
        [BossLine.Defeated] = ["Ugh... me ganaste esta vez...", "Qué golpe... me voy a acordar."],
        [BossLine.Victory] = ["¡Grrr! Volvé cuando seas más fuerte.", "Uno menos en mi territorio."],
    };

    // Lo que dice CUALQUIER monstruo (de cacería, de viaje o jefe) en esa situación. isBoss solo importa si no está en ninguna tabla: elige qué genéricas usar.
    public static string Monster(string name, string emoji, BossLine line, bool isBoss = false, Random? rng = null)
    {
        string[] lines =
            BossLines.TryGetValue(name, out var boss) ? boss[line] :
            MonsterLines.TryGetValue(name, out var own) ? own[line] :
            isBoss ? GenericBossLines[line] : GenericMonsterLines[line];
        return Say(name, emoji, Pick(lines, rng));
    }

    // Para las pruebas: toda tabla de frases (nombre -> lista).
    public static IEnumerable<(string Key, IReadOnlyList<string> Lines)> AllTables() =>
        BlacksmithLines.Select(kv => ($"herrero:{kv.Key}", (IReadOnlyList<string>)kv.Value))
            .Concat(ShopkeeperLines.Select(kv => ($"tendero:{kv.Key}", (IReadOnlyList<string>)kv.Value)))
            .Concat(InnkeeperLines.Select(kv => ($"tabernero:{kv.Key}", (IReadOnlyList<string>)kv.Value)))
            .Concat(BossLines.SelectMany(b => b.Value.Select(kv => ($"{b.Key}:{kv.Key}", (IReadOnlyList<string>)kv.Value))))
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
