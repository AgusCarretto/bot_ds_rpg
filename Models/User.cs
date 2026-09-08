namespace BotDsRpg.Models;

// Refleja 1:1 la tabla "users". DiscordId se guarda como long porque Npgsql
// mapea BIGINT a Int64; los IDs de Discord (ulong) entran sin problema en ese rango.
public sealed class User
{
    public long DiscordId { get; init; }
    public string Class { get; init; } = string.Empty;
    public int Level { get; init; }
    public int Xp { get; init; }
    public int Gold { get; init; }
    public int MaxHp { get; init; }
    public int CurrentHp { get; init; }
    public int? WeaponId { get; init; }
    public int? AmuletId { get; init; }
    public int DailyStreak { get; init; }
    public DateTime? LastDailyClaim { get; init; }
    public int CurrentZoneId { get; init; }
    public int HighestZoneCleared { get; init; }
}
