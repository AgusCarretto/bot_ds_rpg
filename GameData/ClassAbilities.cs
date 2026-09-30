namespace BotDsRpg.GameData;

public enum AbilityKind { Aguante, BolaDeFuego, Sombra, LluviaDeFlechas }

// EffectLabel: cómo se muestra el efecto que dura varios turnos (null si la habilidad es de un solo
// golpe y no deja nada activo).
public sealed record ClassAbility(
    AbilityKind Kind, string Name, string Emoji, int CooldownTurns, string Description, string? EffectLabel);

// TODOS los números de balance de las habilidades viven acá: para retocar una habilidad alcanza con
// cambiar una constante, la lógica (GameData/CombatTurnResolver.cs) no se toca. Efecto MEDIDO
// (simulando usar la habilidad apenas está lista contra solo atacar) sobre el daño hecho / recibido
// por turno: Hechicero x1.30 / x1.00, Arquero x1.20 / x1.00, Ninja x1.07 / x0.66, Guerrero x0.92 / x0.70.
public static class AbilityTuning
{
    // Las cuatro comparten el mismo enfriamiento: turnos del propio jugador (atacar, usar la
    // habilidad o usar un consumible cuentan) durante los que NO se puede volver a usar.
    public const int CooldownTurns = 3;

    // Guerrero — Aguante: ataca al 70% y recibe -60% de daño (se multiplica con su pasiva ×0.9).
    public const double AguanteStrikeMultiplier = 0.70;
    public const double AguanteDamageTakenMultiplier = 0.40;
    public const int AguanteEffectTurns = 2;

    // Hechicero — Bola de Fuego: un solo golpe de 220%.
    public const double BolaDeFuegoMultiplier = 2.20;

    // Ninja — Sombra: no ataca ese turno; 2 turnos con esquive total del 75% (es un TOTAL, no un
    // bonus sobre la pasiva, así sigue valiendo 75% aunque se retoque la pasiva del Ninja) y el
    // primer ataque desde las sombras es crítico garantizado con +25% extra.
    public const int SombraEffectTurns = 2;
    public const double SombraTotalDodgeChance = 0.75;
    public const double SombraAmbushMultiplier = 1.25;

    // Arquero — Lluvia de Flechas: 3 flechas al 60% del daño, cada una con su propio crítico.
    public const int LluviaArrows = 3;
    public const double LluviaArrowMultiplier = 0.60;
}

// Patrón strategy simple (switch por nombre de clase), igual que ClassPassives.For. Cada clase tiene
// exactamente UNA habilidad activa; una clase desconocida no tiene ninguna (sin botón en combate).
public static class ClassAbilities
{
    private static string Pct(double multiplier) => $"{(int)Math.Round(multiplier * 100)}%";

    private static readonly ClassAbility Aguante = new(
        AbilityKind.Aguante, "Aguante", "🛡️", AbilityTuning.CooldownTurns,
        $"Atacás al {Pct(AbilityTuning.AguanteStrikeMultiplier)} y durante {AbilityTuning.AguanteEffectTurns} turnos " +
        $"recibís {Pct(1 - AbilityTuning.AguanteDamageTakenMultiplier)} menos de daño.",
        "Aguante activo");

    private static readonly ClassAbility BolaDeFuego = new(
        AbilityKind.BolaDeFuego, "Bola de Fuego", "🔥", AbilityTuning.CooldownTurns,
        $"Un golpe de fuego que pega el {Pct(AbilityTuning.BolaDeFuegoMultiplier)} de tu daño.",
        null);

    private static readonly ClassAbility Sombra = new(
        AbilityKind.Sombra, "Sombra", "🌑", AbilityTuning.CooldownTurns,
        $"Te desvanecés este turno (no atacás): {AbilityTuning.SombraEffectTurns} turnos con " +
        $"{Pct(AbilityTuning.SombraTotalDodgeChance)} de esquive, y tu próximo ataque es crítico garantizado " +
        $"(+{Pct(AbilityTuning.SombraAmbushMultiplier - 1)}).",
        "Invisible");

    private static readonly ClassAbility LluviaDeFlechas = new(
        AbilityKind.LluviaDeFlechas, "Lluvia de Flechas", "🏹", AbilityTuning.CooldownTurns,
        $"Disparás {AbilityTuning.LluviaArrows} flechas al {Pct(AbilityTuning.LluviaArrowMultiplier)} del daño, " +
        "cada una con su propia chance de crítico.",
        null);

    public static ClassAbility? For(string playerClass) => playerClass switch
    {
        "Guerrero" => Aguante,
        "Hechicero" => BolaDeFuego,
        "Ninja" => Sombra,
        "Arquero" => LluviaDeFlechas,
        _ => null,
    };
}
