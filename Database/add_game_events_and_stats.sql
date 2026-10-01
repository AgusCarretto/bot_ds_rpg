-- =========================================================
-- Migración para bases YA EXISTENTES (schema.sql ya la incluye para instalaciones nuevas): el registro de eventos de
-- juego y los contadores por jugador.
--
--   game_events  una fila por cosa que pasa (una cacería ganada, un /give, una caja abierta...): quién, qué, en qué zona,
--                cuánto y cuándo. Sirve para MEDIR cómo se juega (qué comandos se usan, cuánto tarda alguien en llegar a
--                nivel 5, cuánta gente juega por día; ver Database/report_game_events.sql). Sin FK a users a propósito: el
--                registro tiene que sobrevivir aunque se borre una cuenta de prueba.
--   player_stats un contador por jugador y por tipo de evento (cuántas cacerías ganó, cuánto oro dio...). Es lo que leen
--                las misiones y los logros, así que no hay que recontar la tabla de eventos cada vez.
--
-- Ejecutar UNA SOLA VEZ (es re-ejecutable igual: todo es IF NOT EXISTS). Solo agrega tablas: el bot viejo las ignora.
-- =========================================================

CREATE TABLE IF NOT EXISTS game_events (
    event_id    BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    discord_id  BIGINT NOT NULL,
    kind        TEXT NOT NULL,                  -- ver GameData/GameEventKinds.cs
    zone_id     INTEGER,                        -- zona del jugador al momento (NULL si no aplica)
    amount      BIGINT NOT NULL DEFAULT 1,      -- cuánto: 1 para "pasó una vez", o el oro / las unidades involucradas
    detail      TEXT                            -- dato libre (nombre del ítem, tier de la caja, nivel nuevo...)
);

CREATE INDEX IF NOT EXISTS idx_game_events_kind_time ON game_events (kind, occurred_at);
CREATE INDEX IF NOT EXISTS idx_game_events_player_time ON game_events (discord_id, occurred_at);

CREATE TABLE IF NOT EXISTS player_stats (
    discord_id BIGINT NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    stat_key   TEXT   NOT NULL,                 -- mismo vocabulario que game_events.kind
    value      BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (discord_id, stat_key)
);
