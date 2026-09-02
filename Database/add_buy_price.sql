-- =========================================================
-- Migración: separa el precio de compra del precio de venta en items.
-- Antes, /shop buy y /shop sell usaban la misma columna (sell_price), sin margen.
-- Ejecutar UNA SOLA VEZ contra la base ya creada (schema.sql ya la tiene
-- si es una instalación nueva).
-- =========================================================

ALTER TABLE items ADD COLUMN IF NOT EXISTS buy_price INTEGER;

-- Backfill: 30% de margen sobre el precio de venta actual (redondeado hacia arriba,
-- para que la compra quede siempre por encima de la venta incluso en precios bajos).
UPDATE items SET buy_price = CEIL(sell_price * 1.3)::int WHERE buy_price IS NULL;

ALTER TABLE items ALTER COLUMN buy_price SET NOT NULL;
ALTER TABLE items ALTER COLUMN buy_price SET DEFAULT 0;
ALTER TABLE items ADD CONSTRAINT chk_buy_price_not_below_sell CHECK (buy_price >= sell_price);
