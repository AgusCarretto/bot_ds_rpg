-- v0.22.0: encantamientos con EFECTO (GameData/Enchantments.cs). Cada pieza equipada tiene un efecto además del tier (users.weapon_enchant / amulet_enchant, que no cambian):
--   arma:    filo (+% de ATQ de la pieza, el de siempre) | critico | sangre
--   amuleto: guarda (+% de DEF de la pieza, el de siempre) | vida | esquive
-- Las piezas que ya estaban encantadas quedan con filo / guarda (el DEFAULT), o sea EXACTAMENTE como estaban: nadie pierde nada.
--
-- CORRER ANTES de arrancar el binario v0.22.0: toda consulta de jugador (Repositories/UserSql.cs) lee las dos columnas nuevas. Re-ejecutable.

BEGIN;

ALTER TABLE users ADD COLUMN IF NOT EXISTS weapon_enchant_kind TEXT NOT NULL DEFAULT 'filo';
ALTER TABLE users ADD COLUMN IF NOT EXISTS amulet_enchant_kind TEXT NOT NULL DEFAULT 'guarda';

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'users_weapon_enchant_kind_check') THEN
        ALTER TABLE users ADD CONSTRAINT users_weapon_enchant_kind_check CHECK (weapon_enchant_kind IN ('filo', 'critico', 'sangre'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'users_amulet_enchant_kind_check') THEN
        ALTER TABLE users ADD CONSTRAINT users_amulet_enchant_kind_check CHECK (amulet_enchant_kind IN ('guarda', 'vida', 'esquive'));
    END IF;

    -- Verificación: las dos columnas existen y todos los jugadores tienen un efecto válido.
    IF (SELECT count(*) FROM information_schema.columns WHERE table_name = 'users' AND column_name IN ('weapon_enchant_kind', 'amulet_enchant_kind')) <> 2 THEN
        RAISE EXCEPTION 'add_enchant_kinds: faltan columnas en users';
    END IF;
    RAISE NOTICE 'add_enchant_kinds: ok. Jugadores con arma encantada: %, con amuleto encantado: %',
        (SELECT count(*) FROM users WHERE weapon_enchant > 0), (SELECT count(*) FROM users WHERE amulet_enchant > 0);
END $$;

COMMIT;
