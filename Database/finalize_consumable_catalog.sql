-- =========================================================
-- Ajusta la escala de rareza de Consumibles a como quedó definida en el pase de arte (auras):
-- ya no hay Legendario en esta categoría, Raro queda solo para la Empanada, y Épico absorbe a
-- Choripán (antes Raro) y Cordero Patagónico (antes Legendario). Saca del catálogo el único
-- ítem que quedó sin lugar en la nueva escala.
--
-- OJO: stat_value/sell_price/buy_price NO se tocan acá — Choripán y Cordero Patagónico quedan
-- con los precios que tenían en su rareza vieja (Raro/Legendario), no los de Épico. Si querés que
-- también sigan la progresión de precio de Épico (ver Database/rebalance_consumables.sql para el
-- criterio de HP/oro parejo), avisame y te armo ese ajuste aparte.
--
-- Ejecutar UNA SOLA VEZ.
-- =========================================================

UPDATE items SET rarity = 'Épico' WHERE name = 'Choripán';
UPDATE items SET rarity = 'Épico' WHERE name = 'Cordero Patagónico';

-- "Matambre Arrollado" era el único Consumible Legendario — sin ese escalón ya no encaja en la
-- progresión (Común x2, Raro x1, Épico x4, Mítico x2). No tiene receta ni drop que lo referencie.
DELETE FROM items WHERE name = 'Matambre Arrollado';

-- Chequeo rápido: la escala de Consumibles debería quedar Común=2, Raro=1, Épico=4, Mítico=2 (9 total).
-- SELECT rarity, COUNT(*) FROM items WHERE type = 'Consumable' GROUP BY rarity ORDER BY rarity;
