-- =========================================================
-- Migración para bases YA EXISTENTES (una instalación nueva no las crea: los seeds de recetas ya no las traen):
-- saca la SEGUNDA arma general de cada zona. El molde pasó de 4 afinidad + 2 generales + 2 amuletos (8 recetas, 5
-- visibles por jugador) a 4 + 1 + 2 (7, 4 visibles: 2 armas y 2 amuletos), porque con UN solo drop por monstruo
-- (finalize_monster_roster.sql) no alcanzan las fuentes y la segunda general no le ganaba al arma de afinidad de nadie.
--
--   Zona 1: Machete de Chacra        Zona 2: Alabarda del Alfa     Zona 3: Martillo de Fragua
--   Zona 4: Hacha de Obsidiana       Zona 5: Guadaña de Almas
--
-- Se borra la RECETA (sus ingredientes se van en cascada) y el ÍTEM solo si nadie lo tiene ni lo lleva equipado:
-- inventory.item_id es ON DELETE CASCADE, así que borrar un ítem que alguien tiene le borraría la mochila. Si alguien
-- lo tiene, el ítem se queda (se sigue pudiendo equipar y vender) y el script avisa con un NOTICE.
--
-- Ejecutar DESPUÉS de re-correr seed_recipes.sql y seed_zone2..5_gear_and_recipes.sql (que cargan las recetas nuevas).
-- Re-ejecutable.
-- =========================================================

DO $$
DECLARE
    v_names TEXT[] := ARRAY['Machete de Chacra', 'Alabarda del Alfa', 'Martillo de Fragua', 'Hacha de Obsidiana', 'Guadaña de Almas'];
    v_kept  TEXT;
BEGIN
    DELETE FROM recipes WHERE result_item_id IN (SELECT item_id FROM items WHERE name = ANY (v_names));

    DELETE FROM items i
    WHERE i.name = ANY (v_names)
      AND NOT EXISTS (SELECT 1 FROM inventory inv WHERE inv.item_id = i.item_id AND inv.quantity > 0)
      AND NOT EXISTS (SELECT 1 FROM users u WHERE u.weapon_id = i.item_id OR u.amulet_id = i.item_id);

    SELECT string_agg(name, ', ') INTO v_kept FROM items WHERE name = ANY (v_names);
    IF v_kept IS NOT NULL THEN
        RAISE NOTICE 'remove_extra_general_recipes: sin receta pero con el ítem todavía en el catálogo (alguien lo tiene): %', v_kept;
    END IF;
END $$;
