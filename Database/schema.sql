-- =========================================================
-- Asado y Acero RPG — Esquema inicial de base de datos
-- PostgreSQL
-- =========================================================
-- Orden de creación: items -> users -> inventory -> cooldowns
-- (users referencia items vía weapon_id/amulet_id, por eso items va primero)

BEGIN;

-- unaccent(): permite buscar ítems por nombre sin distinguir tildes (ej. "Jabali" encuentra
-- "Jabalí"), usado en ItemRepository.GetByNameAsync.
CREATE EXTENSION IF NOT EXISTS unaccent;

-- ---------------------------------------------------------
-- items: catálogo de armas, materiales y objetos del juego
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS items (
    item_id     INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    -- UNIQUE a propósito: ItemRepository.GetByNameAsync resuelve con LIMIT 1 sin ORDER BY, así que
    -- un nombre duplicado hace que /equip, /shop buy y /forge make resuelvan a cualquiera de las
    -- copias de forma inconsistente entre ejecuciones. También hace que el "ON CONFLICT DO NOTHING"
    -- que ya usan todos los seeds empiece a funcionar de verdad si se re-corren por error.
    name        TEXT NOT NULL UNIQUE,
    type        TEXT NOT NULL,                 -- ej: 'Espada', 'Daga', 'Arco', 'Grimorio', 'Madera', 'Mineral'
    rarity      TEXT NOT NULL DEFAULT 'Común'
                    CHECK (rarity IN ('Común', 'Raro', 'Épico', 'Legendario', 'Mítico')),
    stat_value  INTEGER NOT NULL DEFAULT 0 CHECK (stat_value >= 0),
    sell_price  INTEGER NOT NULL DEFAULT 0 CHECK (sell_price >= 0), -- lo que paga la tienda al vender
    buy_price   INTEGER NOT NULL DEFAULT 0 CHECK (buy_price >= sell_price), -- lo que cobra la tienda al comprar
    -- Familia del arma (solo aplica cuando type = 'Weapon'): define la sinergia de clase en combate,
    -- ver GameData/ClassCatalog.cs (WeaponType) y GameData/ClassWeaponSynergy.cs.
    weapon_family TEXT CHECK (weapon_family IN ('Espadas', 'Dagas', 'Arcos', 'Grimorios')),
    -- Clase exclusiva para equipar/forjar este ítem (ver Modules/EquipModule.cs y
    -- Modules/ForgeModule.cs). NULL = disponible para cualquier clase.
    class_requirement TEXT CHECK (class_requirement IN ('Guerrero', 'Ninja', 'Arquero', 'Hechicero')),
    -- Emoji personalizado de Discord ("<:nombre:id>"), NULL = todavía sin pixel art cargado
    -- (GameData/ItemDisplay.cs cae a mostrar solo el nombre en ese caso).
    emoji TEXT
);

-- ---------------------------------------------------------
-- recipes / recipe_ingredients: recetas del herrero (ver Modules/ForgeModule.cs). Un ítem
-- resultado tiene como máximo una receta (UNIQUE); una receta puede tener varios ingredientes.
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS recipes (
    recipe_id       INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    result_item_id  INTEGER NOT NULL UNIQUE REFERENCES items (item_id) ON DELETE CASCADE,
    gold_cost       INTEGER NOT NULL DEFAULT 0 CHECK (gold_cost >= 0)
);

CREATE TABLE IF NOT EXISTS recipe_ingredients (
    recipe_id  INTEGER NOT NULL REFERENCES recipes (recipe_id) ON DELETE CASCADE,
    item_id    INTEGER NOT NULL REFERENCES items (item_id) ON DELETE CASCADE,
    quantity   INTEGER NOT NULL CHECK (quantity > 0),
    PRIMARY KEY (recipe_id, item_id)
);

CREATE INDEX IF NOT EXISTS idx_recipe_ingredients_recipe_id ON recipe_ingredients (recipe_id);

-- ---------------------------------------------------------
-- zones / monsters / monster_drops: mundo dividido en zonas de dificultad creciente (ver
-- Modules/ZoneModule.cs y Database/seed_zones_and_monsters.sql). /hunt solo caza monstruos de la
-- zona ACTUAL del jugador (users.current_zone_id) — /travel sigue con su pool fijo en código
-- (GameData/MonsterCatalog.TravelMonsters), sin relación con zonas.
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS zones (
    zone_id     INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name        TEXT NOT NULL UNIQUE,
    description TEXT NOT NULL DEFAULT '',
    min_level   INTEGER NOT NULL DEFAULT 1 CHECK (min_level >= 1),
    emoji       TEXT
);

CREATE TABLE IF NOT EXISTS monsters (
    monster_id  INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    zone_id     INTEGER NOT NULL REFERENCES zones (zone_id) ON DELETE CASCADE,
    name        TEXT NOT NULL UNIQUE,
    emoji       TEXT,
    min_hp      INTEGER NOT NULL CHECK (min_hp > 0),
    max_hp      INTEGER NOT NULL CHECK (max_hp >= min_hp),
    min_damage  INTEGER NOT NULL CHECK (min_damage >= 0),
    max_damage  INTEGER NOT NULL CHECK (max_damage >= min_damage),
    -- Bonus FIJO que este monstruo suma a la recompensa base de /hunt (ver
    -- GameData/CombatRewardCalculator.RollHuntReward) — no reemplaza la fórmula existente, la
    -- complementa, así que un monstruo de Zona 1 con 0/0 no cambia nada respecto a lo que ya había.
    gold_reward INTEGER NOT NULL DEFAULT 0 CHECK (gold_reward >= 0),
    xp_reward   INTEGER NOT NULL DEFAULT 0 CHECK (xp_reward >= 0),
    -- Jefe de zona (ver Modules/AdventureModule.cs, comando /boss): a lo sumo uno por zona,
    -- EXCLUIDO del pool aleatorio de /hunt (Repositories/MonsterRepository.GetMonstersByZoneAsync)
    -- — solo se enfrenta a propósito con /boss. Derrotarlo sube users.highest_zone_cleared.
    is_boss     BOOLEAN NOT NULL DEFAULT false
);

CREATE TABLE IF NOT EXISTS monster_drops (
    monster_id INTEGER NOT NULL REFERENCES monsters (monster_id) ON DELETE CASCADE,
    item_id    INTEGER NOT NULL REFERENCES items (item_id) ON DELETE CASCADE,
    PRIMARY KEY (monster_id, item_id)
);

CREATE INDEX IF NOT EXISTS idx_monsters_zone_id ON monsters (zone_id);
CREATE INDEX IF NOT EXISTS idx_monster_drops_monster_id ON monster_drops (monster_id);

-- ---------------------------------------------------------
-- users: perfil de cada jugador, una fila por discord_id
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS users (
    discord_id        BIGINT PRIMARY KEY,       -- snowflake ID de Discord
    class             TEXT NOT NULL DEFAULT 'Guerrero'
                        CHECK (class IN ('Guerrero', 'Ninja', 'Arquero', 'Hechicero')),
    level             INTEGER NOT NULL DEFAULT 1 CHECK (level >= 1),
    xp                INTEGER NOT NULL DEFAULT 0 CHECK (xp >= 0),
    gold              INTEGER NOT NULL DEFAULT 50 CHECK (gold >= 0),
    max_hp            INTEGER NOT NULL DEFAULT 100 CHECK (max_hp > 0),
    current_hp        INTEGER NOT NULL DEFAULT 100 CHECK (current_hp >= 0),
    weapon_id         INTEGER REFERENCES items (item_id) ON DELETE SET NULL,
    amulet_id         INTEGER REFERENCES items (item_id) ON DELETE SET NULL,
    daily_streak      INTEGER NOT NULL DEFAULT 0 CHECK (daily_streak >= 0),
    last_daily_claim  TIMESTAMPTZ, -- NULL = todavía no reclamó ningún /daily
    -- Zona donde caza /hunt (ver arriba). Default 1 = "Praderas del Mate": tiene que existir ANTES
    -- de que cualquier jugador corra /start (Database/seed_zones_and_monsters.sql debe correr
    -- antes de que haya jugadores nuevos, o el INSERT de un /start viola esta FK).
    current_zone_id   INTEGER NOT NULL DEFAULT 1 REFERENCES zones (zone_id),
    -- Rango (zone_id) de la zona MÁS DIFÍCIL cuyo jefe ya derrotó, 0 = ninguno todavía. Compara por
    -- posición en la lista de zonas ordenada por min_level, no por zone_id crudo (ver
    -- Modules/ZoneModule.ExecuteTravelAsync) — así no depende de que los zone_id sigan siendo
    -- consecutivos en orden de dificultad para siempre.
    highest_zone_cleared INTEGER NOT NULL DEFAULT 0 CHECK (highest_zone_cleared >= 0),
    CONSTRAINT chk_current_hp_within_max CHECK (current_hp <= max_hp)
);

-- ---------------------------------------------------------
-- inventory: ítems que posee cada jugador (relación N a N entre users e items)
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS inventory (
    discord_id  BIGINT  NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    item_id     INTEGER NOT NULL REFERENCES items (item_id) ON DELETE CASCADE,
    quantity    INTEGER NOT NULL DEFAULT 1 CHECK (quantity >= 0),
    PRIMARY KEY (discord_id, item_id)
);

-- ---------------------------------------------------------
-- cooldowns: última ejecución de cada comando farmeable por jugador
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS cooldowns (
    discord_id       BIGINT NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    command_name     TEXT   NOT NULL,           -- ej: 'hunt', 'travel', 'chop', 'mine'
    last_executed_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (discord_id, command_name)
);

-- Índices para las consultas más frecuentes (lookup de inventario/cooldowns por jugador)
CREATE INDEX IF NOT EXISTS idx_inventory_discord_id ON inventory (discord_id);
CREATE INDEX IF NOT EXISTS idx_cooldowns_discord_id ON cooldowns (discord_id);

COMMIT;
