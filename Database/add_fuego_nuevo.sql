-- =========================================================
-- v0.12.0: Fuego Nuevo (el reinicio), las bendiciones y su historial. Migración para una base que YA existe (una base nueva ya trae todo esto en schema.sql: NO hace falta
-- correr esto contra una instalación limpia). Re-ejecutable. IMPORTANTE para una base viva: correrlo ANTES de arrancar el bot v0.12.0 (el código lee users.fuego_nuevo y
-- users.run_started_at en CADA consulta de jugador, y las tablas nuevas en cada pelea). Hacé un backup antes (DEPLOY.md, sección 6). Ejecutar con psql:
--     psql -d asado-y-acero -v ON_ERROR_STOP=1 -f add_fuego_nuevo.sql
--
--   1. users.fuego_nuevo (cuántos Fuegos Nuevos hizo; 0 = ninguno) y users.run_started_at (cuándo arrancó la vuelta actual);
--   2. fuego_nuevo_history: una fila por Fuego Nuevo (clase antes y después, nivel, cuánto tardó la vuelta);
--   3. player_blessings: las bendiciones del jugador con su nivel (I a V);
--   4. blessing_offers: la oferta de 3 bendiciones de cada Fuego Nuevo (queda guardada hasta que elige).
-- =========================================================

ALTER TABLE users ADD COLUMN IF NOT EXISTS fuego_nuevo INTEGER NOT NULL DEFAULT 0 CHECK (fuego_nuevo >= 0);
ALTER TABLE users ADD COLUMN IF NOT EXISTS run_started_at TIMESTAMPTZ NOT NULL DEFAULT now();

CREATE TABLE IF NOT EXISTS fuego_nuevo_history (
    discord_id    BIGINT NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    number        INTEGER NOT NULL CHECK (number >= 1),
    class_before  TEXT NOT NULL,
    class_after   TEXT NOT NULL,
    level_before  INTEGER NOT NULL CHECK (level_before >= 1),
    started_at    TIMESTAMPTZ NOT NULL,
    finished_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (discord_id, number)
);

CREATE TABLE IF NOT EXISTS player_blessings (
    discord_id     BIGINT NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    blessing_key   TEXT NOT NULL,
    level          INTEGER NOT NULL CHECK (level BETWEEN 1 AND 5),
    PRIMARY KEY (discord_id, blessing_key)
);

CREATE TABLE IF NOT EXISTS blessing_offers (
    discord_id      BIGINT NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    fuego_nuevo_no  INTEGER NOT NULL CHECK (fuego_nuevo_no >= 1),
    offered_keys    TEXT NOT NULL,        -- las 3 claves separadas por coma ("manada,filo_antiguo,aprendiz")
    chosen_key      TEXT,                 -- NULL = todavía no eligió
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (discord_id, fuego_nuevo_no)
);

DO $$
BEGIN
    RAISE NOTICE 'add_fuego_nuevo: listo. Jugadores: %, con Fuego Nuevo hecho: %', (SELECT count(*) FROM users), (SELECT count(*) FROM users WHERE fuego_nuevo > 0);
END $$;
