namespace BotDsRpg.GameData;

// Bonus pasivos derivados PURA y EXCLUSIVAMENTE de la clase del jugador (ver ClassCatalog.All).
// 1.0/0.0 = "sin efecto", para que sumar/multiplicar por estos valores sea siempre seguro sin
// chequear la clase en cada lugar que resuelve un golpe.
public sealed record ClassPassiveProfile(
    double MaxHpMultiplier, // Guerrero: HP Máximo de combate ×1.2 (ver Services/AdventureCombatStarter.cs)
    double DamageTakenMultiplier, // Guerrero: daño final recibido ×0.9
    double DodgeChanceBonus, // Ninja: +0.15 sobre CombatMath.DodgeChance
    double CritChanceBonus, // Arquero: +0.15 sobre CombatMath.CritChance
    double LifestealChance, // Hechicero: probabilidad de Sifón de Almas
    double LifestealRatio); // Hechicero: % del daño infligido que cura si el Sifón procede

// Patrón strategy simple (switch por nombre de clase) para no repartir "if (player.Class == ...)"
// por todo el código de combate — GameData/CombatMath.cs y Services/AdventureCombatStarter.cs son
// los únicos que necesitan conocer esto. Sienta la base para sumar pasivas nuevas o por nivel más
// adelante: alcanza con agregar un caso acá.
public static class ClassPassives
{
    private static readonly ClassPassiveProfile None = new(
        MaxHpMultiplier: 1.0,
        DamageTakenMultiplier: 1.0,
        DodgeChanceBonus: 0.0,
        CritChanceBonus: 0.0,
        LifestealChance: 0.0,
        LifestealRatio: 0.0);

    public static ClassPassiveProfile For(string playerClass) => playerClass switch
    {
        // Tanque: más HP efectivo en combate + reduce el daño final que le pega el monstruo.
        "Guerrero" => None with { MaxHpMultiplier = 1.2, DamageTakenMultiplier = 0.9 },

        // Sifón de Almas: al atacar, 30% de probabilidad de curarse el 50% del daño infligido.
        "Hechicero" => None with { LifestealChance = 0.30, LifestealRatio = 0.50 },

        // Puntería: crítico base 0.10 + 0.15 = 0.25.
        "Arquero" => None with { CritChanceBonus = 0.15 },

        // Reflejos: evasión base 0.05 + 0.15 = 0.20.
        "Ninja" => None with { DodgeChanceBonus = 0.15 },

        _ => None,
    };
}
