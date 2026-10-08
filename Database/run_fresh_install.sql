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
--     de abajo (son los mismos 27 archivos, en esta carpeta).
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
-- El monstruo dedicado de /travel (uno por zona, is_travel = true): usa zonas y materiales que ya existen a esta altura.
\ir seed_travel_monsters.sql
-- El plantel final: 3 monstruos de /hunt en Zona 1 y UN solo drop por monstruo (pisa las listas de dos drops de los
-- seeds de arriba). Va DESPUÉS de los tres que crean monstruos; las recetas de abajo se calibran contra estos drops.
\ir finalize_monster_roster.sql
-- seed_recipes.sql va DESPUÉS de los seeds de arriba a propósito: sus recetas usan ítems que nacen ahí
-- (Espada de Madera / Hoja de Acero Puro en seed_consumables_and_base_swords.sql, Pluma de Ñandú /
-- Colmillo de Cimarrón en seed_zones_and_monsters.sql). Una receta cuyo ítem todavía no existe se omitiría
-- en SILENCIO, o peor, se crearía sin ese ingrediente — por eso este seed se verifica solo y FALLA en voz alta.
\ir seed_recipes.sql
-- Recetas de Zona 2 (escalera de zonas): usa drops del jefe (seed_zone_bosses.sql) y le quita al Hacha su
-- receta de Zona 1, así que va DESPUÉS de seed_recipes.sql. También se verifica sola.
\ir seed_zone2_gear_and_recipes.sql
-- Zonas 3, 4 y 5 (escalera de zonas): mismo molde. Cada una re-estatea las Legendarias que ya existían (las de
-- seed_class_gear_and_monster_drops.sql), crea sus ítems nuevos y usa los drops de los jefes: van en orden.
\ir seed_zone3_gear_and_recipes.sql
\ir seed_zone4_gear_and_recipes.sql
\ir seed_zone5_gear_and_recipes.sql
\ir finalize_consumable_catalog.sql
\ir remove_legacy_consumables.sql
-- Precio de las comidas: sube más rápido que la curación (una sola curación por pelea en /travel y /boss).
\ir rebalance_consumable_prices.sql
-- Comida de 9 a 6 (con reembolso a quien tuviera las retiradas) y buffs de los banquetes Míticos (+15% de ataque).
\ir rework_food_catalog.sql
-- Las cajas y su botín: usa materiales, comida y banquetes que ya existen a esta altura.
\ir seed_boxes.sql
-- El rework final (v0.7.0): 20 monstruos, 3 drops por zona, el jefe da un cofre y 6 recetas por zona. PISA lo de los seeds de arriba (recetas,
-- drops de jefes, Perro Cimarrón...) y usa los cofres de seed_boxes.sql, así que va ACÁ: después de todo lo demás y antes de los emojis.
\ir rework_drops_and_recipes.sql
-- Cajas v2 (v0.8.0): cada caja dice cuántos ítems trae, cuesta mucho más y su botín sale con las chances reales de /chop, /mine y los drops de zona.
\ir rework_boxes.sql
\ir update_item_emojis.sql
-- La cara de cada monstruo (emoji de la aplicación, miniatura de los mensajes de combate): va DESPUÉS de los scripts que crean los 20 monstruos.
\ir update_monster_portraits.sql
-- Las mascotas (v0.10.0): los 5 huevos, la comida y las 5 especies. Las TABLAS están en schema.sql. Va al final: necesita zonas e ítems.
\ir seed_pets.sql
-- El Fogón Eterno (v0.11.0): la zona puerta (id 0), su equipo, sus recetas y el jefe. Usa los drops y los materiales de las 5 zonas: va al final.
\ir seed_fogon.sql
-- Cofres parejos (v0.14.0): las cajas Cofre de Escoria y Brasero del Fogón, la caja que da cada zona (zone_boxes), el premio del Asador y la 6.ª mascota. Va al final: pisa el cofre del jefe de la 5.
\ir seed_zone_boxes.sql
-- Se retiran los 17 trofeos de las cajas (v0.14.4): materiales que solo salían de las cajas y no sirven para nada. Va DESPUÉS de seed_zone_boxes.sql (que todavía los nombra) y de rework_boxes.sql.
\ir retire_box_trophies.sql

\echo 'Listo — base cargada completa: esquema, items, recetas, zonas/monstruos, catálogo final de consumibles y emojis.'
