namespace BotDsRpg.Repositories;

// Una herramienta que lleva puesta un jugador (v0.29.0): Kind = "axe" o "pickaxe" (GameData/ToolRules.cs), Multiplier = items.stat_value (2 o 3).
public sealed record PlayerTool(string Kind, int ItemId, string Name, string? Emoji, string Rarity, int Multiplier);

// Las herramientas de cada jugador, en la tabla player_tools (Database/add_tools.sql): una por tipo. Armar una nueva (CraftingRepository.CraftAsync) la reemplaza y el Fuego Nuevo las borra.
public interface IToolRepository
{
    // Las herramientas que tiene puestas (0, 1 o 2). Sin ninguna se recolecta ×1.
    Task<IReadOnlyList<PlayerTool>> GetAsync(ulong discordId, CancellationToken cancellationToken = default);
}
