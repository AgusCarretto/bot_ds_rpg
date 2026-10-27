-- =========================================================
-- v0.30.0: el DRAGÓN (/dragon, /dragon-breed, /dragon-train, aa dragon; GameData/DragonRules.cs, Repositories/DragonRepository.cs). Un dragón por jugador, que se compra en la taberna, sube de TIER (1 a 10) criándolo con el de
-- otro jugador del mismo tier y sube de NIVEL (0 hasta tier × 10) entrenándolo con oro. Para una base que ya estaba andando: agrega la tabla (y, si ya tenía la primera versión de la tabla, le agrega la columna del nivel).
-- schema.sql ya la trae, así que una instalación nueva NO necesita correr esto. Re-ejecutable (IF NOT EXISTS) y sin tocar los dragones que ya hubiera.
--
-- Correrlo ANTES de arrancar el binario de la v0.30.0: PlayerBonusService lee la tabla en cada pelea y cada recolección. Sin ella el bot sigue andando (loguea el aviso y nadie tiene dragón), pero la taberna
-- ofrece comprarlo y la compra falla hasta que se corra. Se corre con psql (NO con el panel de consultas de Railway, que le agrega un LIMIT).
--
-- Con clave foránea a users (borrar la cuenta borra el dragón). El Fuego Nuevo NO lo toca: como las mascotas, dura toda la vida de la cuenta.
--   kind         fuerza (ATQ) | defensa (DEF) | oro (oro) | especial (EXP)
--   tier         1 a 10 (cada tier desbloquea una mejora nueva)
--   level        nivel de entrenamiento: 0 hasta tier × 10 (lo valida el código; cada nivel suma al bono de su tipo y sale más caro que el anterior)
--   fail_streak  intentos de criar fallidos SEGUIDOS en este tier (la «piedad»: al llegar al tope del tier el siguiente sale seguro)
--   last_bred_at la última cría (el cooldown de 12 horas se mide con el reloj de la base)
-- =========================================================

CREATE TABLE IF NOT EXISTS player_dragons (
    discord_id   BIGINT PRIMARY KEY REFERENCES users(discord_id) ON DELETE CASCADE,
    kind         TEXT NOT NULL CHECK (kind IN ('fuerza', 'defensa', 'oro', 'especial')),
    tier         INTEGER NOT NULL DEFAULT 1 CHECK (tier BETWEEN 1 AND 10),
    level        INTEGER NOT NULL DEFAULT 0 CHECK (level >= 0),
    fail_streak  INTEGER NOT NULL DEFAULT 0 CHECK (fail_streak >= 0),
    last_bred_at TIMESTAMPTZ,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Si la tabla ya existía de la primera versión (sin entrenamiento), le falta el nivel.
ALTER TABLE player_dragons ADD COLUMN IF NOT EXISTS level INTEGER NOT NULL DEFAULT 0 CHECK (level >= 0);
