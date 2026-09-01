-- =========================================================
-- Migración: agrega HP a la tabla users.
-- Ejecutar UNA SOLA VEZ contra la base ya creada (schema.sql ya la tiene
-- si es una instalación nueva).
-- =========================================================

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS max_hp     INTEGER NOT NULL DEFAULT 100 CHECK (max_hp > 0),
    ADD COLUMN IF NOT EXISTS current_hp INTEGER NOT NULL DEFAULT 100 CHECK (current_hp >= 0);

ALTER TABLE users
    ADD CONSTRAINT chk_current_hp_within_max CHECK (current_hp <= max_hp);
