-- =========================================================
-- v0.38.0: los AMULETOS de las 5 zonas piden ×4 de CADA drop de cacería de su zona (antes: ×7 y ×7 en las Zonas 2-5 y ×5 y ×5 en la 1). El dueño (2026-10-29): «es una exageración para alguien de
-- FN 0 tener que farmear 18 drops para 1 zona sola… prefiero pedir más materiales y bajar un poco el drop: si me pedís 7 y 7 un amuleto es imposible, y más sabiendo que con 4 ya hago el arma…
-- así solo se farmea 8 en amuleto y 4 en arma».
--
--   psql -d asado-y-acero -v ON_ERROR_STOP=1 -f rebalance_amulet_drops.sql
--
-- Por qué 4 y 4 y no «3 en el que ya está en el arma y 5 en el otro»: el amuleto es UNO por zona para las cuatro clases y cada clase tiene SU drop en el arma (Guerrero y Ninja el físico, Arquero y
-- Hechicero el arcano), así que «el que ya está en el arma» cambia con la clase; con 3 y 5 los de una ruta farmeaban 7 y 5 y los de la otra 9 y 3 (un 29 % más de espera para dos de las cuatro clases).
-- Con 4 y 4 son 8 en el amuleto para todos (4 + 4 del arma de su ruta = 8 de un drop y 4 del otro: 12 por zona en vez de 18). El tiempo de los drops de un amuleto baja de ~233 a ~133 minutos y lo
-- frena la recolección (Oro Puro ×4 en las Zonas 2-3 = ~190 min, Zafiro ×1 en las 4-5 = ~143): si se lo quiere más largo, se sube la RECOLECCIÓN (el dueño prefiere pedir más materiales), no los drops.
--
-- Es solo DATOS (valor absoluto y re-ejecutable, una transacción) y se puede correr antes o después del binario. Una instalación nueva lo trae en run_fresh_install.sql después de
-- remove_travel_drops.sql y rebalance_weapon_drops.sql.
-- =========================================================

BEGIN;

UPDATE recipe_ingredients ri
SET quantity = 4
FROM recipes r
JOIN items res ON res.item_id = r.result_item_id
WHERE ri.recipe_id = r.recipe_id
  AND res.type = 'Amulet'
  AND r.zone_id BETWEEN 1 AND 5
  AND ri.item_id IN (
      SELECT md.item_id
      FROM monsters m JOIN monster_drops md ON md.monster_id = m.monster_id
      WHERE NOT m.is_boss AND NOT m.is_travel AND m.zone_id > 0);

-- Verificación: los 5 amuletos de las zonas 1 a 5 piden exactamente 2 drops de cacería ×4 (y nada más de drops).
DO $$
DECLARE
    v_ok  INTEGER;
    v_bad INTEGER;
BEGIN
    SELECT count(*) INTO v_ok
    FROM recipe_ingredients ri
    JOIN recipes r ON r.recipe_id = ri.recipe_id
    JOIN items res ON res.item_id = r.result_item_id
    JOIN items ing ON ing.item_id = ri.item_id
    WHERE res.type = 'Amulet' AND r.zone_id BETWEEN 1 AND 5 AND ing.type = 'Material' AND ri.quantity = 4;

    SELECT count(*) INTO v_bad
    FROM recipe_ingredients ri
    JOIN recipes r ON r.recipe_id = ri.recipe_id
    JOIN items res ON res.item_id = r.result_item_id
    JOIN items ing ON ing.item_id = ri.item_id
    WHERE res.type = 'Amulet' AND r.zone_id BETWEEN 1 AND 5 AND ing.type = 'Material' AND ri.quantity <> 4;

    IF v_ok <> 10 OR v_bad <> 0 THEN
        RAISE EXCEPTION 'rebalance_amulet_drops: se esperaban 10 ingredientes de drop ×4 en los 5 amuletos y ninguno distinto (hay % en ×4 y % distintos)', v_ok, v_bad;
    END IF;

    RAISE NOTICE 'rebalance_amulet_drops: listo. Los 5 amuletos piden sus dos drops de cacería ×4 (8 en total).';
END $$;

COMMIT;
