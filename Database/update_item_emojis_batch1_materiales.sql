-- =========================================================
-- Carga de emojis personalizados — Tanda 1: los 11 materiales de /chop y /mine (Madera + Mineral),
-- los primeros que se generaron como pixel art (ver prompts de Director de Arte).
--
-- Usa WHERE name = '...' en vez de WHERE item_id = X a propósito: el item_id es un IDENTITY que
-- puede valer distinto en cada instalación (ya nos pasó con este catálogo entre máquinas, ver
-- CLAUDE.md / MEJORAS.md), pero el nombre es estable. Ejecutable las veces que haga falta
-- (cada UPDATE es un upsert de un solo campo, no hay riesgo de duplicar filas).
--
-- Pegá acá el código de emoji personalizado de Discord tal cual lo da el server tras subirlo,
-- formato "<:nombre_emoji:1234567890>" (o "<a:nombre:id>" si es animado) — NO el emoji unicode.
-- =========================================================

UPDATE items SET emoji = '<:maderacomun:1545190666122960906>'    WHERE name = 'Madera de Pino';
UPDATE items SET emoji = '<:maderarara:1545190728970407998>'     WHERE name = 'Madera de Roble';
UPDATE items SET emoji = '<:maderaepica:1545190573957447700>'    WHERE name = 'Madera de Nogal';
UPDATE items SET emoji = '<:maderalegendaria:1545190639715745832>' WHERE name = 'Madera de Ébano';
UPDATE items SET emoji = '[EMOJI_PLACEHOLDER]' WHERE name = 'Corteza del Árbol de Vida';

UPDATE items SET emoji = '[EMOJI_PLACEHOLDER]' WHERE name = 'Piedra';
UPDATE items SET emoji = '[EMOJI_PLACEHOLDER]' WHERE name = 'Carbón';
UPDATE items SET emoji = '[EMOJI_PLACEHOLDER]' WHERE name = 'Hierro';
UPDATE items SET emoji = '[EMOJI_PLACEHOLDER]' WHERE name = 'Oro Puro';
UPDATE items SET emoji = '[EMOJI_PLACEHOLDER]' WHERE name = 'Gema de Zafiro';
UPDATE items SET emoji = '[EMOJI_PLACEHOLDER]' WHERE name = 'Fragmento de Meteorito';

-- Chequeo rápido después de pegar los códigos reales: confirma que las 11 filas quedaron con
-- un emoji que no sea el placeholder.
-- SELECT name, emoji FROM items WHERE name IN (
--   'Madera de Pino','Madera de Roble','Madera de Nogal','Madera de Ébano','Corteza del Árbol de Vida',
--   'Piedra','Carbón','Hierro','Oro Puro','Gema de Zafiro','Fragmento de Meteorito'
-- ) ORDER BY name;
