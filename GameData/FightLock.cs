namespace BotDsRpg.GameData;

// Qué se puede usar EN MEDIO de una pelea (v0.25.4, el dueño, 2026-10-23: «bloqueá todos los comandos si estás en medio de pelea, exceptuando los de la pelea como el use; no quiero que pueda recolectar madera en plena pelea»).
// Un jugador está «en pelea» si tiene un combate solitario (/hunt, /travel, /boss), un jefe en equipo ya peleando o un duelo en curso (Services/FightGuard.cs). Mientras tanto SOLO valen:
//   · las acciones de la pelea: comer (/use), y los comandos escritos «aa attack», «aa ability» y «aa flee» (los botones del combate no son comandos: nunca se bloquean);
//   · lo que solo mira, para decidir qué hacer: /profile, /inventory, /cd, /info y /tutorial.
// Todo lo demás (/chop, /mine, /forge, /taberna, /daily, /play, /give, /zona...) responde con el aviso de abajo y no hace nada. Es una lista de lo PERMITIDO y no de lo prohibido a propósito: un comando nuevo
// queda bloqueado en pelea hasta que alguien decida lo contrario, en vez de colarse. Se aplica en UN solo lugar, antes de despachar el comando (Program.cs: slash y texto), así no hay que acordarse módulo por módulo.
public static class FightLock
{
    // Los nombres son los del comando: el slash de primer nivel («use», «profile») y, en texto, el nombre PRINCIPAL del comando (los alias «atk», «u», «p» apuntan al mismo).
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "use", "attack", "ability", "flee",
        // v0.47.0: la jugada escrita de la mazmorra («aa dg agua»).
        "dungeon",
        "profile", "inventory", "cd", "info", "tutorial",
        // v0.49.0: la ficha de stats solo mira.
        "stats",
    };

    // En una mazmorra (v0.47.0) no se come: valen la jugada escrita y lo que solo mira, pero no «use» (ni atacar o huir, que son de las peleas).
    public static bool IsAllowedInDungeon(string? commandName) =>
        !string.IsNullOrWhiteSpace(commandName) && commandName.Trim().ToLowerInvariant() is "dungeon" or "profile" or "inventory" or "cd" or "info" or "tutorial" or "stats";

    // En una Aventura (v0.57.0) tampoco se come, ni en sus peleas ni entre escenas: valen las jugadas de la pelea y lo que solo mira, pero no «use».
    public static bool IsAllowedInAdventure(string? commandName) =>
        !string.IsNullOrWhiteSpace(commandName) && commandName.Trim().ToLowerInvariant() is "attack" or "ability" or "flee" or "aventura" or "profile" or "inventory" or "cd" or "info" or "tutorial" or "stats";

    public static bool IsAllowed(string? commandName) =>
        !string.IsNullOrWhiteSpace(commandName) && Allowed.Contains(commandName.Trim());

    // El comando de texto «bank deposit» (o «bank») cuenta por su primera palabra, que es el grupo.
    public static string TextKey(string alias) =>
        alias.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;

    // shown: el comando tal como lo escribió («/chop», «aa chop»).
    public static string Message(string shown) =>
        Loc.F($"⚔️ **Estás en plena pelea**: no podés usar `{shown}` hasta que termine. Mientras tanto atacá, comé algo con `/use` o huí.");

    // En la Aventura (v0.58.2): dice cómo volver a ver la escena, por si la pantalla no llegó a mostrarse (la aventura se vence sola a los 3 minutos sin elegir).
    public static string AdventureMessage(string shown) =>
        Loc.F($"🗺️ **Estás en una aventura**: no podés usar `{shown}` hasta que termine. Si no ves los botones, escribí `/aventura` (o `aa aventura`) para volver a ver la escena.");
}
