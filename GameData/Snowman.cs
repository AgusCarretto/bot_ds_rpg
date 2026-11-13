namespace BotDsRpg.GameData;

// Un proyectil para tirarle al Muñeco: se paga con Regalos y pega Damage. Los caros rinden un poco más por Regalo (premian juntar).
public sealed record SnowProjectile(string Key, string Name, string Emoji, int Price, int Damage);

// Un puesto del ranking de una vuelta del Muñeco: quién, cuánto daño y en qué puesto quedó (1 = el que más pegó).
public sealed record SnowmanRank(ulong DiscordId, long Damage, int Rank);

// El Muñeco de Nieve Gigante (v0.57.0, docs/navidad.md; el dueño 2026-11-13): UNO solo para todos los servidores, del 1 al 24 de diciembre. Se le tiran proyectiles comprados con
// Regalos y su vida baja para todos a la vez. Cuando muere, TODOS los que le pegaron en esa vuelta cobran (más el top 10 y el top 3) y, si todavía caen Regalos, vuelve con
// ×1,5 de vida («volvió más enojado»). Premios: el dueño pidió «mucho más de Regalos», un Cofre de Oro como mínimo para todos, más Regalos al top 10 y un Cofre de Escoria al
// top 3. Las cantidades exactas y la vida inicial son propuesta, «a medir» (la vida se puede cambiar con Christmas__SnowmanHp sin tocar código).
public static class SnowmanRules
{
    public const int DefaultInitialHp = 100_000;
    public const double RespawnMultiplier = 1.5;
    public const int MaxThrowsPerCommand = 50;

    public const int AllGifts = 150;            // a todos los que le pegaron
    public const int Top10Gifts = 300;          // además, a los 10 que más le pegaron
    public const int TopRewarded = 10;
    public const int TopEscoria = 3;
    public const string AllBox = "Cofre de Oro";
    public const string TopBox = "Cofre de Escoria";
    public const int MinDust = 10;
    public const int MaxDust = 500;
    public const int DamagePerDust = 10;        // 1 de Polvo cada 10 de daño, entre 10 y 500

    public const string KillStat = "snowman_kill";      // vueltas del Muñeco en las que pegaste y cayó (los títulos)
    public const string DamageStat = "snowman_damage";  // daño total al Muñeco, de por vida

    public static readonly IReadOnlyList<SnowProjectile> Projectiles =
    [
        new("bola", "Bola de Nieve", "❄️", 1, 10),
        new("baston", "Bastón de Caramelo", "🍬", 10, 110),
        new("petardo", "Petardo", "🧨", 50, 600),
        new("trineo", "Trineo", "🛷", 200, 2600),
    ];

    public static SnowProjectile? Find(string? key)
    {
        string wanted = (key ?? string.Empty).Trim().ToLowerInvariant();
        return Projectiles.FirstOrDefault(p => p.Key == wanted);
    }

    public static int RespawnHp(int previousMaxHp) => (int)Math.Min(int.MaxValue, Math.Round(previousMaxHp * RespawnMultiplier));

    public static int DustFor(long damage) => (int)Math.Clamp(damage / DamagePerDust, MinDust, MaxDust);

    public static int GiftsFor(int rank) => AllGifts + (rank <= TopRewarded ? Top10Gifts : 0);
}
