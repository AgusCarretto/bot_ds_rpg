-- =========================================================
-- Corre TODOS los scripts necesarios, EN ORDEN, para levantar el juego completo (esquema + items +
-- recetas + zonas/monstruos + catálogo final de consumibles + emojis) contra una base de Postgres
-- COMPLETAMENTE VACÍA. Ver CLAUDE.md, sección "Database setup (fresh machine)".
--
-- OJO — herramienta: esto usa \ir (meta-comando de psql), NO es SQL estándar. Funciona con:
--   - psql (la terminal de línea de comandos que viene con Postgres), parado en esta carpeta:
--       psql -U postgres -d asado-y-acero -f run_fresh_install.sql
--   - El "PSQL Tool" de pgAdmin (el ícono de terminal, NO el "Query Tool" — ese último solo manda
--     SQL crudo al servidor y no entiende \ir, va a tirar error de sintaxis).
--   - DBeaver u otros clientes: probablemente NO sirve — correlos a mano, uno por uno, en el orden
--     de abajo (son los mismos 11 archivos, en esta carpeta).
--
-- USAR SOLO CONTRA UNA BASE VACÍA. seed.sql, add_weapon_family.sql y
-- seed_class_gear_and_monster_drops.sql NO son idempotentes (duplican filas si la base ya tiene
-- sus datos) — si tu base YA tiene algo cargado, NO corras este archivo entero: revisá primero qué
-- te falta y corré esos scripts sueltos a mano.
-- =========================================================

\set ON_ERROR_STOP on

\ir schema.sql
\ir seed.sql
\ir add_weapon_family.sql
\ir seed_class_gear_and_monster_drops.sql
\ir seed_consumables_and_base_swords.sql
\ir seed_zones_and_monsters.sql
-- Los jefes de zona (/boss, /raid y el bloqueo de progresión de /zona) y los ítems que dropean — antes
-- faltaba en esta lista, así que una base nueva se quedaba sin ningún jefe.
\ir seed_zone_bosses.sql
-- seed_recipes.sql va DESPUÉS de los seeds de arriba a propósito: sus recetas usan ítems que nacen ahí
-- (Espada de Madera / Hoja de Acero Puro en seed_consumables_and_base_swords.sql, Pluma de Ñandú /
-- Colmillo de Cimarrón en seed_zones_and_monsters.sql). Una receta cuyo ítem todavía no existe se omitiría
-- en SILENCIO, o peor, se crearía sin ese ingrediente — por eso este seed se verifica solo y FALLA en voz alta.
-- (Las recetas de las zonas 2 y 3 son un borrador, draft_zone2_3_gear_and_recipes.sql: NO se corre acá.)
\ir seed_recipes.sql
\ir finalize_consumable_catalog.sql
\ir remove_legacy_consumables.sql
\ir update_item_emojis.sql

\echo 'Listo — base cargada completa: esquema, items, recetas, zonas/monstruos, catálogo final de consumibles y emojis.'
