-- =========================================================
-- v0.39.0: expediciones de mascotas (/pet expedition, aa pet expedition; GameData/PetExpeditions.cs). Migración para una base que YA existe: una base nueva ya trae la tabla en schema.sql.
-- Re-ejecutable, solo crea la tabla. ANTES del binario v0.39.0 no es obligatorio (lo único que la lee es /pet expedition, el aviso de que volvió y la línea de /cd, que ante un error
-- se omiten sin romper nada), pero /pet expedition falla hasta que exista. Ejecutar con psql:
--     psql -d asado-y-acero -v ON_ERROR_STOP=1 -f add_pet_expeditions.sql
--
-- Una fila por jugador: solo UNA mascota de expedición a la vez (el PRIMARY KEY lo garantiza aunque dos comandos corran juntos). La fila se BORRA al recoger el premio.
-- El fin lo calcula la base con su reloj (now() + las horas), el mismo con el que se cobra, así no depende de que el reloj del bot coincida.
-- =========================================================

CREATE TABLE IF NOT EXISTS pet_expeditions (
    discord_id  BIGINT      PRIMARY KEY REFERENCES users (discord_id) ON DELETE CASCADE,
    species_id  INTEGER     NOT NULL REFERENCES pet_species (species_id) ON DELETE CASCADE,
    hours       INTEGER     NOT NULL CHECK (hours > 0),
    started_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    ends_at     TIMESTAMPTZ NOT NULL
);

DO $$
BEGIN
    RAISE NOTICE 'add_pet_expeditions: listo. Expediciones en curso: %', (SELECT count(*) FROM pet_expeditions);
END $$;
