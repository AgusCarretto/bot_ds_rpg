-- =========================================================
-- v0.36.0 (ajuste): las armas de clase de las Zonas 2 a 5 vuelven a pedir ×4 de su drop de cacería (remove_travel_drops.sql las había subido de ×4 a ×5). El dueño (2026-10-28): «baja 1 todas las armas
-- del tema drop». Con ×4 el drop de cacería tarda ~133 min y la recolección (6 Hierro, ~143 min) frena el arma igual que antes de quitar el drop de viaje: el tiempo total queda como era (~143-150 min).
-- Los AMULETOS no se tocan (siguen en ×7 de cada drop de cacería).
--
--   psql -d asado-y-acero -v ON_ERROR_STOP=1 -f rebalance_weapon_drops.sql
--
-- Es solo DATOS: se puede correr antes o después del binario (el código lee las recetas de la base). Para una base que YA corrió remove_travel_drops.sql (como la de producción el 2026-10-28) es lo único
-- que hay que correr; una instalación nueva lo trae en run_fresh_install.sql justo después de remove_travel_drops.sql. Es RE-EJECUTABLE (valor absoluto, solo toca lo que está en ×5) y va en UNA transacción.
-- =========================================================

BEGIN;

UPDATE recipe_ingredients ri
SET quantity = 4
FROM recipes r
JOIN items res ON res.item_id = r.result_item_id
WHERE ri.recipe_id = r.recipe_id
  AND res.type = 'Weapon'
  AND r.zone_id BETWEEN 2 AND 5
  AND ri.quantity = 5
  AND ri.item_id IN (
      SELECT md.item_id
      FROM monsters m JOIN monster_drops md ON md.monster_id = m.monster_id
      WHERE NOT m.is_boss AND NOT m.is_travel AND m.zone_id > 0);

-- Verificación: las 16 armas de clase de las Zonas 2 a 5 piden su drop de cacería ×4 y ninguna quedó en ×5.
DO $$
DECLARE
    v_ok  INTEGER;
    v_bad INTEGER;
BEGIN
    SELECT count(*) INTO v_ok
    FROM recipe_ingredients ri
    JOIN recipes r ON r.recipe_id = ri.recipe_id
    JOIN items res ON res.item_id = r.result_item_id
    WHERE res.type = 'Weapon' AND r.zone_id BETWEEN 2 AND 5 AND ri.quantity = 4
      AND ri.item_id IN (SELECT md.item_id FROM monsters m JOIN monster_drops md ON md.monster_id = m.monster_id WHERE NOT m.is_boss AND NOT m.is_travel AND m.zone_id > 0);

    SELECT count(*) INTO v_bad
    FROM recipe_ingredients ri
    JOIN recipes r ON r.recipe_id = ri.recipe_id
    JOIN items res ON res.item_id = r.result_item_id
    WHERE res.type = 'Weapon' AND r.zone_id BETWEEN 2 AND 5 AND ri.quantity > 4
      AND ri.item_id IN (SELECT md.item_id FROM monsters m JOIN monster_drops md ON md.monster_id = m.monster_id WHERE NOT m.is_boss AND NOT m.is_travel AND m.zone_id > 0);

    IF v_ok <> 16 OR v_bad <> 0 THEN
        RAISE EXCEPTION 'rebalance_weapon_drops: se esperaban 16 armas de las zonas 2-5 con su drop de cacería ×4 y ninguna en más (hay % en ×4 y % en más)', v_ok, v_bad;
    END IF;

    RAISE NOTICE 'rebalance_weapon_drops: listo. Armas de las zonas 2-5 con su drop de cacería ×4: %.', v_ok;
END $$;

COMMIT;
