-- =========================================================
-- v0.14.0: cofres parejos. Migración para una base que YA existe (una base nueva ya trae la tabla en schema.sql y el catálogo en seed_zone_boxes.sql: NO hace falta correr esto
-- contra una instalación limpia). Re-ejecutable. IMPORTANTE para una base viva: correrlo ANTES de arrancar el bot v0.14.0 (el código lee zone_boxes en cada premio de misión,
-- logro o jefe). Hacé un backup antes (DEPLOY.md, sección 6). Ejecutar con psql parado en esta carpeta (usa \ir):
--     psql -d asado-y-acero -v ON_ERROR_STOP=1 -f add_zone_boxes.sql
--
--   1. la tabla zone_boxes (qué caja da cada zona en cada rol);
--   2. 'gather' como bonus válido de las mascotas (la 6.ª, la de la Zona 0);
--   3. el catálogo nuevo: las dos cajas, el cofre de los jefes de la 5 y de la 0, el premio del Asador, las cajas de cada zona y la mascota: seed_zone_boxes.sql.
-- =========================================================

CREATE TABLE IF NOT EXISTS zone_boxes (
    zone_id        INTEGER NOT NULL REFERENCES zones (zone_id) ON DELETE CASCADE,
    role           TEXT NOT NULL CHECK (role IN ('repeat', 'daily', 'weekly', 'prize', 'prize_top')),
    box_item_id    INTEGER NOT NULL REFERENCES boxes (box_item_id) ON DELETE CASCADE,
    chance_percent INTEGER NOT NULL DEFAULT 100 CHECK (chance_percent BETWEEN 1 AND 100),
    PRIMARY KEY (zone_id, role)
);

ALTER TABLE pet_species DROP CONSTRAINT IF EXISTS pet_species_bonus_kind_check;
ALTER TABLE pet_species ADD CONSTRAINT pet_species_bonus_kind_check CHECK (bonus_kind IN ('gold', 'xp', 'defense', 'drop', 'gather'));

\ir seed_zone_boxes.sql

DO $$
BEGIN
    RAISE NOTICE 'add_zone_boxes: listo. Cajas por zona: %, mascotas: %', (SELECT count(*) FROM zone_boxes), (SELECT count(*) FROM pet_species);
END $$;
