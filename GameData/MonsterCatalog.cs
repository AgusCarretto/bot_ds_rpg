namespace BotDsRpg.GameData;

public sealed record MonsterTemplate(string Name, string Emoji);

// Enemigos de sabor narrativo para /hunt (comunes) y /travel (más duros).
// Las estadísticas de combate en sí las calcula CombatService; esto es solo flavor text.
public static class MonsterCatalog
{
    public static readonly IReadOnlyList<MonsterTemplate> HuntMonsters =
    [
        new("Jabalí salvaje", "🐗"),
        new("Lobo hambriento", "🐺"),
        new("Rata gigante", "🐀"),
        new("Bandido novato", "🗡️"),
        new("Serpiente venenosa", "🐍"),
    ];

    public static readonly IReadOnlyList<MonsterTemplate> TravelMonsters =
    [
        new("Orco guerrero", "👹"),
        new("Ogro de las cavernas", "🧌"),
        new("Bandido élite", "🏴"),
        new("Araña gigante", "🕷️"),
        new("Espectro errante", "👻"),
    ];

    public static MonsterTemplate RollFrom(IReadOnlyList<MonsterTemplate> pool) =>
        pool[Random.Shared.Next(pool.Count)];
}
