-- =========================================================
-- Migración: columna para el emoji personalizado de cada ítem (pixel art recortado como
-- Emoji de Discord), usada en /inventory, /shop, /forge recipes, logs de /hunt y /mine, etc.
-- Ejecutar UNA SOLA VEZ contra la base ya creada (schema.sql ya la tiene si es una
-- instalación nueva).
-- =========================================================

ALTER TABLE items ADD COLUMN IF NOT EXISTS emoji VARCHAR(100);

-- NULL por defecto: el código (GameData/ItemDisplay.cs) cae a mostrar solo el nombre del ítem
-- si todavía no le cargaste un emoji, así no hace falta backfillear los 58 de una para no romper
-- nada en producción mientras vas subiendo los pixel art de a tandas.
