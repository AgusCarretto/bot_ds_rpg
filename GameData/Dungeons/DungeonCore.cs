namespace BotDsRpg.GameData.Dungeons;

// Las mazmorras de las Zonas 6 a 10 (v0.47.0). El dueño: «que se puedan hacer solos… que sea como un puzzle… cada zona nueva tiene una mazmorra distinta, que haya patrones, que sea algo
// interactivo»; «al perder la dungeon volvés a farmear y perdés la reliquia; al pasarla se abre la zona siguiente, o vas derecho a pelear contra el Asador»; «no se puede comer en la mazmorra».
// Cada mazmorra es un acertijo PURO (este archivo y los de al lado): recibe acciones (lo que dice cada botón) y cambia su estado; no sabe nada de Discord ni de la base. La vida, la Reliquia, el
// tiempo y el premio los lleva la sesión (Services/DungeonService.cs). Cada corrida se sortea distinta (con el Random que se le pasa), para que la respuesta no se pueda copiar de otro.
public enum DungeonKind { FirePattern, SmokeMaze, Embers, Lock, Rhythm }

// Qué pasó con una acción: siguió bien, fue un error (saca vida), se resolvió el acertijo, o no correspondía (un botón viejo: no pasa nada).
public enum DungeonStep { Ok, Mistake, Solved, Ignored }

// Un botón del acertijo. Action va dentro del id del botón (corta, sin «:»). Row: la fila de Discord (0..4, hasta 5 botones por fila).
public sealed record DungeonButton(string Action, string Label, string? Emoji, int Row, bool Primary = false, bool Disabled = false);

public interface IDungeonPuzzle
{
    DungeonKind Kind { get; }

    bool Solved { get; }

    // Se perdió por el acertijo mismo (se acabaron los intentos o las rondas), aunque quede vida.
    bool Failed { get; }

    // Aplica lo que el jugador tocó. Un error saca vida (lo descuenta la sesión con DungeonRules.MistakeDamage).
    DungeonStep Apply(string action);

    // El cuerpo del mensaje: el estado del acertijo (texto de Discord).
    string Render();

    // La línea sobre la última jugada («✅ Bien», «❌ Era 💧…»), o null al empezar.
    string? Note { get; }

    IReadOnlyList<DungeonButton> Buttons();
}

public static class DungeonRules
{
    // Si no tocás nada en este tiempo la mazmorra se pierde (como el turno de una pelea, pero más largo: hay que pensar).
    public static readonly TimeSpan ActionTimeout = TimeSpan.FromMinutes(3);

    // La mazmorra de cada zona, por su posición en la escalera (6..10).
    public static DungeonKind? ForZoneRank(int rank) => rank switch
    {
        6 => DungeonKind.FirePattern,
        7 => DungeonKind.SmokeMaze,
        8 => DungeonKind.Embers,
        9 => DungeonKind.Lock,
        10 => DungeonKind.Rhythm,
        _ => null,
    };

    public static IDungeonPuzzle Create(DungeonKind kind, Random rng) => kind switch
    {
        DungeonKind.FirePattern => new FirePattern(rng),
        DungeonKind.SmokeMaze => new SmokeMaze(rng),
        DungeonKind.Embers => new Embers(rng),
        DungeonKind.Lock => new FireLock(rng),
        _ => new Rhythm(rng),
    };

    // Cuánto de la vida máxima saca cada error (en %). Con un juego prolijo se sale con el 70-90 % de la vida; con uno descuidado, no se sale.
    public static int MistakePercent(DungeonKind kind) => kind switch
    {
        DungeonKind.FirePattern => 12,
        DungeonKind.SmokeMaze => 10,
        DungeonKind.Embers => 6,
        DungeonKind.Lock => 10,
        _ => 9,
    };

    public static int MistakeDamage(DungeonKind kind, int maxHp) => Math.Max(1, (int)Math.Round(maxHp * MistakePercent(kind) / 100.0, MidpointRounding.AwayFromZero));

    // El nombre de cada una (lo muestran la pantalla y la ayuda). Propiedades: salen en el idioma de quien lee.
    public static string Name(DungeonKind kind) => kind switch
    {
        DungeonKind.FirePattern => Loc.T("El patrón del fuego"),
        DungeonKind.SmokeMaze => Loc.T("El laberinto de humo"),
        DungeonKind.Embers => Loc.T("Las brasas"),
        DungeonKind.Lock => Loc.T("El candado del Fogón"),
        _ => Loc.T("El ritmo del Asador"),
    };

    // Cómo se juega, en una o dos frases.
    public static string HowTo(DungeonKind kind) => kind switch
    {
        DungeonKind.FirePattern => Loc.T("Las llamas anuncian una serie de golpes; memorizala y contestá cada uno con su contraataque, en orden. Cada piso la serie es más larga."),
        DungeonKind.SmokeMaze => Loc.T("Un laberinto lleno de humo: ves los 4 lados de donde estás (camino blanco, pared negra) y lo que ya recorriste. Encontrá la salida sin pisar las brasas: se ven como un camino más, están donde una rama se desvía y ninguna está en el camino a la salida."),
        DungeonKind.Embers => Loc.T("Un tablero de brasas: tocar una prende o apaga ella y sus vecinas. Prendelas todas; cada 3 toques el viento sopla sobre una (como si la tocara) y te quema."),
        DungeonKind.Lock => Loc.T("Una clave de 4 símbolos. Probá combinaciones: cada intento te dice cuántos están en su lugar y cuántos están pero en otro lado."),
        _ => Loc.T("El Asador pega siguiendo un ciclo que se repite. Aprendelo y anticipá su próximo golpe; hay que acertar varios seguidos."),
    };
}
