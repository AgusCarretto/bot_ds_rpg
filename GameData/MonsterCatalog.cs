namespace BotDsRpg.GameData;

// MinHp/MaxHp y MinDamage/MaxDamage se sortean una vez al iniciar el combate (ver
// Services/AdventureCombatStarter.cs) y quedan fijos para toda la pelea.
// DropItemNames: nombres exactos (tabla items) de lo que este monstruo puede soltar al ganar
// /hunt — nunca madera/piedra, eso es exclusivo de /chop y /mine (ver
// Database/seed_class_gear_and_monster_drops.sql y Modules/AdventureModule.ResolveDroppedItemAsync).
// Vacío para los monstruos de /travel, que todavía usan el drop por rareza sorteada (type
// 'Material', cualquiera del catálogo) en vez de una lista fija por monstruo.
// GoldBonus/XpBonus: bonus fijo que este monstruo suma a la recompensa base de /hunt (ver
// GameData/CombatRewardCalculator.RollHuntReward) — 0 por defecto para que /travel (que sigue sin
// pasar por acá) no se vea afectado.
public sealed record MonsterTemplate(
    string Name, string Emoji, int MinHp, int MaxHp, int MinDamage, int MaxDamage, IReadOnlyList<string> DropItemNames,
    int GoldBonus = 0, int XpBonus = 0);

public static class MonsterCatalog
{
    // Los monstruos de /hunt viven en la base (tablas monsters/monster_drops, ver
    // Repositories/IMonsterRepository.cs) desde que se armó el Sistema de Zonas: el pool ya no es
    // fijo, depende de la zona actual del jugador (users.current_zone_id). Acá solo queda el pool
    // de /travel, que sigue siendo fijo en código a propósito (zonas no lo tocan).
    public static readonly IReadOnlyList<MonsterTemplate> TravelMonsters =
    [
        new("Orco guerrero", "👹", 50, 90, 12, 24, []),
        new("Ogro de las cavernas", "🧌", 50, 90, 12, 24, []),
        new("Bandido élite", "🏴", 50, 90, 12, 24, []),
        new("Araña gigante", "🕷️", 50, 90, 12, 24, []),
        new("Espectro errante", "👻", 50, 90, 12, 24, []),
    ];

    public static MonsterTemplate RollFrom(IReadOnlyList<MonsterTemplate> pool) =>
        pool[Random.Shared.Next(pool.Count)];
}
