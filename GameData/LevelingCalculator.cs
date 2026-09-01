namespace BotDsRpg.GameData;

public sealed record LevelingResult(int Level, int Xp, int MaxHp, int CurrentHp, int LevelsGained);

// Cálculo puro de nivelado (sin acceso a datos), compartido por UserRepository.AddXpAsync
// y AdventureRepository.ApplyRewardAsync para que ambos apliquen exactamente la misma fórmula.
public static class LevelingCalculator
{
    private const int HpGainedPerLevel = 15;

    // XP necesaria para pasar del nivel actual al siguiente.
    public static int RequiredXpForLevel(int level) => (int)(100 * Math.Pow(level, 1.5));

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
