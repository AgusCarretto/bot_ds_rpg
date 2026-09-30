-- =========================================================
-- Precio de las comidas (Consumibles) con una curva que SUBE MÁS RÁPIDO que la curación.
--
-- Antes todas costaban lo mismo por punto de vida (~0.22 oro/HP: 15 HP = 3 oro, 300 HP = 67 oro; ver
-- rebalance_consumables.sql, que buscaba "eficiencia pareja"). Dos cosas cambiaron: en /travel y /boss te podés
-- curar UNA sola vez por pelea (GameData/CombatHeal.cs), así que la comida que más cura pasó a ser mucho más
-- valiosa que antes, y /travel paga la fórmula de /hunt x10, o sea que el oro sobra y 67 oro por 300 HP era
-- regalado. Ahora: compra = ~0.1 x HP^1.6, redondeado (0.5 oro/HP la Mate Amargo, ~3 oro/HP la Mate Dulce de la
-- Abuela), y venta = 75% de la compra.
--
--   HP    compra  venta      (antes)
--    15        8      6       3 / 2
--    20       12      9       4 / 3
--    40       36     27       9 / 7
--    50       52     39      11 / 8
--    80      110     82      18 / 14
--   100      160    120      22 / 17
--   150      300    225      33 / 25
--   250      700    525      56 / 43
--   300      920    690      67 / 52
--
-- Re-ejecutable (UPDATE por nombre), y FALLA EN VOZ ALTA si falta alguna de las 9 comidas. Ejecutar DESPUÉS de
-- seed_consumables_and_base_swords.sql / finalize_consumable_catalog.sql / remove_legacy_consumables.sql.
-- =========================================================

DO $$
DECLARE
    t         RECORD;
    v_updated INTEGER;
BEGIN
    FOR t IN
        SELECT * FROM (VALUES
            ('Mate Amargo',                           8,   6),
            ('Pan Casero',                           12,   9),
            ('Empanada de Carne',                    36,  27),
            ('Choripán',                             52,  39),
            ('Vacío al Disco',                      110,  82),
            ('Asado de Tira',                       160, 120),
            ('Cordero Patagónico',                  300, 225),
            ('Asado Completo del Domingo en Familia', 700, 525),
            ('Mate Dulce de la Abuela',             920, 690)
        ) AS v(name, buy_price, sell_price)
    LOOP
        UPDATE items SET buy_price = t.buy_price, sell_price = t.sell_price
        WHERE name = t.name AND type = 'Consumable';

        GET DIAGNOSTICS v_updated = ROW_COUNT;
        IF v_updated <> 1 THEN
            RAISE EXCEPTION 'rebalance_consumable_prices: no existe la comida "%" (o está duplicada)', t.name;
        END IF;
    END LOOP;
END $$;

-- Chequeo rápido:
-- SELECT name, stat_value AS cura_hp, buy_price, sell_price FROM items WHERE type = 'Consumable' ORDER BY stat_value;
