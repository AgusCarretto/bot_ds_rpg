-- =========================================================
-- Migración para bases YA EXISTENTES (schema.sql ya la incluye para instalaciones nuevas): los buffs temporales (por ahora el
-- de ataque que dan los banquetes de comida Mítica).
--
--   item_buffs    qué buff da un ítem al usarlo: attack_percent (+% de ataque) durante "minutes" minutos. Solo tiene filas
--                 la comida con buff (los banquetes, ver Database/rework_food_catalog.sql); el resto de la comida solo cura.
--   player_buffs  el buff ACTIVO de cada jugador: uno por tipo (buff_key = 'attack'), con su vencimiento. Usar otro banquete
--                 lo REEMPLAZA (no se acumulan), así no hay forma de apilar ataque sin límite.
--
-- Solo agrega tablas: el bot viejo las ignora. Ejecutar UNA vez, antes de rework_food_catalog.sql. Re-ejecutable igual.
-- =========================================================

CREATE TABLE IF NOT EXISTS item_buffs (
    item_id        INTEGER PRIMARY KEY REFERENCES items (item_id) ON DELETE CASCADE,
    attack_percent INTEGER NOT NULL CHECK (attack_percent BETWEEN 1 AND 100),
    minutes        INTEGER NOT NULL CHECK (minutes BETWEEN 1 AND 1440)
);

CREATE TABLE IF NOT EXISTS player_buffs (
    discord_id BIGINT NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    buff_key   TEXT   NOT NULL,                  -- hoy solo 'attack'
    percent    INTEGER NOT NULL CHECK (percent BETWEEN 1 AND 100),
    expires_at TIMESTAMPTZ NOT NULL,
    source     TEXT,                             -- nombre del ítem que lo dio (para mostrarlo en /profile)
    PRIMARY KEY (discord_id, buff_key)
);
