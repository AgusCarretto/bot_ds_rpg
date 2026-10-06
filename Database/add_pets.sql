-- =========================================================
-- v0.10.0: mascotas. Migración para una base que YA existe (una base nueva ya trae las tablas en schema.sql y el catálogo en seed_pets.sql: NO hace falta correr
-- esto contra una instalación limpia). Re-ejecutable. IMPORTANTE para una base viva: correrlo ANTES de arrancar el bot v0.10.0 (el código lee estas tablas al
-- vencer a un jefe y en cada pelea). Hacé un backup antes (DEPLOY.md, sección 6). Ejecutar con psql parado en esta carpeta (usa \ir):
--     psql -d asado-y-acero -v ON_ERROR_STOP=1 -f add_pets.sql
--
--   1. las dos tablas (pet_species y player_pets);
--   2. el catálogo (huevos, comida y las 5 especies): seed_pets.sql;
--   3. los huevos de quien YA venció jefes: uno por cada zona hasta la más alta que venció, para que los abra con /open (no se repite si ya los abrió o ya los tiene).
-- =========================================================

CREATE TABLE IF NOT EXISTS pet_species (
    species_id         INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    zone_id            INTEGER NOT NULL UNIQUE REFERENCES zones (zone_id) ON DELETE CASCADE,
    name               TEXT NOT NULL UNIQUE,
    emoji              TEXT,
    bonus_kind         TEXT NOT NULL CHECK (bonus_kind IN ('gold', 'xp', 'defense', 'drop')),
    max_bonus_percent  DOUBLE PRECISION NOT NULL CHECK (max_bonus_percent > 0),
    egg_item_id        INTEGER NOT NULL UNIQUE REFERENCES items (item_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS player_pets (
    discord_id   BIGINT NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    species_id   INTEGER NOT NULL REFERENCES pet_species (species_id) ON DELETE CASCADE,
    feed_points  INTEGER NOT NULL DEFAULT 0 CHECK (feed_points >= 0),
    last_fed_at  TIMESTAMPTZ,
    hatched_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (discord_id, species_id)
);

\ir seed_pets.sql

-- Los huevos retroactivos: quien venció al jefe de la Zona N ya "se ganó" las mascotas de las zonas hasta la N. Se las damos como huevo (el momento de abrirlo es suyo).
INSERT INTO inventory (discord_id, item_id, quantity)
SELECT u.discord_id, ps.egg_item_id, 1
FROM users u
JOIN zones cleared ON cleared.zone_id = u.highest_zone_cleared
JOIN pet_species ps ON TRUE
JOIN zones z ON z.zone_id = ps.zone_id AND z.min_level <= cleared.min_level
WHERE u.highest_zone_cleared > 0
  AND NOT EXISTS (SELECT 1 FROM player_pets pp WHERE pp.discord_id = u.discord_id AND pp.species_id = ps.species_id)
ON CONFLICT (discord_id, item_id) DO NOTHING;

DO $$
DECLARE
    v_eggs INTEGER;
BEGIN
    SELECT COALESCE(sum(inv.quantity), 0) INTO v_eggs FROM inventory inv JOIN items i USING (item_id) WHERE i.type = 'Huevo';
    RAISE NOTICE 'add_pets: listo. Huevos sin abrir en las mochilas: %', v_eggs;
END $$;
