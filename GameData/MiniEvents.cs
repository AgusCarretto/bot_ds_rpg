namespace BotDsRpg.GameData;

public enum MiniEventKind { Stones, Wood, Silver }

// Un material de lo que se lleva alguien (el ítem de la base y cuántas unidades).
public sealed record MiniEventItem(int ItemId, string Name, int Quantity);

// Lo que gana UNO de los que se suman: materiales (Items) o plata (Gold). Nunca los dos: la plata es un tipo de evento y los materiales, los otros dos.
public sealed record MiniEventReward(IReadOnlyList<MiniEventItem> Items, int Gold);

// Las reglas del minievento (puras, sin Discord ni base): qué cuenta cada tipo y cuánto se lleva cada uno según cuánta gente se suma.
//
// "MIENTRAS MÁS PERSONAS, MÁS RECOMPENSA": lo que se lleva CADA UNO crece con la cantidad de participantes (no se reparte un premio fijo:
// alone te llevás poco y entre varios cada uno se lleva más), hasta un tope. Es chico a propósito: nunca reemplaza a jugar.
//   · Piedra / Madera (v0.65.0): una TIRADA de /mine o de /chop por persona que se sume (1 solo, hasta 10), con TU multiplicador de recolección (Fuego Nuevo, bendiciones y oficio) y la misma tabla de rarezas de
//     /mine y /chop (sin el Mítico, como el hallazgo de las expediciones). Antes eran 2 unidades de Piedra o Madera de Pino por persona, siempre iguales: con el Fuego Nuevo (×3 la cantidad en el FN 10) no
//     valía ni apretar el botón (pedido de los jugadores). Así vale lo que valen n acciones tuyas.
//   · Plata: tantas "cacerías de oro" de TU zona como personas se sumaron (1 solo, hasta 10): escala con tu zona igual que las misiones.
public static class MiniEventRules
{
    // A partir de acá más gente no suma más (y la recompensa más grande queda acotada).
    public const int MaxCountedParticipants = 10;

    public static int Counted(int participants) => Math.Clamp(participants, 1, MaxCountedParticipants);

    // Cuántas tiradas de /mine o /chop vale el evento para cada uno (0 en el de plata).
    public static int Rolls(MiniEventKind kind, int participants) => kind == MiniEventKind.Silver ? 0 : Counted(participants);

    // El oro del evento de plata (0 en los otros dos): una cacería de oro de TU zona por persona.
    public static int GoldFor(MiniEventKind kind, int participants, int zoneRank) =>
        kind == MiniEventKind.Silver ? Counted(participants) * MissionRewards.GoldUnit(zoneRank) : 0;

    // De qué tipo de ítem salen las tiradas: Mineral para las piedras (/mine) y Madera para la leña (/chop).
    public static string GatherType(MiniEventKind kind) => kind == MiniEventKind.Stones ? "Mineral" : "Madera";

    // El premio de UNO. pool: los materiales de la base (del tipo que corresponde); multiplier: su multiplicador de /mine o /chop (1 si no se conoce). rng se inyecta para probarlo.
    public static MiniEventReward Reward(MiniEventKind kind, int participants, int zoneRank, IReadOnlyList<GatherCandidate> pool, double multiplier, Random? rng = null)
    {
        return kind == MiniEventKind.Silver
            ? new MiniEventReward([], GoldFor(kind, participants, zoneRank))
            : new MiniEventReward(RollMaterials(pool, GatherType(kind), Rolls(kind, participants), multiplier, rng ?? Random.Shared), 0);
    }

    // Las tiradas de recolección: cada una saca una rareza (la tabla de /mine y /chop, SIN Mítico), un ítem de esa rareza y cuántas unidades (GatheringYield, por el multiplicador del jugador). Lo repetido se junta.
    // Si el catálogo no tiene nada de la rareza que salió, cuenta como Común (nunca se queda sin premio por un catálogo incompleto).
    public static IReadOnlyList<MiniEventItem> RollMaterials(IReadOnlyList<GatherCandidate> pool, string type, int rolls, double multiplier, Random rng)
    {
        var totals = new Dictionary<int, (GatherCandidate Item, int Quantity)>();
        for (int i = 0; i < rolls; i++)
        {
            string rarity = RarityCatalog.RollGatheringRarityNoMythic(rng);
            var candidates = pool.Where(p => p.Type == type && p.Rarity == rarity).ToList();
            if (candidates.Count == 0)
            {
                rarity = "Común";
                candidates = pool.Where(p => p.Type == type && p.Rarity == rarity).ToList();
            }

            if (candidates.Count == 0)
            {
                continue;
            }

            var item = candidates[rng.Next(candidates.Count)];
            int quantity = GatheringYield.RollScaled(rarity, multiplier, rng);
            totals[item.ItemId] = totals.TryGetValue(item.ItemId, out var have) ? (item, have.Quantity + quantity) : (item, quantity);
        }

        return totals.Values
            .OrderBy(t => RarityCatalog.RankOf(t.Item.Rarity)).ThenBy(t => t.Item.Name)
            .Select(t => new MiniEventItem(t.Item.ItemId, t.Item.Name, t.Quantity))
            .ToList();
    }

    // «6× Piedra · 1× Hierro» o «💰 112 oro». Lo más raro va al final (el orden de RollMaterials).
    public static string Describe(MiniEventReward reward) =>
        reward.Items.Count > 0
            ? string.Join(" · ", reward.Items.Select(i => $"{i.Quantity}× {ItemEmojiRegistry.Label(i.Name, string.Empty)}"))
            : $"💰 {reward.Gold} oro";

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
