-- =========================================================
-- SOLO para una base que YA está corriendo (una instalación nueva ya trae el catálogo recortado por
-- seed_recipes.sql — no hace falta correr esto): deja las recetas con el molde de una zona.
--
-- Qué hace:
--   1) Borra toda receta que NO sea una de las 8 de Zona 1 (4 de afinidad + 2 armas generales + 2 amuletos,
--      ver seed_recipes.sql): las 15 Legendarias de clase +45/+55 y las 8 de zonas 2 y 3.
--      Los ÍTEMS Legendarios de clase siguen en el catálogo (solo pierden la receta).
--   2) Borra del catálogo los 8 ítems del borrador de zonas 2 y 3 (el borrador de zonas 2 y 3 de la vuelta anterior, ya reemplazado por seed_zoneN_gear_and_recipes.sql),
--      que eran nuevos y ya no tienen receta.
--
-- SEGURO: los ítems se borran solo si NADIE los tiene. inventory.item_id es ON DELETE CASCADE (borraría el
-- inventario de un jugador en silencio) y users.weapon_id / amulet_id son SET NULL (lo desequiparía), así que
-- si alguien tiene alguno de los 8 ítems el script ABORTA con un error y no toca nada.
--
-- Orden en una base existente: add_recipe_zone_and_affinity.sql -> ESTE -> seed_recipes.sql.
-- Re-ejecutable: si ya está recortado no encuentra nada que borrar.
-- =========================================================

BEGIN;

DO $$
DECLARE
    held TEXT;
BEGIN
    SELECT string_agg(DISTINCT i.name, ', ') INTO held
    FROM items i
    WHERE i.name IN ('Espadón de Zafiro', 'Puñal de Ébano', 'Arco de Ébano', 'Grimorio de Zafiro', 'Maza del Yunque',
                     'Báculo del Árbol de Vida', 'Talismán de Ceniza Bendita', 'Coraza de Escoria')
      AND (EXISTS (SELECT 1 FROM inventory inv WHERE inv.item_id = i.item_id)
           OR EXISTS (SELECT 1 FROM users u WHERE u.weapon_id = i.item_id OR u.amulet_id = i.item_id));

    IF held IS NOT NULL THEN
        RAISE EXCEPTION 'trim_recipes_to_zone_template: alguien tiene estos ítems, no se borra nada: %', held;
    END IF;
END $$;

DELETE FROM recipes r
USING items i
WHERE i.item_id = r.result_item_id
  AND i.name NOT IN ('Espada de Madera', 'Daga Oxidada', 'Arco Corto de Sauce', 'Grimorio Desgastado',
                     'Hoja de Acero Puro', 'Hacha de Hierro MK3', 'Amuleto del Levantador', 'Hombreras de Cuero Grueso');

DELETE FROM items
WHERE name IN ('Espadón de Zafiro', 'Puñal de Ébano', 'Arco de Ébano', 'Grimorio de Zafiro', 'Maza del Yunque',
               'Báculo del Árbol de Vida', 'Talismán de Ceniza Bendita', 'Coraza de Escoria');

COMMIT;

-- Chequeo: tras correr seed_recipes.sql tienen que quedar exactamente 8 recetas.
-- SELECT count(*) FROM recipes;
