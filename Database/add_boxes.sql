-- =========================================================
-- Migración para bases YA EXISTENTES (schema.sql ya la incluye para instalaciones nuevas): las cajas.
--
-- Una caja es un ítem más (items.type = 'Caja', con su rareza y su precio) que se abre con /abrir. Lo que da no está en el
-- código: está acá, en la base, para poder retocar el botín sin tocar nada del bot.
--
--   boxes     una fila por caja: cuántas tiradas hace al abrirla (rolls).
--   box_loot  lo que puede salir en cada tirada, con su peso: 'gold' (oro, entre min_qty y max_qty) o 'item' (un ítem del
--             catálogo — material, comida o OTRA caja de menor tier —, entre min_qty y max_qty unidades). La chance de cada
--             entrada es su peso sobre la suma de pesos de esa caja. Las cargan Database/seed_boxes.sql.
--
-- Una caja se VENDE en la tienda si su items.buy_price es mayor que 0; las que son solo premio (misiones, logros, torneo)
-- tienen buy_price 0.
-- Solo agrega tablas: el bot viejo las ignora. Ejecutar UNA vez, ANTES de Database/seed_boxes.sql. Re-ejecutable igual.
-- =========================================================

CREATE TABLE IF NOT EXISTS boxes (
    box_item_id INTEGER PRIMARY KEY REFERENCES items (item_id) ON DELETE CASCADE,
    rolls       INTEGER NOT NULL CHECK (rolls BETWEEN 1 AND 10)
);

CREATE TABLE IF NOT EXISTS box_loot (
    loot_id     INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    box_item_id INTEGER NOT NULL REFERENCES boxes (box_item_id) ON DELETE CASCADE,
    kind        TEXT NOT NULL CHECK (kind IN ('gold', 'item')),
    item_id     INTEGER REFERENCES items (item_id) ON DELETE CASCADE,   -- NULL si es oro
    weight      INTEGER NOT NULL CHECK (weight > 0),
    min_qty     INTEGER NOT NULL CHECK (min_qty >= 1),
    max_qty     INTEGER NOT NULL CHECK (max_qty >= min_qty),
    CONSTRAINT box_loot_kind_item CHECK ((kind = 'gold' AND item_id IS NULL) OR (kind = 'item' AND item_id IS NOT NULL))
);

-- Una entrada por caja y por ítem (y una sola de oro por caja y rango no se repite: el rango distingue "común" de "jackpot").
CREATE UNIQUE INDEX IF NOT EXISTS ux_box_loot_item ON box_loot (box_item_id, item_id) WHERE kind = 'item';
CREATE UNIQUE INDEX IF NOT EXISTS ux_box_loot_gold ON box_loot (box_item_id, min_qty, max_qty) WHERE kind = 'gold';
CREATE INDEX IF NOT EXISTS idx_box_loot_box ON box_loot (box_item_id);
