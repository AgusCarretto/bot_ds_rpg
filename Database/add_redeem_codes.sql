-- =========================================================
-- v0.37.0: los CÓDIGOS CANJEABLES (`aa code <CÓDIGO>` y `/code`). El dueño (2026-10-28): «vamos a ir trabajando en code, `aa code`: un comando nuevo que tenga códigos que se puedan canjear por cosas».
--
--   psql -d asado-y-acero -v ON_ERROR_STOP=1 -f add_redeem_codes.sql
--
-- Tres tablas: el código con sus reglas (redeem_codes), lo que da (redeem_code_rewards) y quién ya lo canjeó (redeem_code_claims, una fila por código y jugador: de ahí sale «uno por persona»).
-- Es RE-EJECUTABLE y OPCIONAL antes del binario: sin las tablas solo fallan `aa code` y `aa admincode` (dicen «no pude canjear»); el resto del bot no las toca. Una base nueva ya las trae en schema.sql.
-- =========================================================

CREATE TABLE IF NOT EXISTS redeem_codes (
    code_id     SERIAL      PRIMARY KEY,
    code        TEXT        NOT NULL UNIQUE,                                          -- normalizado: MAYÚSCULAS, sin acentos ni espacios (A-Z 0-9 _ -)
    max_uses    INTEGER     CHECK (max_uses IS NULL OR max_uses > 0),                 -- cuántas personas lo pueden canjear en total; NULL = sin tope
    uses        INTEGER     NOT NULL DEFAULT 0 CHECK (uses >= 0),
    expires_at  TIMESTAMPTZ,                                                          -- NULL = no vence
    active      BOOLEAN     NOT NULL DEFAULT TRUE,                                    -- apagarlo (aa admincode off) lo frena sin borrar nada
    note        TEXT,
    created_by  BIGINT      NOT NULL,                                                 -- el ID de Discord del administrador que lo creó
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS redeem_code_rewards (
    reward_id   SERIAL      PRIMARY KEY,
    code_id     INTEGER     NOT NULL REFERENCES redeem_codes (code_id) ON DELETE CASCADE,
    kind        TEXT        NOT NULL CHECK (kind IN ('gold', 'dust', 'xp', 'item')),
    item_id     INTEGER     REFERENCES items (item_id) ON DELETE CASCADE,            -- solo para kind = 'item' (si el ítem se borra del catálogo, el premio desaparece con él)
    amount      INTEGER     NOT NULL CHECK (amount > 0),
    CONSTRAINT redeem_reward_item_ck CHECK ((kind = 'item') = (item_id IS NOT NULL))
);

CREATE TABLE IF NOT EXISTS redeem_code_claims (
    code_id     INTEGER     NOT NULL REFERENCES redeem_codes (code_id) ON DELETE CASCADE,
    discord_id  BIGINT      NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    claimed_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (code_id, discord_id)
);

CREATE INDEX IF NOT EXISTS idx_redeem_code_rewards_code ON redeem_code_rewards (code_id);
