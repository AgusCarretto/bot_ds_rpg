-- =========================================================
-- Migración para bases YA EXISTENTES (schema.sql ya la incluye para instalaciones nuevas):
-- agrega el sistema de Zonas — tablas zones/monsters/monster_drops + users.current_zone_id.
--
-- Ejecutar UNA SOLA VEZ, ANTES de Database/seed_zones_and_monsters.sql (que carga las 5 zonas y
-- los monstruos — hace falta que zone_id 1 exista antes de que corra cualquier /start nuevo,
-- porque users.current_zone_id tiene DEFAULT 1 con FK a zones).
-- =========================================================

BEGIN;

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
    gold_reward INTEGER NOT NULL DEFAULT 0 CHECK (gold_reward >= 0),
    xp_reward   INTEGER NOT NULL DEFAULT 0 CHECK (xp_reward >= 0)
);

CREATE TABLE IF NOT EXISTS monster_drops (
    monster_id INTEGER NOT NULL REFERENCES monsters (monster_id) ON DELETE CASCADE,
    item_id    INTEGER NOT NULL REFERENCES items (item_id) ON DELETE CASCADE,
    PRIMARY KEY (monster_id, item_id)
);

CREATE INDEX IF NOT EXISTS idx_monsters_zone_id ON monsters (zone_id);
CREATE INDEX IF NOT EXISTS idx_monster_drops_monster_id ON monster_drops (monster_id);

-- Placeholder momentáneo: la zona 1 recién queda con nombre real cuando corra el seed de abajo.
-- Sin esto, agregar la columna con DEFAULT 1 fallaría en una base que ya tiene jugadores (violaría
-- la FK antes de que exista ninguna fila en zones).
INSERT INTO zones (zone_id, name, description, min_level, emoji)
VALUES (1, 'Praderas del Mate', 'Cargando...', 1, '🌾')
ON CONFLICT (zone_id) DO NOTHING;

ALTER TABLE users ADD COLUMN IF NOT EXISTS current_zone_id INTEGER NOT NULL DEFAULT 1 REFERENCES zones (zone_id);

COMMIT;
