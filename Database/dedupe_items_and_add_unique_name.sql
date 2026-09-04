-- =========================================================
-- Limpia los duplicados/huérfanos que aparecieron en "items" por no tener nunca un UNIQUE(name),
-- y agrega esa constraint para que no vuelva a pasar.
--
-- Diagnóstico (item_id reales de la base de Agus, confirmados por SELECT antes de escribir esto):
--   - 'Espada de Madera': id 17 y 57, duplicado EXACTO (mismos stats/precios).
--   - 'Hacha de Hierro MK3' / 'Hoja de Acero Puro': los id 18 y 19 quedaron con los NOMBRES
--     CRUZADOS de una carga vieja (18 = nombre "Hacha..." pero stats Raro/15/195, que en realidad
--     son los de "Hoja..."; 19 al revés). Los id 58 y 59 son los que coinciden exacto con
--     Database/seed_consumables_and_base_swords.sql (la fuente de verdad actual) — esos quedan.
--   - 'Cuero de Jabalí', 'Colmillo de Lobo', 'Núcleo de Golem', 'Medalla del Náutico',
--     'Sello de Forja MK3': huérfanos de una era anterior del proyecto, ninguna receta ni drop
--     de monstruo los referencia hoy con ese nombre exacto.
--
-- Ejecutar UNA SOLA VEZ. Si tus item_id no coinciden exactamente con los de arriba (por ejemplo
-- si ya borraste algo a mano mientras tanto), correlo por partes y ajustá los números primero con:
--   SELECT item_id, name, type, rarity, stat_value, buy_price FROM items
--   WHERE name IN ('Espada de Madera','Hacha de Hierro MK3','Hoja de Acero Puro') ORDER BY name, item_id;
-- =========================================================

-- 1) "Espada de Madera": nos quedamos con la más vieja (17), borramos la copia repetida.
DELETE FROM items WHERE item_id = 57;

-- 2) "Hacha de Hierro MK3" / "Hoja de Acero Puro" mal etiquetados. El DELETE se lleva en cascada
--    cualquier receta que apuntara al id 18 (recipes.result_item_id → ON DELETE CASCADE) — la
--    receta correcta, apuntando al id 59, queda intacta.
DELETE FROM items WHERE item_id IN (18, 19);

-- 3) Huérfanos sin ningún gancho en el juego actual.
DELETE FROM items WHERE name IN (
    'Cuero de Jabalí', 'Colmillo de Lobo', 'Núcleo de Golem',
    'Medalla del Náutico', 'Sello de Forja MK3'
);

-- 4) Nunca más: de acá en adelante, "ON CONFLICT DO NOTHING" en los seeds (seed.sql,
--    seed_class_gear_and_monster_drops.sql, seed_consumables_and_base_swords.sql, etc.) va a
--    prevenir duplicados de verdad si alguno se re-corre por error. Si esta línea falla, es porque
--    queda ALGÚN OTRO nombre duplicado además de los de arriba — el mensaje de error te va a decir
--    cuál; conseguí su item_id con el SELECT del comentario de arriba y resolvelo antes de reintentar.
ALTER TABLE items ADD CONSTRAINT items_name_unique UNIQUE (name);
