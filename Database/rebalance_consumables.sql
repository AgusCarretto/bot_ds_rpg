-- =========================================================
-- Rebalance de precios de Consumibles: la carga inicial (seed_consumables_and_base_swords.sql)
-- escalaba el heal linealmente con la rareza pero el precio más rápido todavía, así que la
-- eficiencia (HP por oro) caía de ~3.75 (Común) a ~0.31 (Mítico) — un jugador racional nunca
-- iba a comprar nada más caro que el ítem Común más barato. Rebalanceado para que la eficiencia
-- quede pareja (~4.4-5.0 HP/oro) en toda la escala: lo caro sigue costando más y curando más de
-- un saque, pero deja de ser matemáticamente una mala compra.
--
-- Ejecutable las veces que haga falta (cada UPDATE es un upsert de campos existentes).
--
-- Solo hace falta correr esto en una base que YA corrió seed_consumables_and_base_swords.sql
-- con los números viejos. Una instalación nueva no lo necesita: ese seed ya quedó actualizado
-- con estos mismos valores balanceados.
-- =========================================================

UPDATE items SET stat_value = 15,  sell_price = 2,  buy_price = 3  WHERE name = 'Mate Amargo';
UPDATE items SET stat_value = 20,  sell_price = 3,  buy_price = 4  WHERE name = 'Pan Casero';

UPDATE items SET stat_value = 40,  sell_price = 7,  buy_price = 9  WHERE name = 'Empanada de Carne';
UPDATE items SET stat_value = 50,  sell_price = 8,  buy_price = 11 WHERE name = 'Choripán';

UPDATE items SET stat_value = 80,  sell_price = 14, buy_price = 18 WHERE name = 'Vacío al Disco';
UPDATE items SET stat_value = 100, sell_price = 17, buy_price = 22 WHERE name = 'Asado de Tira';

UPDATE items SET stat_value = 150, sell_price = 25, buy_price = 33 WHERE name = 'Cordero Patagónico';
UPDATE items SET stat_value = 180, sell_price = 31, buy_price = 40 WHERE name = 'Matambre Arrollado';

UPDATE items SET stat_value = 250, sell_price = 43, buy_price = 56 WHERE name = 'Asado Completo del Domingo en Familia';
UPDATE items SET stat_value = 300, sell_price = 52, buy_price = 67 WHERE name = 'Mate Dulce de la Abuela';

-- Chequeo rápido: la columna hp_por_oro debería quedar entre ~4.4 y ~5.0 en las 10 filas.
-- SELECT name, rarity, stat_value, buy_price, ROUND(stat_value::numeric / buy_price, 2) AS hp_por_oro
-- FROM items WHERE type = 'Consumable' ORDER BY buy_price;
