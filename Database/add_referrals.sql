-- =========================================================
-- v0.43.0: INVITAR AMIGOS (`aa invitedby @amigo` y `/invitedby`). El dueño (2026-10-31): una vez por cuenta, 10k para el nuevo, 20k para el que lo invitó y 80k más cuando el nuevo llega a nivel 5.
--
--   psql -d asado-y-acero -v ON_ERROR_STOP=1 -f add_referrals.sql
--
-- Una fila por jugador invitado (invitee_id es la clave: de ahí sale «una vez por cuenta»). level_paid_at queda NULL hasta que se paga el premio del nivel 5 (la guarda de ese pago).
-- Es RE-EJECUTABLE y OPCIONAL antes del binario: sin la tabla solo falla `aa invitedby` (dice «no pude») y el pago del nivel 5 se registra como aviso en el log; nada más la toca.
-- Una base nueva ya la trae en schema.sql.
-- =========================================================

CREATE TABLE IF NOT EXISTS referrals (
    invitee_id    BIGINT      PRIMARY KEY REFERENCES users (discord_id) ON DELETE CASCADE,
    inviter_id    BIGINT      NOT NULL,                       -- sin FK a propósito: si el que invitó borra la cuenta, la fila queda (el invitado no puede anotar a otro)
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    level_paid_at TIMESTAMPTZ,                                -- cuándo se pagó el premio del nivel 5 al que invitó (NULL = todavía no)
    CHECK (invitee_id <> inviter_id)
);

CREATE INDEX IF NOT EXISTS idx_referrals_inviter ON referrals (inviter_id);
