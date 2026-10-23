-- =========================================================
-- El jefe mundial (v0.24.0, GameData/WorldBoss.cs): un jefe por día (hora de Uruguay) que pelean ENTRE TODOS los anotados a las 00:00. Para una base que ya estaba andando: agrega las 2 tablas.
-- schema.sql ya las trae, así que una instalación nueva NO necesita correr esto. Re-ejecutable (IF NOT EXISTS).
--
-- CORRER ANTES de arrancar el binario v0.24.0: el reloj (Modules/ArenaScheduler.cs) consulta worldboss_days cada 30 segundos; sin la tabla solo registra un error por ciclo, pero /miniboss no anda.
-- =========================================================

CREATE TABLE IF NOT EXISTS worldboss_days (
    day              DATE PRIMARY KEY,                -- el día de Uruguay del jefe (se pelea a la medianoche que lo cierra)
    status           TEXT NOT NULL DEFAULT 'open' CHECK (status IN ('open', 'resolved', 'cancelled')),
    channel_id       BIGINT,                          -- canal donde se anotó el primero: ahí se anuncia el resultado
    boss_key         TEXT,                            -- cuál jefe fue (WorldBossRules.Bosses)
    participants     INTEGER NOT NULL DEFAULT 0,
    total_damage_pct NUMERIC(8, 2),                   -- cuánta vida le sacaron entre todos (100 = cayó)
    defeated         BOOLEAN,
    hunts            INTEGER,                         -- el premio de cada anotado, en cacerías de oro de su zona
    resolved_at      TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS worldboss_entries (
    day          DATE        NOT NULL REFERENCES worldboss_days (day) ON DELETE CASCADE,
    discord_id   BIGINT      NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    display_name TEXT        NOT NULL,                -- el nombre al anotarse: el anuncio lo muestra aunque la persona se vaya
    joined_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    damage_pct   NUMERIC(8, 2),                       -- lo que le sacó al jefe (se llena al pelear)
    reward_gold  INTEGER,                             -- el oro que cobró (se llena al pelear)
    PRIMARY KEY (day, discord_id)
);

CREATE INDEX IF NOT EXISTS idx_worldboss_days_status ON worldboss_days (status, day);
