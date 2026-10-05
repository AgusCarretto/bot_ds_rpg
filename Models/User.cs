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
    public bool HasBank { get; init; }      // compró la cuenta del banco (GameData/BankRules.cs)
    public int BankGold { get; init; }      // el oro guardado en el banco: la penalidad por muerte no lo toca
    public int Dust { get; init; }          // Polvo: sale de desmantelar materiales y se gasta en encantar
    public int WeaponEnchant { get; init; } // tier del encantamiento del arma puesta (0 = ninguno, 1..5; GameData/Enchantments.cs)
    public int AmuletEnchant { get; init; } // ídem del amuleto
}
