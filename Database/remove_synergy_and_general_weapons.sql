-- =========================================================
-- v0.27.0 — Sin sinergia de clase y sin armas generales por zona (el dueño, 2026-10-26: «nadie hace el arma general, porque se ahorra los drops con
-- el arma con sinergia o el amuleto… sacamos la sinergia y el arma sin sinergia: queda un arma de cada clase con un ATQ fijo y un amuleto por zona»).
--
-- Migración para la BASE VIVA (una base nueva ya nace así: rework_drops_and_recipes.sql lleva el estado final). Va ANTES del binario v0.27.0:
-- el código deja de multiplicar por 1,5 el arma de clase, así que si el binario nuevo arranca con las estadísticas viejas el arma de todos rendiría un tercio menos.
--
--   1) El ×1,5 pasa a ser parte del número del arma de clase: ATQ 9/20/32/50/80 → 14/30/48/75/120, que es EXACTAMENTE lo que el combate calculaba antes
--      ((int)Math.Round(stat × 1,5), redondeo al par: 9 → 14, 15 → 22). Combate, duelos, Arena, jefes en equipo y la calibración de la escalera no se mueven.
--      Los precios de venta NO se tocan (siguen siendo los de antes).
--   2) Se les saca la receta a las 4 armas generales de las zonas 2 a 5 (Cuchilla de Cenizas, Pico de Minero Reforzado, Lanza de Magma, Martillo del Titán).
--      Los ÍTEMS quedan en el catálogo: quien tenga una la conserva (equipada o no) y puede venderla.
--   3) La Hoja de Acero Puro (la primera general) queda como la única, SIN ZONA ESPECÍFICA y hecha solo con materiales de recolección (2 Hierro + 10 Madera de
--      Pino: ~48 min de /mine, como antes), así cualquiera puede comprarse una base sin gastar drops.
--
-- Re-ejecutable (valores absolutos, no multiplica de nuevo) y FALLA EN VOZ ALTA si algo no quedó como se esperaba.
-- =========================================================

BEGIN;

-- 1) ATQ fijo de las armas de clase (las 20 de las zonas + las 3 viejas que ya nadie puede forjar).
UPDATE items i
SET stat_value = v.stat
FROM (VALUES
    ('Espada de Madera', 14), ('Daga Oxidada', 14), ('Arco Corto de Sauce', 14), ('Grimorio Desgastado', 14),
    ('Hacha de Hierro MK3', 30), ('Colmillo Nocturno', 30), ('Arco Élfico Ancestral', 30), ('Grimorio de las Tormentas', 30),
    ('Mazo de Escoria', 48), ('Dagas de Garra Maldita', 48), ('Boleadoras de Escoria', 48), ('Báculo de Tizón', 48),
    ('Facón de Hueso Añejo', 75), ('Cuchillos de Ceniza', 75), ('Arco de Caza Mayor', 75), ('Códice de las Brasas', 75),
    ('Espada del Abismo', 120), ('Colmillo del Cráter', 120), ('Arco del Alma Errante', 120), ('Báculo del Árbol de Vida', 120),
    ('Arco Largo del Cazador', 22), ('Dagas Gemelas de Sombra', 22), ('Báculo del Aprendiz', 22)
) AS v(name, stat)
WHERE i.name = v.name AND i.type = 'Weapon';

-- 2) Fuera las recetas de las armas generales de las zonas 2 a 5 (los ítems se quedan).
DELETE FROM recipe_ingredients
WHERE recipe_id IN (SELECT r.recipe_id FROM recipes r JOIN items i ON i.item_id = r.result_item_id
                    WHERE i.name IN ('Cuchilla de Cenizas', 'Pico de Minero Reforzado', 'Lanza de Magma', 'Martillo del Titán'));
DELETE FROM recipes
WHERE result_item_id IN (SELECT item_id FROM items WHERE name IN ('Cuchilla de Cenizas', 'Pico de Minero Reforzado', 'Lanza de Magma', 'Martillo del Titán'));

-- 3) La Hoja de Acero Puro: sin zona, sin afinidad y solo con materiales de recolección.
UPDATE recipes
SET zone_id = NULL, affinity = FALSE, gold_cost = 40
WHERE result_item_id = (SELECT item_id FROM items WHERE name = 'Hoja de Acero Puro');

DELETE FROM recipe_ingredients
WHERE recipe_id = (SELECT r.recipe_id FROM recipes r JOIN items i ON i.item_id = r.result_item_id WHERE i.name = 'Hoja de Acero Puro');

INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT r.recipe_id, ing.item_id, v.quantity
FROM recipes r
JOIN items res ON res.item_id = r.result_item_id AND res.name = 'Hoja de Acero Puro'
JOIN (VALUES ('Hierro', 2), ('Madera de Pino', 10)) AS v(name, quantity) ON TRUE
JOIN items ing ON ing.name = v.name;

-- 4) Verificación.
DO $$
DECLARE
    v_n   INTEGER;
    v_bad TEXT;
BEGIN
    SELECT string_agg(i.name || '=' || i.stat_value, ', ') INTO v_bad
    FROM items i
    WHERE i.type = 'Weapon' AND i.weapon_family IS NOT NULL AND i.stat_value NOT IN (14, 30, 48, 75, 120, 22);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'remove_synergy_and_general_weapons: armas de clase con un ATQ inesperado: [%]', v_bad;
    END IF;

    SELECT COUNT(*) INTO v_n
    FROM recipes r JOIN items i ON i.item_id = r.result_item_id
    WHERE i.type = 'Weapon' AND i.weapon_family IS NULL AND r.zone_id IS NOT NULL AND r.zone_id > 0;
    IF v_n <> 0 THEN
        RAISE EXCEPTION 'remove_synergy_and_general_weapons: quedan % arma(s) general(es) con zona (tienen que ser 0)', v_n;
    END IF;

    SELECT COUNT(*) INTO v_n
    FROM recipes r JOIN items i ON i.item_id = r.result_item_id
    WHERE i.name = 'Hoja de Acero Puro' AND r.zone_id IS NULL AND NOT r.affinity AND r.gold_cost = 40
      AND (SELECT COUNT(*) FROM recipe_ingredients ri JOIN items ing ON ing.item_id = ri.item_id
           WHERE ri.recipe_id = r.recipe_id AND ing.type IN ('Madera', 'Mineral')) = 2
      AND (SELECT COUNT(*) FROM recipe_ingredients ri WHERE ri.recipe_id = r.recipe_id) = 2;
    IF v_n <> 1 THEN
        RAISE EXCEPTION 'remove_synergy_and_general_weapons: la Hoja de Acero Puro no quedó sin zona y solo con materiales de recolección';
    END IF;

    SELECT COUNT(*) INTO v_n FROM items WHERE name IN ('Cuchilla de Cenizas', 'Pico de Minero Reforzado', 'Lanza de Magma', 'Martillo del Titán');
    IF v_n <> 4 THEN
        RAISE EXCEPTION 'remove_synergy_and_general_weapons: las 4 armas generales retiradas tienen que seguir en el catálogo (hay %)', v_n;
    END IF;
END $$;

COMMIT;
