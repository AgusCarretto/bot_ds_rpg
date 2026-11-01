-- =========================================================
-- v0.46.0: la cría SOLO del dragón tiene su propio contador de fallos seguidos. El dueño (2026-11-01): «cuando alguien lo hace solo cuenta un intento fallido y cuando lo
-- hace con alguien también, pero si hizo 5 solo le cuentan para esa cuenta… habría que tener las cuentas separadas». Hasta ahora los fallos solos sumaban a la piedad
-- de la cría en pareja (y al revés). Desde esta versión `fail_streak` es solo el de la pareja y `solo_fail_streak` el de criar solo; los dos vuelven a 0 al subir de tier.
--
--   psql -d asado-y-acero -v ON_ERROR_STOP=1 -f add_dragon_solo_streak.sql
--
-- Correrla ANTES del binario v0.46.0 (todas las consultas del dragón leen la columna). Re-ejecutable. Los fallos que ya había quedan en el contador de la pareja
-- (no se pueden separar hacia atrás); el de criar solo arranca en 0.
-- =========================================================

ALTER TABLE player_dragons ADD COLUMN IF NOT EXISTS solo_fail_streak INTEGER NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'player_dragons_solo_fail_streak_check') THEN
        ALTER TABLE player_dragons ADD CONSTRAINT player_dragons_solo_fail_streak_check CHECK (solo_fail_streak >= 0);
    END IF;
END $$;
