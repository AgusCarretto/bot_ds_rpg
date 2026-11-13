-- Navidad en el Fogón (v0.57.0, docs/navidad.md): los Regalos (la moneda del evento) y el calendario de adviento.
-- Los Regalos se guardan por TEMPORADA (season = 'navidad2026'): al terminar el evento el bot deja de leerlos, así que "desaparecen"
-- solos sin borrar nada; no los toca el Fuego Nuevo (no están en users ni en el inventario).
-- Opcional antes del binario: sin las tablas, las victorias se cobran igual (el Regalo no se suma y queda un aviso en el log) y /navidad contesta un error.
-- Re-ejecutable.
CREATE TABLE IF NOT EXISTS event_gifts (
    discord_id BIGINT NOT NULL,
    season     TEXT   NOT NULL,
    balance    BIGINT NOT NULL DEFAULT 0 CHECK (balance >= 0),
    earned     BIGINT NOT NULL DEFAULT 0 CHECK (earned >= 0),
    PRIMARY KEY (discord_id, season)
);

CREATE TABLE IF NOT EXISTS event_advent_claims (
    discord_id BIGINT      NOT NULL,
    season     TEXT        NOT NULL,
    day        SMALLINT    NOT NULL CHECK (day BETWEEN 1 AND 24),
    claimed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (discord_id, season, day)
);

-- La tienda (v0.57.0): las Campanas compradas (se usan con «aa campana» para reiniciar /hunt) y hasta cuándo dura la Vela (+50 % de Regalos).
ALTER TABLE event_gifts ADD COLUMN IF NOT EXISTS bells        INTEGER     NOT NULL DEFAULT 0 CHECK (bells >= 0);
ALTER TABLE event_gifts ADD COLUMN IF NOT EXISTS candle_until TIMESTAMPTZ;

-- El Muñeco de Nieve Gigante (v0.57.0): UNO para todos los servidores. Cada vuelta es una fila (round); al morir se cierra (killed_at) y, si
-- todavía caen Regalos, se abre la siguiente con ×1,5 de vida. event_snowman_hits = cuánto le pegó cada jugador en cada vuelta (el ranking y los premios).
CREATE TABLE IF NOT EXISTS event_snowman (
    season     TEXT        NOT NULL,
    round      INTEGER     NOT NULL CHECK (round >= 1),
    max_hp     INTEGER     NOT NULL CHECK (max_hp > 0),
    hp         INTEGER     NOT NULL CHECK (hp >= 0),
    started_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    killed_at  TIMESTAMPTZ,
    killer_id  BIGINT,
    PRIMARY KEY (season, round)
);

CREATE TABLE IF NOT EXISTS event_snowman_hits (
    season     TEXT    NOT NULL,
    round      INTEGER NOT NULL,
    discord_id BIGINT  NOT NULL,
    damage     BIGINT  NOT NULL DEFAULT 0 CHECK (damage >= 0),
    spent      BIGINT  NOT NULL DEFAULT 0 CHECK (spent >= 0),
    PRIMARY KEY (season, round, discord_id)
);
