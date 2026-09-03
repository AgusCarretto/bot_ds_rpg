-- =========================================================
-- Migración: equipamiento exclusivo por clase (Guerrero/Ninja/Arquero/Hechicero).
-- Ejecutar UNA SOLA VEZ contra la base ya creada (schema.sql ya la tiene si es una instalación nueva).
-- =========================================================
-- NULL = disponible para cualquier clase (todo lo existente hoy queda así, sin backfill).
-- Los valores deben coincidir con el CHECK de users.class (ver schema.sql).

ALTER TABLE items ADD COLUMN IF NOT EXISTS class_requirement TEXT
    CHECK (class_requirement IN ('Guerrero', 'Ninja', 'Arquero', 'Hechicero'));
