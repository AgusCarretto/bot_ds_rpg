namespace BotDsRpg.GameData;

public sealed record LevelingResult(int Level, int Xp, int MaxHp, int CurrentHp, int LevelsGained);

// Cálculo puro de nivelado (sin acceso a datos), compartido por UserRepository.AddXpAsync,
// UserRepository.ClaimDailyAsync y AdventureRepository.ApplyVictoryAsync para que los tres
// apliquen exactamente la misma fórmula.
public static class LevelingCalculator
{
    // Vida máxima que suma cada nivel (también la usa GameData/LevelUpCard.cs para decirlo).
    public const int HpGainedPerLevel = 15;

    // XP que pide cada nivel al cuadrado (v0.28.0, al estilo de EPIC RPG, que usa 125 × nivel²): antes era 100 × nivel^1,5, una curva mucho más plana (en el nivel 50: 35.355 contra 100.000 ahora).
    // El 40 sale de medir el juego perfecto con la EXP de pelea de nuestras zonas (tests/v0280test): con 125 el nivel 25 del Fogón tardaba 58 h; con 40, ~19 h (antes ~13,5 h) y a las 31 h de juego el nivel
    // es ~32 (antes 43). Los premios que se pagan en % del nivel (daily, misiones, logros, Arena) siguen esta curva solos.
    public const int XpPerLevelSquared = 40;

    // XP necesaria para pasar del nivel actual al siguiente.
    public static int RequiredXpForLevel(int level) => XpPerLevelSquared * level * level;

    // currentHp debe ser el HP ya con cualquier daño de combate aplicado (si corresponde):
    // si el jugador sube de nivel acá, se lo cura al máximo como parte de la subida.
    public static LevelingResult ApplyXpGain(int level, int xp, int maxHp, int currentHp, int xpGained)
    {
        xp += xpGained;
        int levelsGained = 0;

        int required = RequiredXpForLevel(level);
        while (xp >= required)
        {
            xp -= required;
            level++;
            levelsGained++;
            maxHp += HpGainedPerLevel;
            required = RequiredXpForLevel(level);
        }

        int newCurrentHp = levelsGained > 0 ? maxHp : currentHp;

        return new LevelingResult(level, xp, maxHp, newCurrentHp, levelsGained);
    }
}
