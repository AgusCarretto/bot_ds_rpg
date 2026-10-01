-- =========================================================
-- REPORTE (solo lectura, NO forma parte de la instalación): cuánto "vale" lo que da cada caja contra lo que cuesta.
--
--   psql -U postgres -d asado-y-acero -P pager=off -f report_box_economy.sql
--
-- valor_esperado = tiradas x (suma de peso x cantidad promedio x valor) / suma de pesos, donde el valor de un ítem es lo que
-- pagaría la tienda al VENDERLO (items.sell_price; una caja de menor tier se cuenta por su venta, no por su precio de compra) y el
-- oro vale su cantidad. Es un piso: un jugador valora un Hierro más que sus 10 de oro porque le ahorra media hora de minar.
-- Para una caja de la tienda el objetivo es un valor esperado de ~55-65% del precio (un sumidero de oro con premios, no un
-- negocio). La caja Mítica no se vende: no tiene precio contra el cual compararse.
-- chance_jackpot = probabilidad de que, en al menos una tirada, salga oro que paga más de lo que costó la caja.
-- =========================================================
WITH per_entry AS (
    SELECT l.box_item_id, l.weight, l.kind,
           (l.min_qty + l.max_qty) / 2.0 AS avg_qty,
           CASE WHEN l.kind = 'gold' THEN 1 ELSE it.sell_price END AS unit_value
    FROM box_loot l LEFT JOIN items it ON it.item_id = l.item_id
), totals AS (
    SELECT p.box_item_id,
           SUM(p.weight) AS total_weight,
           SUM(p.weight * p.avg_qty * p.unit_value) AS weighted_value,
           SUM(p.weight) FILTER (WHERE p.kind = 'gold' AND b.buy_price > 0 AND p.avg_qty >= b.buy_price) AS jackpot_weight
    FROM per_entry p JOIN items b ON b.item_id = p.box_item_id
    GROUP BY p.box_item_id
)
SELECT b.name AS caja, b.rarity, b.buy_price AS precio, bx.rolls AS tiradas,
       ROUND((bx.rolls * t.weighted_value / t.total_weight)::numeric) AS valor_esperado,
       CASE WHEN b.buy_price > 0 THEN ROUND((100.0 * bx.rolls * t.weighted_value / t.total_weight / b.buy_price)::numeric, 0) END AS pct_del_precio,
       CASE WHEN b.buy_price > 0 THEN ROUND((100.0 * (1 - power(1 - COALESCE(t.jackpot_weight, 0)::numeric / t.total_weight, bx.rolls)))::numeric, 1) END AS chance_jackpot_pct
FROM boxes bx
JOIN items b ON b.item_id = bx.box_item_id
JOIN totals t ON t.box_item_id = bx.box_item_id
ORDER BY CASE b.rarity WHEN 'Común' THEN 1 WHEN 'Raro' THEN 2 WHEN 'Épico' THEN 3 WHEN 'Legendario' THEN 4 ELSE 5 END;
