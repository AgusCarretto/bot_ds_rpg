-- =========================================================
-- v0.15.1: las tres migraciones de la versión, en el orden que va, para una base que YA existe (la de Railway). UN solo comando, desde esta carpeta (Database):
--
--   psql "<la URL pública de la base>" -v ON_ERROR_STOP=1 -f apply_v0151.sql
--
-- Es re-ejecutable (cada script pone valores absolutos) y se verifica solo: si algo no queda como debe, frena con un error en lugar de dejarlo a medias.
-- Va ANTES de subir el binario v0.15.1 a main: rebalance_dust.sql agrega la columna items.dust_value, que el binario nuevo lee en cada consulta de ítems.
--   1) rebalance_dust.sql      el Polvo de los drops de monstruo por su origen (17 cacería / 38 viaje) — agrega la columna
--   2) rebalance_crafting.sql  el crafteo más corto (1 drop de viaje menos, sin Mítico en la Zona 5, Corteza y Meteorito en el equipo del Fogón)
--   3) rebalance_amulets.sql   los amuletos 35/60/85/120 (Brasa 165) y el daño de los jefes
-- Una base NUEVA no necesita esto: run_fresh_install.sql ya trae todo.
-- Hacé un backup antes (DEPLOY.md, sección 6).
-- =========================================================
\set ON_ERROR_STOP on
\ir rebalance_dust.sql
\ir rebalance_crafting.sql
\ir rebalance_amulets.sql
\echo 'v0.15.1 aplicada: Polvo por origen, crafteo y amuletos.'
