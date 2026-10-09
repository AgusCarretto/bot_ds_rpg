-- =========================================================
-- v0.16.0: los recordatorios de cooldown (GameData/Reminders.cs). Migración para una base que YA existe (una base nueva ya trae estas tablas en schema.sql).
-- Re-ejecutable. Correrla ANTES de arrancar el binario v0.16.0: después de cada comando el bot lee y escribe estas tablas.
--
--   psql -d asado-y-acero -v ON_ERROR_STOP=1 -f add_reminders.sql
-- =========================================================

CREATE TABLE IF NOT EXISTS reminders (
    discord_id BIGINT      NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    kind       TEXT        NOT NULL,
    channel_id BIGINT      NOT NULL,
    due_at     TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (discord_id, kind)
);

CREATE INDEX IF NOT EXISTS idx_reminders_due_at ON reminders (due_at);

CREATE TABLE IF NOT EXISTS reminder_settings (
    discord_id BIGINT PRIMARY KEY REFERENCES users (discord_id) ON DELETE CASCADE,
    off_kinds  TEXT[] NOT NULL DEFAULT '{}'
);

DO $$
BEGIN
    IF to_regclass('public.reminders') IS NULL OR to_regclass('public.reminder_settings') IS NULL THEN
        RAISE EXCEPTION 'add_reminders: las tablas no quedaron creadas.';
    END IF;
END $$;
