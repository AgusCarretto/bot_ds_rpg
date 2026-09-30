namespace BotDsRpg.GameData;

// MinHp/MaxHp y MinDamage/MaxDamage se sortean una vez al iniciar el combate (ver
// Services/AdventureCombatStarter.cs) y quedan fijos para toda la pelea.
// DropItemNames: nombres exactos (tabla items) de lo que este monstruo puede soltar al ganar —
// nunca madera/piedra, eso es exclusivo de /chop y /mine (ver
// Database/seed_class_gear_and_monster_drops.sql y Modules/AdventureModule.ResolveDroppedItemAsync).
// GoldBonus/XpBonus: bonus fijo que este monstruo suma a la recompensa base de /hunt (ver
// GameData/CombatRewardCalculator.RollHuntReward); /travel lo multiplica junto con el resto.
public sealed record MonsterTemplate(
    string Name, string Emoji, int MinHp, int MaxHp, int MinDamage, int MaxDamage, IReadOnlyList<string> DropItemNames,
    int GoldBonus = 0, int XpBonus = 0);

// Los tres tipos de monstruo de una zona, que se excluyen entre sí (monsters.is_boss / is_travel, ver
// Database/schema.sql): el pool de /hunt, el jefe de /boss (y /raid) y el monstruo dedicado de /travel.
public enum MonsterKind { Hunt, Boss, Travel }

// Un monstruo con la zona a la que pertenece y de qué tipo es (lo usa /drops, que lista todo junto).
public sealed record ZoneMonster(int ZoneId, MonsterKind Kind, MonsterTemplate Monster);

public static class MonsterCatalog
{
    // Los monstruos viven en la base (tablas monsters/monster_drops, ver Repositories/IMonsterRepository.cs),
    // todos según la zona actual del jugador (users.current_zone_id): el pool de /hunt, el jefe de /boss
    // y el monstruo dedicado de /travel. Acá no queda ningún monstruo fijo en código.
    public static MonsterTemplate RollFrom(IReadOnlyList<MonsterTemplate> pool) =>
        pool[Random.Shared.Next(pool.Count)];
}
