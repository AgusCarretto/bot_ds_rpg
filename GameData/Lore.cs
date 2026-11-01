using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Las Crónicas del Fogón (v0.15.0): la historia del juego. Todo lo que se lee vive en ESTE archivo, como los diálogos de GameData/NpcDialogue.cs; las pantallas (Modules/StoryModule.cs)
// y los avisos (Services/ProgressNotifier.cs) solo lo muestran. Diseño y razones: docs/lore/cronicas-del-fogon.md.
//
//   · ACTO I — SUBIR (vuelta 1): el prólogo y un capítulo por jefe. Cada uno se abre la PRIMERA vez que el jugador vence a ese jefe (los de la escalera, y el Asador Eterno). Los capítulos
//     los cuenta el Tabernero, junto al fuego.
//   · ACTO II — SOLTAR (Fuegos Nuevos 1 a 99): cada Fuego Nuevo le saca un poco de pesar al Asador (1 % por vuelta) y en ciertas vueltas se abre una escena. Las cuenta el propio fuego
//     («el fuego recuerda»). La mesa del Fogón acompaña: 40 platos que el Asador va levantando de a poco.
//   · ACTO III — EL QUE VOLVIÓ (Fuego Nuevo 100): el final de esta parte de la historia. A propósito deja puertas abiertas.
//
// NADA se guarda: lo que el jugador ya leyó sale de lo que ya existe (zona más alta vencida, haber vencido al Asador, cuántos Fuegos Nuevos hizo; ver StoryProgress). La historia NO da poder
// (ni stats, ni oro, ni drops): no toca ningún balance. Los sellados no muestran nada (ni título): nadie se spoilea lo que todavía no vio.

// Un capítulo del Acto I. BossName: el jefe cuya primera victoria lo abre (el nombre tal cual está en la base). FirstClearLine: lo que pasa en la pantalla de esa primera victoria.
public sealed record LoreChapter(string Key, int Number, string Title, string BossName, string Text, string FirstClearLine);

// Una escena del Acto II/III: se abre al llegar a ese Fuego Nuevo.
public sealed record LoreScene(string Key, int FuegoNuevo, string Title, string Text);

// Hasta dónde llegó un jugador, lo único que hace falta para saber qué puede leer. BossesBeaten: cuántos jefes de la escalera venció (en la vuelta actual: highest_zone_cleared se reinicia con
// cada Fuego Nuevo, pero con un Fuego Nuevo hecho todo el Acto I ya está abierto). AsadorBeaten: users.gate_cleared de esta vuelta.
public sealed record StoryProgress(int BossesBeaten, bool AsadorBeaten, int FuegoNuevo)
{
    public static StoryProgress For(User player, IReadOnlyList<Zone> orderedZones)
    {
        int beaten = player.HighestZoneCleared <= 0 ? 0 : Math.Max(0, ZoneRanking.RankOf(orderedZones, player.HighestZoneCleared));
        return new StoryProgress(beaten, player.GateCleared, Math.Max(0, player.FuegoNuevo));
    }
}

public static class Lore
{
    // Los platos de la mesa larga del Fogón, el día que cayó la Fragua.
    public const int TableSeats = 40;

    // El Fuego Nuevo en que el Asador queda libre. El pesar baja 1 % por vuelta, así que llega a 0 justo ahí.
    public const int FinalFuegoNuevo = 100;

    public const string PrologueKey = "prologo";

    // ---------------- Cuentas del Acto II ----------------

    // El pesar que todavía carga el Asador, en % (100 con ningún Fuego Nuevo, 0 con 100).
    public static int Pesar(int fuegoNuevo) => Math.Clamp(FinalFuegoNuevo - fuegoNuevo, 0, 100);

    // Cuántos de los cuarenta platos siguen puestos: 0,4 menos por vuelta, redondeado (30 en la vuelta 25, 20 en la 50, 10 en la 75, ninguno en la 99).
    public static int Plates(int fuegoNuevo) =>
        Math.Clamp(TableSeats - (int)Math.Round(Math.Max(0, fuegoNuevo) * TableSeats / (double)FinalFuegoNuevo, MidpointRounding.AwayFromZero), 0, TableSeats);

    // ---------------- Desbloqueos ----------------

    public static bool IsUnlocked(LoreChapter chapter, StoryProgress progress) =>
        progress.FuegoNuevo >= 1 || (chapter.Number <= LadderChapters ? progress.BossesBeaten >= chapter.Number : progress.AsadorBeaten);

    public static bool IsUnlocked(LoreScene scene, StoryProgress progress) => progress.FuegoNuevo >= scene.FuegoNuevo;

    // Los capítulos de la escalera (1 a 5) son los de los jefes de zona; el 6 es el del Asador.
    public const int LadderChapters = 5;

    public static LoreChapter? ChapterForBoss(string? bossName) =>
        bossName is null ? null : Chapters.FirstOrDefault(c => c.BossName == bossName);

    public static LoreChapter? FindChapter(string? key) => Chapters.FirstOrDefault(c => c.Key == key);

    public static LoreScene? FindScene(string? key) => Scenes.FirstOrDefault(s => s.Key == key);

    // Las escenas que se abren JUSTO al llegar a ese Fuego Nuevo (puede haber más de una en la misma vuelta).
    public static IReadOnlyList<LoreScene> ScenesOpenedAt(int fuegoNuevo) => Scenes.Where(s => s.FuegoNuevo == fuegoNuevo).ToList();

    // El Fuego Nuevo de la próxima escena que todavía no se abrió, o null si ya se abrieron todas.
    public static int? NextSceneFuegoNuevo(int fuegoNuevo) =>
        Scenes.Where(s => s.FuegoNuevo > fuegoNuevo).Select(s => (int?)s.FuegoNuevo).Min();

    public static readonly string PrologueTitle = Loc.Mark("Antes de la ceniza");

    public static readonly string Prologue = Paragraphs(
        Loc.Mark("Sentate, pibe, que el mate todavía está caliente."),
        Loc.Mark("Dicen que antes de las cenizas había una sola luz en el mundo: la **Gran Fragua**. Era un fuego que no se apagaba nunca, y de él salía todo lo bueno: el acero de los arados, el pan de cada mañana y la primera brasa de cada hogar. Alrededor de la Fragua se armaba el fogón, y en el fogón se compartía el mate, se repartía el asado y se contaban las historias."),
        Loc.Mark("Una noche la Fragua se ahogó en su propio fuego y colapsó. Nadie sabe bien por qué. Lo que el fuego devoró se volvió ceniza y escoria; lo que no devoró cambió de forma, y se quedó con hambre."),
        Loc.Mark("Por eso estamos acá, en el Campamento Base, con los pocos que quedaron. Prendemos esta brasa chiquita todas las noches para acordarnos de cómo era. Vos llegaste con las manos vacías, y está bien: toda brasa empieza chica."),
        Loc.Mark("*(Ah, esa silla de al lado está vacía. No, no la muevas. Cosas mías.)*"));

    // ---------------- Acto I: un capítulo por jefe ----------------

    public static readonly IReadOnlyList<LoreChapter> Chapters =
    [
        new("cap1", 1, Loc.Mark("El Rey que no quiso soltar el pasto"), "Rey Jabalí",
            Paragraphs(
                Loc.Mark("Las Praderas del Mate fueron el primer fogón del mundo: pasto tan alto que se perdía un ternero, viento cargado de yerba y, en el medio, un rey. El **Rey Jabalí** no era un bicho cualquiera: era el guardián de la primera brasa, la del mate. Cuando la Fragua cayó, él se quedó. Y de tanto cuidar lo que ya no estaba terminó creyendo que cuidar era no dejar pasar a nadie."),
                Loc.Mark("Vos lo venciste, y eso cambia algo. Al caer soltó la brasa que guardaba, y esa brasa tomó forma de huevo. Cuidala: las brasas crían."),
                Loc.Mark("Seguí hacia el **Bosque de Cenizas**. Dicen que hay un bosque que lleva años quemándose sin terminar de apagarse. Y que de noche, ahí, se escuchan aullidos.")),
            Loc.Mark("Cuando cae, suelta un suspiro largo, como quien por fin puede dejar de vigilar.")),

        new("cap2", 2, Loc.Mark("Séptimo hijo"), "Lobisón Alfa",
            Paragraphs(
                Loc.Mark("Se cuenta que el séptimo hijo varón de una misma casa, si nace con luna mala, se vuelve lobisón. El **Lobisón Alfa** fue el guardabosques: el séptimo de siete hermanos que cuidaron este monte generación tras generación. Cuando el bosque empezó a arder no se fue: se quedó apagando brasas con las patas, noche tras noche, hasta que el fuego le entró por dentro. Hoy aúlla y ni él sabe si pide ayuda o la ofrece."),
                Loc.Mark("Entre los troncos negros queda un solo árbol vivo, el **Árbol de Vida**. Dicen que el fuego no se anima a tocarlo. Dicen."),
                Loc.Mark("Más allá del bosque la tierra se abre. Son las **Minas del Yunque**, donde los fragüeros estaban terminando un último encargo cuando todo se vino abajo. Un encargo que nadie quiere nombrar.")),
            Loc.Mark("El aullido se corta en seco. Por primera vez en años, el bosque no suena a pedido de ayuda.")),

        new("cap3", 3, Loc.Mark("El pedido que no se cancela"), "Capataz de Hierro",
            Paragraphs(
                Loc.Mark("Las Minas del Yunque eran el orgullo de los fragüeros: túneles tallados por generaciones y, al fondo, el taller donde se hacía el mejor acero del mundo. La noche de la caída había un pedido pendiente, el más grande que se recuerde: una **parrilla sin fin**, encargada por alguien que no regateó ni el precio ni la urgencia."),
                Loc.Mark("El **Capataz de Hierro** tomó el encargo con una promesa: «El pedido no se cancela». Y no se canceló. Los fragüeros murieron o se fueron; él siguió. Los gólems que viste son parrillas a medio terminar, que se levantaron solas a seguir el trabajo."),
                Loc.Mark("Cuando lo venciste, el taller se quedó en silencio. Por primera vez en años, nadie estaba forjando nada."),
                Loc.Mark("¿De quién era el pedido? Preguntale al Herrero. Va a hacerse el distraído; insistí.")),
            Loc.Mark("Baja el martillo, mira alrededor y dice una sola cosa: «¿Entonces estaba terminado?»")),

        new("cap4", 4, Loc.Mark("La chimenea del mundo"), "Señor del Volcán",
            Paragraphs(
                Loc.Mark("Arriba de las minas empiezan los picos de la **Cordillera del Fuego**: la chimenea de la Gran Fragua, por donde subía el humo y bajaba la luz. Alguien tenía que cuidar que no se tapara ni se desbordara, y ese fue el **Señor del Volcán**. Un trabajo de equilibrio: ni mucho fuego, ni poco."),
                Loc.Mark("La noche de la caída hubo demasiado, y desde entonces la montaña respira lava y no se acuerda de cómo calmarse. No era malvado: era una chimenea que nadie desatascó. Cuando lo venciste el humo cambió de color y bajó un aire más tibio, casi amable."),
                Loc.Mark("Ya queda poco camino. El **Cráter de la Escoria** es donde estuvo la Fragua. Los que fueron volvieron distintos, o no volvieron.")),
            Loc.Mark("La montaña exhala. Parece, de golpe, una montaña común.")),

        new("cap5", 5, Loc.Mark("El Soberano"), "Soberano de la Escoria",
            Paragraphs(
                Loc.Mark("En el Cráter la tierra se derritió y se enfrió tantas veces que ya no se distingue el suelo del recuerdo. Acá estuvo la Gran Fragua, y acá se coronó el **Soberano de la Escoria**: el último maestro fragüero, que se sentó sobre el yunque más grande y se negó a levantarse cuando el mundo se vino abajo."),
                Loc.Mark("Guardaba un arca, y en el arca la llave de una puerta: la que lleva al **Fogón Eterno**, al final del mundo. Al vencerlo, la puerta se abrió."),
                Loc.Mark("Para entrar vas a necesitar el equipo del Fogón. Lo que hay adentro no es un monstruo cualquiera. Es alguien muy cansado, y es el motivo de todo.")),
            Loc.Mark("Se levanta del yunque por primera vez desde la caída. Es más pequeño de lo que todos creían.")),

        new("cap6", 6, Loc.Mark("La mesa larga"), FogonRules.BossName,
            Paragraphs(
                Loc.Mark("Hay una mesa larga en el fondo del Fogón Eterno: cuarenta lugares, cuarenta platos servidos, y ni una silla ocupada."),
                Loc.Mark("Antes de la caída, el que cuidaba el fogón de todos era el Asador. Esa noche había puesto la mesa para el valle entero —los fragüeros que volvían del turno, los viejos, los chicos— y salió un momento a buscar más leña, para que alcanzara. Cuando volvió, la Fragua se había apagado y nadie se había sentado."),
                Loc.Mark("Nadie sabe por qué cayó. Él cree saberlo: que si no hubiera salido, nada de esto pasaba. Como esa culpa no tiene dónde dejarse, la volvió trabajo. Juró que ningún fuego volvería a apagarse, le dio de comer al fuego bosques, minas y montañas, y encargó una parrilla sin fin para tener con qué cocinar cuando volvieran. Sigue cocinando para cuarenta. Sigue esperando."),
                Loc.Mark("No lo venciste porque fuera malo. Lo venciste porque ya casi no podía sostenerse. Le sacaste un carbón y él te dejó, sin entender por qué no dolía."),
                Loc.Mark("Con ese carbón podés prender un **fuego nuevo**. El mundo vuelve a su primera mañana; vos no, o no del todo. Y cada vez que vuelvas, él va a pesar un poco menos.")),
            Loc.Mark("Mira el carbón en tus manos. «¿Van a venir?», pregunta, bajito. No le contestás. Él tampoco esperaba que lo hicieras.")),
    ];

    // ---------------- Acto II y III: lo que el fuego recuerda ----------------
    // Ordenadas por Fuego Nuevo. Las cuenta el fuego, en tercera persona (el Tabernero es un personaje más).

    public static readonly IReadOnlyList<LoreScene> Scenes =
    [
        new("fn1", 1, Loc.Mark("Otra vuelta"),
            Paragraphs(
                Loc.Mark("Otra vez el pasto alto, otra vez el mate tibio. El Tabernero te mira un segundo de más. «¿Nos conocemos?», dice. «No. Seguro que no.»"),
                Loc.Mark("Te sirve el mate sin que lo pidas. Y deja un segundo mate al lado, para nadie, como si fuera lo más normal del mundo."),
                Loc.Mark("El fuego del campamento se movió un segundo, como si alguien lejos hubiera exhalado. Dicen que el Asador dejó de contar los platos. Un instante nomás. Después volvió a contarlos. Pero ya pesa un poco menos."))),

        new("fn1b", 1, Loc.Mark("Alcanza"),
            Paragraphs(
                Loc.Mark("El Herrero no te mira cuando habla. Pule una pieza que ya está pulida."),
                Loc.Mark("«Yo fui el que entregó la parrilla. Era aprendiz del Soberano y me tocó llevarla hasta el Fogón. El Asador la recibió con las dos manos, como a un regalo. Me preguntó: \"¿Alcanza para todos los que van a venir?\"»"),
                Loc.Mark("«Yo sabía que no venía nadie. Se lo dije como se le dice a un chico: \"Alcanza, Asador. Alcanza para todos.\"»"),
                Loc.Mark("Deja el trapo. «No fue el pedido lo que lo hundió. Fue que yo le mentí con cariño y él me creyó. Todavía no sé si fue una crueldad o un favor.»"))),

        new("fn2", 2, Loc.Mark("Dos mates"),
            Paragraphs(
                Loc.Mark("El Tabernero ya no te pregunta si se conocen. Deja los dos mates, se sienta del otro lado y por primera vez no habla de la silla vacía."),
                Loc.Mark("«Me dijeron que el Herrero durmió toda la noche», comenta, mirando la brasa. «Hace años que no pasaba.»"))),

        new("fn3", 3, Loc.Mark("Los nombres"),
            Paragraphs(
                Loc.Mark("Algo cambió en el Fogón. El Asador ya no cuenta platos: ahora dice nombres. Uno por cada mano que sirve. «Ramona. El Gringo. La Nena de los Ledesma.» No sabe que los dice en voz alta."),
                Loc.Mark("El Tabernero, que siempre tiene una frase lista, esa noche no dijo nada y limpió la misma mesa tres veces."))),

        new("fn5", 5, Loc.Mark("El humo cambió"),
            Paragraphs(
                Loc.Mark("Cinco fuegos nuevos. Cuando te levantaste, las Praderas olían distinto: a pasto mojado y no a humo viejo. El Tabernero abrió la ventana y dijo que hacía años que no podía."),
                Loc.Mark("En el Fogón, el Asador sigue sirviendo. Pero esta vez, antes de apoyar un plato, se quedó mirándolo. Un instante. Treinta y ocho platos en la mesa."))),

        new("fn10", 10, Loc.Mark("Te habla"),
            Paragraphs(
                Loc.Mark("Diez veces se apagó el fuego y diez veces volviste. Ahora, cuando lo enfrentás, el Asador no te pide que pases de largo: te mira."),
                Loc.Mark("«Vos otra vez», dice. «Siempre volvés vos.»"),
                Loc.Mark("No es una amenaza. Es la primera vez en años que nota que hay alguien enfrente."))),

        new("fn15", 15, Loc.Mark("La leña"),
            Paragraphs(
                Loc.Mark("En la vuelta quince se le acabó la leña al Fogón. El Asador se quedó mirando cómo bajaba el fuego, con el hacha en la mano. No salió a buscar más."),
                Loc.Mark("No se apagó. Quedó una brasa baja, firme, que duró toda la noche."),
                Loc.Mark("«Me fui una vez», dijo, casi sin voz. «Y no fue por eso.» Todavía no sabe si lo cree."))),

        new("fn25", 25, Loc.Mark("Treinta platos"),
            Paragraphs(
                Loc.Mark("Cuando entraste al Fogón, la mesa larga tenía diez platos menos. Treinta."),
                Loc.Mark("No te lo dijo, pero los fue levantando de a uno, vuelta tras vuelta, y a cada plato le dijo un nombre y después un adiós bajito. Los lava él mismo, despacio."),
                Loc.Mark("El Tabernero, desde el campamento, ceba el mate y comenta como al pasar: «Mirá que soltar no es olvidar. Es dejar de esperar a que vuelvan.»"))),

        new("fn35", 35, Loc.Mark("Una silla"),
            Paragraphs(
                Loc.Mark("Veintiséis platos. Hoy el Asador arrastró una silla hasta el fuego —la primera de la mesa que se mueve en años— y se quedó ahí, con las manos en las rodillas."),
                Loc.Mark("«¿Querés que te cuente quiénes eran?», te preguntó. Y te contó: de Ramona, que cantaba pésimo y nadie le decía nada; del Gringo, que siempre llegaba tarde y siempre traía algo; de los chicos de los Ledesma, que se escondían debajo de la mesa."),
                Loc.Mark("Lo escuchaste hasta que amaneció. Es la primera vez que nombra a alguien sin que le tiemble la voz."))),

        new("fn50", 50, Loc.Mark("Veinte"),
            Paragraphs(
                Loc.Mark("Veinte platos. Y por primera vez desde la caída, el Asador se sienta. No a la mesa: al lado del fuego, con las manos vacías. Mira las brasas sin darles de comer. Arden igual."),
                Loc.Mark("Eso es lo que no sabía: que un fuego puede quedarse sin que nadie lo alimente a la fuerza."))),

        new("fn75", 75, Loc.Mark("Diez"),
            Paragraphs(
                Loc.Mark("Diez platos. Te recibe de pie, sin parrilla, y te ceba un mate. Lo hace mal: hace siglos que no cebaba para alguien que estuviera vivo."),
                Loc.Mark("Lo tomás igual. «Está bueno», le decís, y por primera vez es casi verdad."))),

        new("fn90", 90, Loc.Mark("Cuatro platos"),
            Paragraphs(
                Loc.Mark("Cuatro platos. El Asador te espera en la puerta, no en la parrilla. «¿Siempre fuiste vos?», pregunta, como si acabara de entender algo que estuvo delante suyo durante cien años."),
                Loc.Mark("No le contestás. Se ríe bajito: es la primera risa del Fogón desde la caída."))),

        new("fn99", 99, Loc.Mark("El último plato"),
            Paragraphs(
                Loc.Mark("Ya no queda ninguno de los cuarenta. Hay un solo plato en la mesa, frente a una silla que nunca vio a nadie."),
                Loc.Mark("«Ese lo dejo para vos», dice. «Pero todavía no. Una vuelta más.»"))),

        // El final de esta parte de la historia. A propósito termina con puertas abiertas (ver docs/lore/cronicas-del-fogon.md, sección 7).
        new("fn100", FinalFuegoNuevo, Loc.Mark("El que volvió"),
            Paragraphs(
                Loc.Mark("Cuando entrás, la mesa tiene un solo plato y una sola silla ocupada: la tuya. El Asador no pelea. Se sienta enfrente, por primera vez desde la caída, y come con vos."),
                Loc.Mark("«Cuarenta lugares puse esa noche», dice. «Y el único que volvió fuiste vos. Cien veces.»"),
                Loc.Mark("Cuando se levanta, el fuego del Fogón no se apaga: se vuelve fuego de campamento. En el Campamento Base, el Tabernero deja de limpiar la mesa y acomoda la silla vacía. La Gran Fragua sigue en ruinas. Pero por primera vez alguien se pregunta, en voz alta, si se puede volver a encender."),
                Loc.Mark("*Esto no termina acá.*"))),
    ];

    // ---------------- Los avisos y las frases chicas ----------------

    // El aviso de capítulo nuevo (Acto I), sin contar nada del contenido.
    public static string ChapterNotice(LoreChapter chapter) =>
        Loc.F($"📖 **Capítulo desbloqueado:** «{Loc.Tr(chapter.Title)}» — leelo con `/story`.");

    // El aviso de escenas nuevas (Acto II/III) al llegar a un Fuego Nuevo, o null si en esa vuelta no se abre ninguna.
    public static string? SceneNotice(int fuegoNuevo)
    {
        var opened = ScenesOpenedAt(fuegoNuevo);
        if (opened.Count == 0)
        {
            return null;
        }

        string titles = string.Join(Loc.T(" y "), opened.Select(s => $"«{Loc.Tr(s.Title)}»"));
        return opened.Count == 1
            ? Loc.F($"📖 **Escena nueva del fuego:** {titles} — leela con `/story`.")
            : Loc.F($"📖 **Escenas nuevas del fuego:** {titles} — leelas con `/story`.");
    }

    // Lo que dice el Tabernero cuando nace tu primera mascota, y el Herrero cuando forjás tu primera pieza (avisos de la primera vez; ver Services/ProgressNotifier.cs).
    public static string FirstPetLine => Loc.T("🍺 **El Tabernero:** «Cuidala. Las brasas crían.»");

    public static string FirstForgeLine => Loc.T("🔨 **El Herrero:** «Tu primera pieza. El fuego tiene buena memoria: acordate de ésta.»");

    // La línea nueva de la bienvenida (/start): la historia queda a un comando de distancia.
    public static readonly string WelcomeHint = Loc.Mark("Cuando quieras saber por qué estamos acá, el Tabernero te lo cuenta con `/story`.");

    // Une párrafos con una línea en blanco (el mismo aire que el resto de las pantallas del bot).
    private static string Paragraphs(params string[] paragraphs) => string.Join("\n\n", paragraphs);

    // Un texto de la historia en el idioma de quien lo lee (v0.45.0): cada párrafo es una entrada del catálogo (Loc.Mark arriba), así que se traduce de a uno.
    public static string Translate(string text) => string.Join("\n\n", text.Split("\n\n").Select(Loc.Tr));
}
