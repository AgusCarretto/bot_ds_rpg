namespace BotDsRpg.Models;

public enum ArenaJoinStatus
{
    Joined,        // quedó anotado
    AlreadyJoined, // ya estaba anotado en este torneo (doble click, o lo mandó dos veces)
    Full,          // el cupo está lleno (GameData/ArenaRules.MaxPlayers)
    NoAccount,     // no tiene cuenta (no pasó por /start)
    Closed,        // el torneo de ese día ya no está abierto (se jugó o se canceló)
}

// Un anotado en un torneo, con su clase y nivel de hoy (para listarlo). DisplayName es el nombre con el que se anotó.
public sealed record ArenaEntry(ulong DiscordId, string DisplayName, string PlayerClass, int Level, DateTime JoinedAtUtc);

// El torneo de un día. Status: open (anotándose), resolved (se jugó) o cancelled (no llegaron a 2 anotados).
public sealed record ArenaDayInfo(
    DateOnly Day, string Status, ulong? ChannelId, ulong? WinnerId, string? WinnerName, int Participants, int Rounds,
    string? RewardText, DateTime? ResolvedAtUtc);

// Una pelea de la llave tal como queda guardada. P2 null = pasó directo. WinnerHpPercent: con cuánta vida terminó el ganador (para narrarla).
public sealed record ArenaMatchRow(
    int Round, int Slot, ulong P1Id, string P1Name, ulong? P2Id, string? P2Name, ulong WinnerId, int Actions, int WinnerHpPercent = 100);

// Un puesto del podio que cobra ADEMÁS del campeón (v0.65.0): el 2.º (perdió la final) o un 3.º (perdió una semifinal). zoneRank es la zona de ESE jugador (el premio escala con ella, como el del campeón).
public sealed record ArenaPlacing(int Place, ulong DiscordId, string Name, GameData.RewardSpec Reward, int ZoneRank);

// Lo que cobró cada uno de esos puestos. Receipt null = ya no tenía cuenta (el torneo igual queda jugado y no hay a quién pagarle).
public sealed record ArenaPlacingReceipt(int Place, ulong DiscordId, string Name, RewardReceipt? Receipt);

// Resolved false = otro proceso ya había cerrado ese día (no se pagó nada de nuevo). Receipt null con Resolved true = el campeón ya no tenía cuenta.
// Others (v0.65.0): lo que cobraron el 2.º y los 3.º; vacío si no se pidió pagarles.
public sealed record ArenaResolveOutcome(bool Resolved, RewardReceipt? Receipt, IReadOnlyList<ArenaPlacingReceipt>? Others = null);
