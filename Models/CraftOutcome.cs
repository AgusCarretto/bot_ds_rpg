namespace BotDsRpg.Models;

// Resultado de ICraftingRepository.CraftAsync: si falló, FailureReason explica qué faltó
// (oro o un ingrediente puntual) para poder mostrarlo directo en el embed de error.
public sealed record CraftOutcome(bool Success, User? Player, string? FailureReason);
