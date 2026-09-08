-- =========================================================
-- Fix de exploit económico: "Hacha de Hierro MK3" costaba 150 de oro forjarla (receta genérica,
-- ver Database/seed_consumables_and_base_swords.sql) pero vendía por 500 — +350 de oro garantizado
-- por ciclo de forja+venta, sin cooldown en /forge make, con materiales comunes/farmeables
-- (Hierro x5 + Cuero Grueso x3). El sell_price de 500 venía de la escala pensada para armas base
-- SIN receta (add_weapon_family.sql), que nunca se ajustó cuando esta arma puntual sí consiguió
-- una receta barata en una sesión posterior.
--
-- Nuevo precio: mismo ratio sell:buy ≈ 0.77 que ya usa el resto del catálogo, y sell_price
-- (60) muy por debajo de la mitad del gold_cost de su receta (150) — así ni ignorando el costo de
-- los materiales, forjar y vender puede dar ganancia neta.
--
-- Ejecutar UNA SOLA VEZ (UPDATE directo, seguro de re-correr: deja el mismo valor si ya se aplicó).
-- =========================================================

UPDATE items SET sell_price = 60, buy_price = 78 WHERE name = 'Hacha de Hierro MK3';

-- Chequeo rápido: sell_price (60) tiene que quedar bien por debajo de la mitad del gold_cost de su
-- receta (150 / 2 = 75).
-- SELECT i.name, i.sell_price, i.buy_price, r.gold_cost FROM items i JOIN recipes r ON r.result_item_id = i.item_id WHERE i.name = 'Hacha de Hierro MK3';
