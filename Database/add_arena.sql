-- =========================================================
-- La Arena: torneo PvP diario (v0.7.0). Para una base que ya estaba andando: agrega las 3 tablas.
-- schema.sql ya las trae, así que una instalación nueva NO necesita correr esto. Re-ejecutable (IF NOT EXISTS).
-- =========================================================

-- ---------------------------------------------------------
-- arena_days / arena_entries / arena_matches: la Arena, un torneo PvP por día (hora de Uruguay). Durante el día la gente se anota
-- (/arena join); a las 00:00 se arma la llave de eliminación directa, se pelea sola y se paga el premio al campeón (Services/ArenaService.cs).
-- Ver Database/add_arena.sql (migración) y GameData/ArenaRules.cs.
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS arena_days (
    day          DATE PRIMARY KEY,                    -- el día de Uruguay del torneo (se juega a la medianoche que lo cierra)
    status       TEXT NOT NULL DEFAULT 'open' CHECK (status IN ('open', 'resolved', 'cancelled')),
    channel_id   BIGINT,                              -- canal donde se anotó el primero: ahí se anuncia el resultado
    winner_id    BIGINT,
    winner_name  TEXT,
    participants INTEGER NOT NULL DEFAULT 0,
    rounds       INTEGER NOT NULL DEFAULT 0,
    reward_text  TEXT,                                -- lo que cobró el campeón, ya armado para mostrar
    resolved_at  TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS arena_entries (
    day          DATE        NOT NULL REFERENCES arena_days (day) ON DELETE CASCADE,
    discord_id   BIGINT      NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    display_name TEXT        NOT NULL,                -- el nombre al anotarse: la llave se muestra con él aunque la persona se vaya
    joined_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (day, discord_id)
);

-- Las peleas de la llave. Sin FK a users a propósito: es historia, sobrevive al borrado de una cuenta.
CREATE TABLE IF NOT EXISTS arena_matches (
    day       DATE    NOT NULL REFERENCES arena_days (day) ON DELETE CASCADE,
    round     INTEGER NOT NULL,                       -- 1 = primera ronda
    slot      INTEGER NOT NULL,                       -- posición dentro de la ronda
    p1_id     BIGINT  NOT NULL,
    p1_name   TEXT    NOT NULL,
    p2_id     BIGINT,                                 -- NULL = pasó directo (no tuvo rival)
    p2_name   TEXT,
    winner_id BIGINT  NOT NULL,
    actions   INTEGER NOT NULL DEFAULT 0,             -- cuántas acciones duró la pelea
    PRIMARY KEY (day, round, slot)
);

CREATE INDEX IF NOT EXISTS idx_arena_days_status ON arena_days (status, day);
