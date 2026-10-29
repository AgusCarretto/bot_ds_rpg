-- =========================================================
-- v0.40.0: tiers de mascotas. Migración para una base que YA existe (una base nueva ya trae las columnas en schema.sql). Re-ejecutable. Correrlo ANTES de arrancar el binario v0.40.0:
-- cada consulta de mascotas lee las dos columnas. Ejecutar con psql:
--     psql -d asado-y-acero -v ON_ERROR_STOP=1 -f add_pet_tiers.sql
--
--   tier              1, 2 o 3: el tope de nivel de la mascota (10, 20, 30). Se sube evolucionándola (GameData/PetRules.cs: nivel máximo del tier + horas de expedición + huevos sobrantes de su especie).
--   expedition_hours  horas de expedición que ya hizo ESA mascota (suman al recoger el premio de una expedición; antes de la v0.40.0 no se contaban, así que arrancan en 0).
-- =========================================================

ALTER TABLE player_pets ADD COLUMN IF NOT EXISTS tier SMALLINT NOT NULL DEFAULT 1;
ALTER TABLE player_pets ADD COLUMN IF NOT EXISTS expedition_hours INTEGER NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'player_pets_tier_check') THEN
        ALTER TABLE player_pets ADD CONSTRAINT player_pets_tier_check CHECK (tier BETWEEN 1 AND 3);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'player_pets_expedition_hours_check') THEN
        ALTER TABLE player_pets ADD CONSTRAINT player_pets_expedition_hours_check CHECK (expedition_hours >= 0);
    END IF;
    RAISE NOTICE 'add_pet_tiers: listo. Mascotas: %, con tier distinto de 1: %', (SELECT count(*) FROM player_pets), (SELECT count(*) FROM player_pets WHERE tier <> 1);
END $$;
