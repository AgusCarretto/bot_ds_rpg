-- =========================================================
-- Migración: agrega el sistema de racha de /daily.
-- Ejecutar UNA SOLA VEZ contra la base ya creada (schema.sql ya la tiene
-- si es una instalación nueva).
-- =========================================================

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS daily_streak     INTEGER NOT NULL DEFAULT 0 CHECK (daily_streak >= 0),
    ADD COLUMN IF NOT EXISTS last_daily_claim TIMESTAMPTZ; -- NULL = todavía no reclamó ningún /daily
