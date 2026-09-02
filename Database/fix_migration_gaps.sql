-- =========================================================
-- Corrige lo que quedó a mitad de camino al correr las migraciones anteriores.
-- Ejecutar UNA SOLA VEZ. Verificado contra la base real antes de escribir esto:
-- el único usuario existente tiene daily_streak=0, last_daily_claim=NULL,
-- weapon_id=NULL y current_weapon_id=NULL, así que no hay riesgo de pérdida de datos.
-- =========================================================

-- 1. current_weapon_id seguía en la tabla (cleanup_weapon_columns.sql no llegó a esta línea,
--    o se cortó antes) — ningún código la usa desde que armamos /equip con weapon_id.
--    DROP COLUMN también borra su propia FK automáticamente.
ALTER TABLE users DROP COLUMN IF EXISTS current_weapon_id;

-- 2. daily_streak quedó nullable y sin el CHECK >= 0 (se agregó sin NOT NULL en algún momento).
UPDATE users SET daily_streak = 0 WHERE daily_streak IS NULL;
ALTER TABLE users ALTER COLUMN daily_streak SET NOT NULL;
ALTER TABLE users ALTER COLUMN daily_streak SET DEFAULT 0;
ALTER TABLE users ADD CONSTRAINT chk_daily_streak_nonnegative CHECK (daily_streak >= 0);

-- 3. last_daily_claim quedó como TIMESTAMP (sin zona horaria) en vez de TIMESTAMPTZ. El código
--    siempre escribe DateTime.UtcNow: si esta columna sigue "sin zona", Npgsql puede interpretar
--    mal las horas guardadas y el cálculo de 24h/48h de /daily queda inconsistente.
ALTER TABLE users ALTER COLUMN last_daily_claim TYPE TIMESTAMPTZ USING last_daily_claim AT TIME ZONE 'UTC';
