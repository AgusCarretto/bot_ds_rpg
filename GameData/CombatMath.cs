namespace BotDsRpg.GameData;

// Resultado de resolver el golpe del monstruo contra el jugador: si Dodged es true, Damage
// siempre es 0 (la evasión ignora por completo el piso de 1 de daño de la mitigación normal).
public sealed record MonsterHitOutcome(bool Dodged, int Damage);

// Resultado de resolver el golpe del jugador contra el monstruo: si Critical es true, Damage ya
// viene con el multiplicador de crítico aplicado.
public sealed record PlayerHitOutcome(bool Critical, int Damage);

// Fórmulas de daño del combate por turnos (ver Modules/AdventureModule.cs, AutoHuntModule.cs y
// UseModule.cs — los tres llaman a Resolve*Hit para no repetir la misma fórmula). El daño base en
// sí (Ataque/Defensa Total) lo calcula GameData/CombatStats.cs — acá solo vive la variación por
// golpe puntual y la resolución de cada golpe (crítico del jugador, evasión del monstruo).
public static class CombatMath
{
    // Probabilidad base de que el jugador esquive por completo el golpe del monstruo. Plana para
    // todas las clases por ahora — sienta la base para un bonus de evasión por clase más adelante
    // (ej. Ninja), que solo necesitaría sumarse acá antes de comparar contra el roll.
    public const double DodgeChance = 0.05;

    // Probabilidad base de golpe crítico del jugador y su multiplicador. Igual que DodgeChance,
    // plana para todas las clases por ahora — sienta la base para un bonus de crítico por clase
    // más adelante (ej. Arquero/Guerrero).
    public const double CritChance = 0.10;
    public const int CritMultiplier = 2;

    // Cada golpe varía entre 80% y 120% del daño base (Ataque Total, ver CombatStats.TotalAttack).
    public static int RollHit(int baseDamage)
    {
        int min = (int)Math.Round(baseDamage * 0.8);
        int max = (int)Math.Round(baseDamage * 1.2);
        return Random.Shared.Next(min, max + 1);
    }

    // Resuelve el golpe del jugador: primero la variación normal (RollHit), y sobre ESE resultado
    // (no sobre el daño base) se aplica el x2 de crítico si corresponde — más simple de razonar
    // ("el crítico duplica lo que hubieras pegado") que intercalarlo antes de la variancia, y
    // matemáticamente equivalente en distribución. critChanceBonus: pasiva de clase (ej. Arquero,
    // ver GameData/ClassPassives.cs), 0 si la clase no tiene bonus de crítico.
    public static PlayerHitOutcome ResolvePlayerHit(int baseDamage, double critChanceBonus = 0)
    {
        int rolled = RollHit(baseDamage);
        bool critical = Random.Shared.NextDouble() <= (CritChance + critChanceBonus);
        int damage = critical ? rolled * CritMultiplier : rolled;
        return new PlayerHitOutcome(critical, damage);
    }

    // Resuelve el golpe del monstruo: primero el chequeo de evasión (0 de daño si esquiva; con
    // dodgeChanceBonus sumado, ej. Ninja), si no esquiva aplica la mitigación normal con un piso de
    // 1 (la Defensa nunca anula el golpe por completo), y por último la pasiva de daño recibido de
    // clase (ej. Guerrero ×0.9, damageTakenMultiplier=1 si no aplica) — con un segundo piso de 1
    // para que esa reducción tampoco pueda dejar el golpe en 0. El daño devuelto es el que hay que
    // restar del HP del jugador — 0 en caso de esquive compone naturalmente con el sistema de Delta
    // de HP, no necesita ningún caso especial.
    public static MonsterHitOutcome ResolveMonsterHit(
        int monsterDamage, int playerDefense, double dodgeChanceBonus = 0, double damageTakenMultiplier = 1.0)
    {
        bool dodged = Random.Shared.NextDouble() <= (DodgeChance + dodgeChanceBonus);
        if (dodged)
        {
            return new MonsterHitOutcome(true, 0);
        }

        int mitigated = Math.Max(1, monsterDamage - playerDefense);
        int finalDamage = Math.Max(1, (int)Math.Round(mitigated * damageTakenMultiplier));
        return new MonsterHitOutcome(false, finalDamage);
    }

    // Sifón de Almas (Hechicero): probabilidad propia (independiente del roll de crítico) de curar
    // un % del daño que el jugador acaba de infligir. 0 si la clase no tiene esta pasiva o si no
    // procede la tirada — el llamador debe clampear el resultado contra el HP Máximo del jugador.
    public static int RollLifesteal(int damageDealt, double lifestealChance, double lifestealRatio)
    {
        if (lifestealChance <= 0 || Random.Shared.NextDouble() > lifestealChance)
        {
            return 0;
        }

        return (int)Math.Round(damageDealt * lifestealRatio);
    }
}
