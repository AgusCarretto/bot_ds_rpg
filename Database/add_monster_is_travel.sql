-- =========================================================
-- Migración para bases YA EXISTENTES (schema.sql ya la incluye para instalaciones nuevas):
-- agrega monsters.is_travel — el monstruo DEDICADO de /travel de cada zona, separado del pool de /hunt
-- y del jefe (un monstruo no puede ser las dos cosas a la vez).
--
-- Ejecutar UNA SOLA VEZ, ANTES de Database/seed_travel_monsters.sql. Es re-ejecutable igual.
-- =========================================================

ALTER TABLE monsters ADD COLUMN IF NOT EXISTS is_travel BOOLEAN NOT NULL DEFAULT false;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'monsters_not_boss_and_travel') THEN
        ALTER TABLE monsters ADD CONSTRAINT monsters_not_boss_and_travel CHECK (NOT (is_boss AND is_travel));
    END IF;
END $$;
