namespace BotDsRpg.Models;

// Resultado de ICraftingRepository.CraftAsync: si falló, FailureReason explica qué faltó
// (oro o un ingrediente puntual) para poder mostrarlo directo en el embed de error.
// EquippedSlot: un arma o un amuleto forjado NO va al inventario, queda equipado ("weapon" / "amulet"). SlotOccupied: no se forjó porque
// ya tenía algo equipado en ese casillero (hay que vender lo equipado primero).
public sealed record CraftOutcome(bool Success, User? Player, string? FailureReason, string? EquippedSlot = null, bool SlotOccupied = false);
