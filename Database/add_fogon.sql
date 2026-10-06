-- =========================================================
-- v0.11.0: El Fogón Eterno (la "Zona 0"). Migración para una base que YA existe (una base nueva ya trae las columnas en schema.sql y el catálogo en seed_fogon.sql: NO hace
-- falta correr esto contra una instalación limpia). Re-ejecutable. IMPORTANTE para una base viva: correrlo ANTES de arrancar el bot v0.11.0 (el código lee zones.kind,
-- users.in_gate y users.gate_cleared en CADA consulta de jugador o de zona). Hacé un backup antes (DEPLOY.md, sección 6). Ejecutar con psql parado en esta carpeta (usa \ir):
--     psql -d asado-y-acero -v ON_ERROR_STOP=1 -f add_fogon.sql
--
--   1. las columnas nuevas: zones.kind ('normal' o 'gate'), users.in_gate (está en el Fogón) y users.gate_cleared (ya le ganó al Asador en esta vuelta);
--   2. el catálogo del Fogón (la zona 0, el equipo, las recetas y el jefe): seed_fogon.sql.
-- =========================================================

ALTER TABLE zones ADD COLUMN IF NOT EXISTS kind TEXT NOT NULL DEFAULT 'normal' CHECK (kind IN ('normal', 'gate'));
ALTER TABLE users ADD COLUMN IF NOT EXISTS in_gate BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE users ADD COLUMN IF NOT EXISTS gate_cleared BOOLEAN NOT NULL DEFAULT false;

\ir seed_fogon.sql

DO $$
BEGIN
    RAISE NOTICE 'add_fogon: listo. Zonas normales: %, puertas: %', (SELECT count(*) FROM zones WHERE kind = 'normal'), (SELECT count(*) FROM zones WHERE kind = 'gate');
END $$;
