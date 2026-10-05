-- =========================================================
-- v0.9.0: banco, Polvo y encantamientos. Migración para una base que YA existe (una base nueva ya trae estas columnas en schema.sql: NO hace falta
-- correr esto contra una instalación limpia). Re-ejecutable (ADD COLUMN IF NOT EXISTS). IMPORTANTE para una base viva: correrlo ANTES de arrancar el
-- bot v0.9.0 (el código lee las columnas nuevas de users en cada consulta).
--
--   has_bank / bank_gold   la cuenta del banco (se compra por 1.000) y el oro guardado: la penalidad por muerte no lo toca.
--   dust                   Polvo: sale de desmantelar materiales (/dismantle) y se gasta en encantar (/enchant).
--   weapon_enchant /
--   amulet_enchant         tier (0 = ninguno, 1..5) del encantamiento de la pieza puesta; vender la pieza lo borra.
-- =========================================================

ALTER TABLE users ADD COLUMN IF NOT EXISTS has_bank       BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE users ADD COLUMN IF NOT EXISTS bank_gold      INTEGER NOT NULL DEFAULT 0 CHECK (bank_gold >= 0);
ALTER TABLE users ADD COLUMN IF NOT EXISTS dust           INTEGER NOT NULL DEFAULT 0 CHECK (dust >= 0);
ALTER TABLE users ADD COLUMN IF NOT EXISTS weapon_enchant INTEGER NOT NULL DEFAULT 0 CHECK (weapon_enchant BETWEEN 0 AND 5);
ALTER TABLE users ADD COLUMN IF NOT EXISTS amulet_enchant INTEGER NOT NULL DEFAULT 0 CHECK (amulet_enchant BETWEEN 0 AND 5);

-- Verificación: las cinco columnas tienen que existir.
DO $$
DECLARE
    v_missing TEXT;
BEGIN
    SELECT string_agg(c, ', ') INTO v_missing
    FROM unnest(ARRAY['has_bank', 'bank_gold', 'dust', 'weapon_enchant', 'amulet_enchant']) AS c
    WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'users' AND column_name = c);
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'add_bank_dust_enchants: faltan columnas en users: [%]', v_missing;
    END IF;
END $$;
