-- =========================================================
-- Carga de TODOS los emojis (de los ítems y las caras de los monstruos) en una base que YA existe, en UN solo comando, desde esta carpeta (Database):
--
--   psql "<la URL pública de la base>" -v ON_ERROR_STOP=1 -f apply_emojis.sql
--
-- Es solo DATOS (no cambia el esquema ni el binario, no hace falta redesplegar): corre update_item_emojis.sql y update_monster_portraits.sql, que son re-ejecutables
-- (cada UPDATE es por nombre) y son la ÚNICA fuente de los códigos. Cuando el dueño sube arte nuevo al portal, se agrega el UPDATE ahí y se vuelve a correr esto.
-- Al final muestra lo que TODAVÍA no tiene arte propio (vacío o con un emoji común de relleno), sin contar los 7 ítems viejos que nadie puede conseguir.
-- Algunas pantallas guardan datos en caché: pueden tardar hasta 5 minutos en mostrar el emoji nuevo.
-- =========================================================
\set ON_ERROR_STOP on
\ir update_item_emojis.sql
\ir update_monster_portraits.sql

\echo ''
\echo 'Ítems que todavía NO tienen arte propio (vacío o con un emoji común de relleno):'
SELECT type, name, COALESCE(emoji, '(sin emoji)') AS emoji
FROM items
WHERE (emoji IS NULL OR emoji !~ '^<a?:[A-Za-z0-9_]+:[0-9]+>$')
  AND name NOT IN ('Bombilla de Hierro Maldito', 'Botas de Silencio', 'Collar de Hueso', 'Ojo de Jabalí', 'Arco Largo del Cazador', 'Báculo del Aprendiz', 'Dagas Gemelas de Sombra')
ORDER BY type, name;

\echo 'Monstruos sin cara:'
SELECT name FROM monsters WHERE portrait_emoji IS NULL OR portrait_emoji !~ '^<a?:[A-Za-z0-9_]+:[0-9]+>$' ORDER BY name;

\echo 'Emojis cargados.'
