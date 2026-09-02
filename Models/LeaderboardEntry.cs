namespace BotDsRpg.Models;

public sealed class LeaderboardEntry
{
    public long DiscordId { get; init; }
    public string Class { get; init; } = string.Empty;
    public int Level { get; init; }
    public int Xp { get; init; }
}
