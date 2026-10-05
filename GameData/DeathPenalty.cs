namespace BotDsRpg.GameData;

// Lo que pasó al aplicar la penalidad: cuánto oro se perdió, cuánta EXP (del nivel actual) y con cuánto oro quedó en la billetera.
public sealed record DeathPenaltyOutcome(int GoldLost, int XpLost, int GoldAfter);

// La penalidad por morir (pedida por el dueño, 2026-10-05): al PERDER un combate, estés en el nivel que estés, la EXP del nivel vuelve a 0 y se pierde un
// porcentaje del oro de la billetera. NO baja de nivel (eso rompería misiones y el nivel mínimo de las zonas). El oro del banco no se toca.
// Aplica a toda derrota: /hunt, /travel, /boss, /autohunt, /use en plena pelea y el raid que cae entero. No aplica a huir, al tiempo agotado,
// ni a los duelos y la arena (esos no tocan la base). El UPDATE de IUserRepository.ApplyDeathPenaltyAsync usa este mismo porcentaje.
public static class DeathPenalty
{
    // Se pierde el 5 % del oro (hacia abajo: con menos de 20 de oro no se pierde nada).
    public const int GoldPercent = 5;

    public static int GoldLost(int gold) => (int)(Math.Max(0, (long)gold) * GoldPercent / 100);
}
