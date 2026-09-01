-- =========================================================
-- Asado y Acero RPG — Datos iniciales de materiales de farmeo
-- Ejecutar UNA SOLA VEZ, después de schema.sql (no es idempotente:
-- correrlo de nuevo duplica las filas).
-- =========================================================
-- Un material por rareza como punto de partida para los drops de /travel.

INSERT INTO items (name, type, rarity, stat_value, sell_price) VALUES
-- 🪵 Botín de Talar (/chop) — type = 'Madera' para poder filtrar por tipo de recolección
('Madera de Pino',           'Madera', 'Común',      0, 2),
('Madera de Roble',          'Madera', 'Raro',       0, 10),
('Madera de Nogal',          'Madera', 'Épico',      0, 30),
('Madera de Ébano',          'Madera', 'Legendario', 0, 100),
('Corteza del Árbol de Vida','Madera', 'Mítico',     0, 500),

-- ⛏️ Botín de Minería (/mine) — type = 'Mineral' para poder filtrar por tipo de recolección
('Piedra',                   'Mineral', 'Común',      0, 2),
('Hierro',                   'Mineral', 'Raro',       0, 10),
('Oro Puro',                 'Mineral', 'Épico',      0, 30),
('Gema de Zafiro',           'Mineral', 'Legendario', 0, 100),
('Fragmento de Meteorito',   'Mineral', 'Mítico',     0, 500)
ON CONFLICT DO NOTHING;