-- =========================================================
-- Fix de una sola vez: separa el type genérico 'Material' que ya insertaste
-- en 'Madera' (botín de /chop) y 'Mineral' (botín de /mine), para poder
-- filtrar el catálogo de ítems por tipo de recolección.
-- Ejecutar UNA SOLA VEZ contra la base que ya corrió el seed.sql anterior.
-- =========================================================

UPDATE items SET type = 'Madera'
WHERE name IN ('Madera de Pino', 'Madera de Roble', 'Madera de Nogal', 'Madera de Ébano', 'Corteza del Árbol de Vida');

UPDATE items SET type = 'Mineral'
WHERE name IN ('Piedra', 'Hierro', 'Oro Puro', 'Gema de Zafiro', 'Fragmento de Meteorito');
