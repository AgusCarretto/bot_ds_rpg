-- =========================================================
-- v0.16.0: TODAS las migraciones que lleva una base que todavía está en la v0.15.0 hasta la v0.16.0, en el orden que va, en UN solo comando, desde esta carpeta (Database):
--
--   psql "<la URL pública de la base>" -v ON_ERROR_STOP=1 -f apply_v0160.sql
--
-- Es re-ejecutable (cada script pone valores absolutos o usa IF NOT EXISTS) y se verifica solo: si algo no queda como debe, frena con un error en lugar de dejarlo a medias.
-- Va ANTES de subir el binario a main: el binario nuevo lee la columna items.dust_value en CADA consulta de ítems y las tablas reminders / reminder_settings después de CADA comando.
--   1) apply_v0151.sql   Polvo por origen (agrega items.dust_value), crafteo más corto y amuletos 35/60/85/120 (Brasa 165) con el daño de los jefes
--   2) add_reminders.sql los recordatorios de cooldown (tablas reminders y reminder_settings)
-- Una base NUEVA no necesita esto: run_fresh_install.sql ya trae todo.
-- Hacé un backup antes (DEPLOY.md, sección 6).
-- =========================================================
\set ON_ERROR_STOP on
\ir apply_v0151.sql
\ir add_reminders.sql
\echo 'v0.16.0 aplicada: v0.15.1 (Polvo por origen, crafteo y amuletos) y los recordatorios.'
