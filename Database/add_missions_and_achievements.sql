-- =========================================================
-- Misiones, logros y colección de trofeos. Migración para una base que YA corre (una instalación nueva ya lo trae en schema.sql).
-- Re-ejecutable: todo es CREATE ... IF NOT EXISTS y el relleno de abajo pone valores absolutos, no suma.
--
-- QUÉ GUARDA (y qué NO):
--   · mission_claims:     qué misiones ya cobró cada jugador en cada día/semana. El PROGRESO no se guarda: es la suma de lo registrado en
--                         game_events desde el inicio del período (el registro de eventos ES el progreso), y qué misiones tocan es una
--                         función pura del día (GameData/MissionCatalog.cs). Por eso no hay "asignación" de misiones que se pueda desfasar.
--   · achievement_claims: qué tramos de logro ya cobró (el logro en sí es "el contador de player_stats llegó a tal número").
--   · player_collection:  qué trofeos distintos consiguió alguna vez (un trofeo = material que ningún monstruo suelta, o sea los que
--                         solo salen de las cajas). Alimenta el contador 'trophy_found' del logro Coleccionista.
-- =========================================================

BEGIN;

CREATE TABLE IF NOT EXISTS mission_claims (
    discord_id   BIGINT      NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    period       TEXT        NOT NULL CHECK (period IN ('daily', 'weekly')),
    period_start TIMESTAMPTZ NOT NULL,            -- el instante UTC en que empezó el día/semana de Uruguay (GameData/UruguayCalendar.cs)
    mission_key  TEXT        NOT NULL,            -- la clave de la misión, o '_bonus' (el premio por completar todas las del período)
    claimed_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (discord_id, period, period_start, mission_key)
);

CREATE TABLE IF NOT EXISTS achievement_claims (
    discord_id      BIGINT      NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    achievement_key TEXT        NOT NULL,
    tier            INTEGER     NOT NULL CHECK (tier >= 1),
    claimed_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (discord_id, achievement_key, tier)
);

CREATE TABLE IF NOT EXISTS player_collection (
    discord_id        BIGINT      NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    item_id           INTEGER     NOT NULL REFERENCES items (item_id) ON DELETE CASCADE,
    first_obtained_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (discord_id, item_id)
);

-- Relleno para quien ya abrió cajas antes de que existiera la colección: lo que tiene HOY en la mochila cuenta (lo que vendió no
-- se puede saber). Un trofeo = material sin ningún monstruo que lo suelte.
INSERT INTO player_collection (discord_id, item_id)
SELECT inv.discord_id, inv.item_id
FROM inventory inv
JOIN items i ON i.item_id = inv.item_id
WHERE inv.quantity > 0
  AND i.type = 'Material'
  AND NOT EXISTS (SELECT 1 FROM monster_drops d WHERE d.item_id = i.item_id)
ON CONFLICT DO NOTHING;

INSERT INTO player_stats (discord_id, stat_key, value)
SELECT discord_id, 'trophy_found', COUNT(*)
FROM player_collection
GROUP BY discord_id
ON CONFLICT (discord_id, stat_key) DO UPDATE SET value = EXCLUDED.value;

COMMIT;
