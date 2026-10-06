namespace BotDsRpg.Models;

// Resultado de cualquier operación que pueda otorgar XP (AddXpAsync, AdventureRepository.ApplyVictoryAsync):
// el usuario ya actualizado y cuántos niveles subió de golpe (0 si no subió ninguno).
// EggGranted (v0.10.0): la especie cuyo huevo se le entregó en esta misma transacción, porque era la PRIMERA vez que vencía al jefe de esa zona (solo ApplyBossVictoryAsync).
// Gate (v0.11.0): si esta victoria abrió El Fogón Eterno (primera vez que vence al jefe de la última zona) o venció al Asador Eterno (habilita el Fuego Nuevo); ver GameData/FogonRules.cs.
public sealed record LevelUpOutcome(User Player, int LevelsGained, PetSpecies? EggGranted = null, BotDsRpg.GameData.GateEvent Gate = BotDsRpg.GameData.GateEvent.None);
