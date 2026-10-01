namespace BotDsRpg.GameData;

public enum MiniEventKind { Stones, Wood, Silver }

// Lo que gana UNO de los que se suman: un material (ItemName x Quantity) o plata (Gold). Nunca los dos.
public sealed record MiniEventReward(string? ItemName, int Quantity, int Gold);

// Las reglas del minievento (puras, sin Discord ni base): qué cuenta cada tipo y cuánto se lleva cada uno según cuánta gente se suma.
//
// "MIENTRAS MÁS PERSONAS, MÁS RECOMPENSA": lo que se lleva CADA UNO crece con la cantidad de participantes (no se reparte un premio fijo:
// alone te llevás poco y entre varios cada uno se lleva más), hasta un tope. Es chico a propósito: nunca reemplaza a jugar.
//   · Piedra / Madera de Pino (comunes): 2 unidades solo, +2 por cada persona más (tope 20: casi 7 acciones de /mine o /chop).
//   · Plata: tantas "cacerías de oro" de TU zona como personas se sumaron (1 solo, hasta 10): escala con tu zona igual que las misiones.
public static class MiniEventRules
{
    // A partir de acá más gente no suma más (y la recompensa más grande queda acotada).
    public const int MaxCountedParticipants = 10;

    public static int Counted(int participants) => Math.Clamp(participants, 1, MaxCountedParticipants);

    public static MiniEventReward Reward(MiniEventKind kind, int participants, int zoneRank)
    {
        int n = Counted(participants);

        return kind switch
        {
            MiniEventKind.Stones => new MiniEventReward("Piedra", 2 * n, 0),
            MiniEventKind.Wood => new MiniEventReward("Madera de Pino", 2 * n, 0),
            _ => new MiniEventReward(null, 0, n * MissionRewards.GoldUnit(zoneRank)),
        };
    }

    public static string Describe(MiniEventReward reward) =>
        reward.ItemName is not null ? $"{reward.Quantity}× {reward.ItemName}" : $"💰 {reward.Gold} oro";

    public static string Emoji(MiniEventKind kind) => kind switch { MiniEventKind.Stones => "🪨", MiniEventKind.Wood => "🪵", _ => "💰" };

    // El aviso que aparece en el canal (título y relato). Varias versiones por tipo, una al azar.
    private static readonly Dictionary<MiniEventKind, (string Title, string[] Stories)> Announcements = new()
    {
        [MiniEventKind.Stones] =
        (
            "¡Se le cayó una bolsa de piedras a un minero!",
            [
                "Un minero tropezó en el camino y desparramó su bolsa de piedras. ¡Vení a juntar alguna antes de que se las lleven!",
                "A un minero se le rompió la bolsa en plena bajada y las piedras salieron rodando. ¡Corré a buscar!",
            ]
        ),
        [MiniEventKind.Wood] =
        (
            "¡Se desató un atado de leña!",
            [
                "A un leñador se le soltó el atado en plena cuesta y la leña rueda por todos lados. ¡Vení a juntar un poco!",
                "Se le rompió la soga a un leñador y los troncos se esparcieron por el camino. ¡Apurate!",
            ]
        ),
        [MiniEventKind.Silver] =
        (
            "¡Se le rompió el bolsillo a un viajero!",
            [
                "A un viajero distraído se le rompió el bolsillo y las monedas ruedan por el camino. ¡Juntá las que puedas!",
                "Un mercader tropezó y su monedero salió volando. ¡Hay plata en el piso!",
            ]
        ),
    };

    public static (string Title, string Story) Announcement(MiniEventKind kind, Random? rng = null)
    {
        var (title, stories) = Announcements[kind];
        return (title, stories[(rng ?? Random.Shared).Next(stories.Length)]);
    }

    // Para las pruebas.
    public static IEnumerable<(MiniEventKind Kind, string Title, IReadOnlyList<string> Stories)> AllAnnouncements() =>
        Announcements.Select(kv => (kv.Key, kv.Value.Title, (IReadOnlyList<string>)kv.Value.Stories));
}
