-- =========================================================
-- Saca los 3 Consumibles viejos, cargados a mano y nunca trackeados en ningún seed
-- ("Mate (Canarias Suave)", "Refuerzo de Milanesa", "Tira de Asado" — los del bug de precios
-- original: 130 oro por 100 HP cuando 10x el barato daban 250 HP por los mismos 130), ahora que
-- el catálogo de 10 en seed_consumables_and_base_swords.sql los reemplaza con precios balanceados.
--
-- OJO: el DELETE se propaga en cascada a "inventory" (item_id tiene ON DELETE CASCADE) — cualquier
-- cantidad que un jugador tuviera comprada de estos 3 ítems desaparece junto con ellos. Con un solo
-- usuario de prueba no debería haber pérdida real; confirmá antes de correrlo si ya hay más gente
-- jugando en tu server.
--
-- Ejecutar UNA SOLA VEZ, DESPUÉS de Database/seed_consumables_and_base_swords.sql (así el catálogo
-- nuevo ya está cargado cuando se van los 3 viejos y /shop view no queda vacío ni un segundo).
-- =========================================================

-- Chequeo antes de borrar, por si los nombres reales en tu base difieren un poco de los del
-- screenshot que compartiste (typos, espacios, etc.) — confirmá que las 3 filas de abajo son las
-- que pensás borrar antes de correr el DELETE.
-- SELECT item_id, name, rarity, stat_value, buy_price, sell_price FROM items
-- WHERE name IN ('Mate (Canarias Suave)', 'Refuerzo de Milanesa', 'Tira de Asado');

DELETE FROM items
WHERE name IN ('Mate (Canarias Suave)', 'Refuerzo de Milanesa', 'Tira de Asado');
