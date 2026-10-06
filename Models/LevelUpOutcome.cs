namespace BotDsRpg.Models;

// Resultado de cualquier operación que pueda otorgar XP (AddXpAsync, AdventureRepository.ApplyVictoryAsync):
// el usuario ya actualizado y cuántos niveles subió de golpe (0 si no subió ninguno).
// EggGranted (v0.10.0): la especie cuyo huevo se le entregó en esta misma transacción, porque era la PRIMERA vez que vencía al jefe de esa zona (solo ApplyBossVictoryAsync).
public sealed record LevelUpOutcome(User Player, int LevelsGained, PetSpecies? EggGranted = null);
