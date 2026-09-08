-- =========================================================
-- Migración para bases YA EXISTENTES (schema.sql ya la incluye para instalaciones nuevas):
-- agrega el Sistema de Jefes de Zona — monsters.is_boss + users.highest_zone_cleared.
--
-- Ejecutar UNA SOLA VEZ, ANTES de Database/seed_zone_bosses.sql.
-- =========================================================

ALTER TABLE monsters ADD COLUMN IF NOT EXISTS is_boss BOOLEAN NOT NULL DEFAULT false;

ALTER TABLE users ADD COLUMN IF NOT EXISTS highest_zone_cleared INTEGER NOT NULL DEFAULT 0
    CHECK (highest_zone_cleared >= 0);
