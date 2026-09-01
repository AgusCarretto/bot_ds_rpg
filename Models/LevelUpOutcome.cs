namespace BotDsRpg.Models;

// Resultado de cualquier operación que pueda otorgar XP (AddXpAsync, AdventureRepository.ApplyRewardAsync):
// el usuario ya actualizado y cuántos niveles subió de golpe (0 si no subió ninguno).
public sealed record LevelUpOutcome(User Player, int LevelsGained);
