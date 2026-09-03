namespace BotDsRpg.GameData;

// Estadísticas de combate derivadas del nivel + equipo. Separado de CombatMath (que resuelve un
// golpe puntual, ej. RollHit) para que GameModule (/profile) y AdventureCombatStarter (preparación
// de /hunt, /travel y /autohunt) compartan exactamente la misma fórmula sin duplicarla. El nivel
// ahora pesa en combate por sí solo, no solo a través del equipo — sienta la base para futuras
// Zonas de Dificultad (ej. escalar BaseAttack/BaseDefense según la zona, no solo el nivel).
public static class CombatStats
{
    // Ataque base: piso de 8 (para que nunca sea un chiste desarmado) + 2 por nivel. "= Nivel" a
    // secas (probado en el server) dejaba a un Guerrero nivel 1 desarmado pegando SIEMPRE 1 de
    // daño (RollHit sobre una base de 1 redondea a 1 en todo el rango 0.8x-1.2x) — imposible de
    // ganarle a un bicho de ~25-45 HP como los del pool de /hunt. Con este piso, nivel 1 pega
    // ~8-12 por golpe: la pelea se resuelve en pocos turnos con riesgo real, no una muerte segura.
    public static int BaseAttack(int level) => 8 + (level * 2);

    // Defensa base sí crece 1 a 1 con el nivel (mitiga, no determina si el bicho es matable).
    public static int BaseDefense(int level) => level;

    // Total = base (nivel) + lo que aporta el equipo (arma con sinergia de clase ya aplicada por
    // el llamador / amuleto). El HP Máximo sigue su lógica aparte (ver LevelingCalculator).
    public static int TotalAttack(int level, int weaponDamage) => BaseAttack(level) + weaponDamage;
    public static int TotalDefense(int level, int amuletDefense) => BaseDefense(level) + amuletDefense;
}
