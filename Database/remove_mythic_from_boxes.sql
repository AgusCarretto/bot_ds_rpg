-- =========================================================
-- v0.38.0: la Corteza del Árbol de Vida y el Fragmento de Meteorito (los dos materiales MÍTICOS de recolección, los que piden el Trinche y la Brasa del Fogón) dejan de salir de las cajas. El dueño
-- (2026-10-29): «pasa mucho que la gente saca en cajas el mítico. Vamos a sacar el mítico de las cajas… solo hablo de la madera y del meteorito, que solo puedan con el cofre del brasero».
--
--   psql -d asado-y-acero -v ON_ERROR_STOP=1 -f remove_mythic_from_boxes.sql
--
-- Qué cambia, en DOS partes:
--   1. DATOS (este script): el Arca del Soberano pierde sus dos entradas FIJAS (Fragmento de Meteorito y Corteza, 15 de peso cada una) y ese peso (30) pasa a su entrada de recolección, así sus tiradas
--      siguen sumando 1000. Es re-ejecutable (sin las entradas no hace nada).
--   2. CÓDIGO (el binario v0.38.0, GameData/BoxLoot.cs): la tirada de RECOLECCIÓN de una caja (que daba cada Mítico con 0,1 % por tirada) ya no puede sacar Mítico en ninguna caja salvo el Brasero del
--      Fogón. No hace falta tocar `box_loot` para eso: la regla es del sorteo. Quedan como únicas fuentes: /chop y /mine (0,5 % por acción), el Tabernero del Inframundo (3.000 Polvo) y el Brasero.
-- Es solo datos y se puede correr antes o después del binario; una instalación nueva lo trae al final de run_fresh_install.sql.
-- =========================================================

BEGIN;

WITH removed AS (
    DELETE FROM box_loot bl
    USING items bi, items ii
    WHERE bl.box_item_id = bi.item_id AND bl.item_id = ii.item_id
      AND bi.name = 'Arca del Soberano'
      AND ii.name IN ('Corteza del Árbol de Vida', 'Fragmento de Meteorito')
      AND bl.kind = 'item'
    RETURNING bl.weight
)
UPDATE box_loot
SET weight = weight + COALESCE((SELECT SUM(weight) FROM removed), 0)
WHERE kind = 'gather' AND box_item_id = (SELECT item_id FROM items WHERE name = 'Arca del Soberano');

-- Verificación: ninguna caja tiene los dos Míticos como entrada fija y las tiradas del Arca siguen sumando 1000.
DO $$
DECLARE
    v_fixed INTEGER;
    v_sum   INTEGER;
BEGIN
    SELECT count(*) INTO v_fixed
    FROM box_loot bl JOIN items ii ON ii.item_id = bl.item_id
    WHERE bl.kind = 'item' AND ii.name IN ('Corteza del Árbol de Vida', 'Fragmento de Meteorito');
    IF v_fixed <> 0 THEN
        RAISE EXCEPTION 'remove_mythic_from_boxes: quedaron % entradas fijas de la Corteza o el Meteorito en cajas', v_fixed;
    END IF;

    SELECT COALESCE(SUM(bl.weight), 0) INTO v_sum
    FROM box_loot bl JOIN items bi ON bi.item_id = bl.box_item_id
    WHERE bi.name = 'Arca del Soberano' AND bl.kind <> 'gold';
    IF v_sum <> 1000 THEN
        RAISE EXCEPTION 'remove_mythic_from_boxes: las tiradas del Arca del Soberano suman % (tienen que sumar 1000)', v_sum;
    END IF;

    RAISE NOTICE 'remove_mythic_from_boxes: listo (el Arca sin Míticos fijos, sus tiradas suman %).', v_sum;
END $$;

COMMIT;
