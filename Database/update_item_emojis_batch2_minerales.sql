-- =========================================================
-- Carga de emojis personalizados — Tanda 2: los 6 minerales de /mine + la última Madera que
-- faltaba (Corteza del Árbol de Vida, Mítico). Con esto, Madera + Mineral quedan 100% completos
-- (11 de 11).
--
-- Mismo patrón que update_item_emojis_batch1_materiales.sql: WHERE name = '...' (no item_id, que
-- varía entre instalaciones), ejecutable las veces que haga falta.
-- =========================================================

UPDATE items SET emoji = '<:MADERADELAVIDA_MITICO:1545413424970727516>' WHERE name = 'Corteza del Árbol de Vida';

UPDATE items SET emoji = '<:PIEDRA_COMUN2:1545413503408541726>'   WHERE name = 'Piedra';
UPDATE items SET emoji = '<:CARBON_COMUN:1545413367852826624>'    WHERE name = 'Carbón';
UPDATE items SET emoji = '<:HIERRO_RARO:1545413396793401465>'     WHERE name = 'Hierro';
UPDATE items SET emoji = '<:ORO_EPICO:1545413475462021160>'       WHERE name = 'Oro Puro';
UPDATE items SET emoji = '<:ZAFIRO_LEGENDARIO:1545413533657866280>' WHERE name = 'Gema de Zafiro';
UPDATE items SET emoji = '<:METEORITO_MITICO:1545413451487117412>'  WHERE name = 'Fragmento de Meteorito';

-- Chequeo rápido: Madera + Mineral (11 ítems) deberían quedar sin ningún emoji NULL ni placeholder.
-- SELECT name, type, rarity, emoji FROM items WHERE type IN ('Madera', 'Mineral') ORDER BY type, rarity;
