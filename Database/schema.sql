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
    name        TEXT NOT NULL,
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
