-- =========================================================
-- Asado y Acero RPG — CAJAS v2 (v0.8.0). Diseño: docs/superpowers/plans/2026-10-05-cajas-v2-y-cooldowns.md
--
-- Cada caja dice cuántos ítems trae (entre min_items y max_items; el oro no cuenta), cuesta MUCHO más y su botín sale con las MISMAS chances que
-- farmear:
--   · una tirada = un ítem; el botín se arma con 4 tipos de tirada (box_loot.kind):
--       gather    un material de recolección sorteado como /chop y /mine (rareza 68 / 21 / 7 / 3,5 y Mítico 0,1% por ítem; lo hace el código, no hay ítem);
--       zone_drop un drop de monstruo de una zona <= la de la caja y <= la máxima que el jugador tiene DESBLOQUEADA (también lo resuelve el código);
--       item      un ítem fijo: comida, un trofeo de colección o la caja de un escalón menos;
--       gold      NO es una tirada: es un bono raro (weight = chance en MILÉSIMOS por apertura) que no cuenta como ítem.
--     Para gather / zone_drop / item, weight es la parte de las tiradas (suman 1000 por caja).
--   · precios: lo que la caja te ahorra en minutos de farmeo x el oro por minuto de su zona x ~0,7 (la eficiencia real de un jugador): comprar nunca es más rápido
--     que farmear. Todas las cajas pasan a venta 0 (no se pueden revender: un cofre de jefe no puede ser una fuente de oro).
--   · una caja solo se COMPRA si ya desbloqueaste su zona (Común = Zona 1 ... Legendario = Zona 4; la Mítica es solo premio); lo valida el código.
--
-- Cantidad de ítems (ajustada por el dueño el 2026-10-05; los precios de la tabla de abajo también): Cajón de Pino 1-5 · Baúl de Roble 5-10 · Arcón de Hierro 10-25 ·
-- Cofre de Oro 25-60 · Arca del Soberano 60-100. La tabla rb_boxes de abajo es la fuente de verdad: si cambia, cambia ESA (y se re-corre el script).
-- Re-ejecutable (la segunda vez no cambia nada) y FALLA EN VOZ ALTA si algo no coincide. Va DESPUÉS de seed_boxes.sql y de rework_drops_and_recipes.sql.
-- IMPORTANTE para una base viva: correr ESTE script ANTES de arrancar el bot v0.8.0 (el código lee las columnas nuevas).
-- =========================================================

-- 0) Estructura: el rango de ítems y los dos tipos de tirada nuevos. "rolls" queda por compatibilidad (la v0.7 lo leía) y ya no se usa.
ALTER TABLE boxes ADD COLUMN IF NOT EXISTS min_items INTEGER;
ALTER TABLE boxes ADD COLUMN IF NOT EXISTS max_items INTEGER;

ALTER TABLE box_loot DROP CONSTRAINT IF EXISTS box_loot_kind_check;
ALTER TABLE box_loot DROP CONSTRAINT IF EXISTS box_loot_kind_item;
ALTER TABLE box_loot ADD CONSTRAINT box_loot_kind_check CHECK (kind IN ('gold', 'item', 'gather', 'zone_drop'));
ALTER TABLE box_loot ADD CONSTRAINT box_loot_kind_item CHECK (
    (kind IN ('gold', 'gather', 'zone_drop') AND item_id IS NULL) OR (kind = 'item' AND item_id IS NOT NULL));

-- 1) Rangos y precios.
CREATE TEMP TABLE rb_boxes (name TEXT, min_items INTEGER, max_items INTEGER, buy_price INTEGER);
INSERT INTO rb_boxes VALUES
    ('Cajón de Pino',      1,  5,   2300),
    ('Baúl de Roble',      5,  10,  14000),
    ('Arcón de Hierro',   10,  25,  42000),
    ('Cofre de Oro',      25,  60, 120000),
    ('Arca del Soberano', 60, 100,      0);

UPDATE boxes b
SET min_items = r.min_items, max_items = r.max_items, rolls = 1
FROM rb_boxes r JOIN items i ON i.name = r.name
WHERE b.box_item_id = i.item_id;

UPDATE items i
SET buy_price = r.buy_price, sell_price = 0
FROM rb_boxes r
WHERE i.name = r.name;

ALTER TABLE boxes DROP CONSTRAINT IF EXISTS boxes_items_range;
ALTER TABLE boxes ADD CONSTRAINT boxes_items_range CHECK (min_items >= 1 AND max_items >= min_items);

-- 2) El botín de cada caja (se borra y se vuelve a cargar: este archivo es la fuente de verdad).
CREATE TEMP TABLE rb_loot (box_name TEXT, kind TEXT, item_name TEXT, weight INTEGER, min_qty INTEGER, max_qty INTEGER);
INSERT INTO rb_loot VALUES
    -- Cajón de Pino (Zona 1): casi todo recolección
    ('Cajón de Pino', 'gather',    NULL,                          790, 1, 1),
    ('Cajón de Pino', 'zone_drop', NULL,                          120, 1, 1),
    ('Cajón de Pino', 'item',      'Mate Amargo',                  60, 1, 1),
    ('Cajón de Pino', 'item',      'Collar de Cuero Viejo',         6, 1, 1),
    ('Cajón de Pino', 'item',      'Cuero Curtido de Pradera',      6, 1, 1),
    ('Cajón de Pino', 'item',      'Pelaje Oscuro',                 6, 1, 1),
    ('Cajón de Pino', 'item',      'Piedra Caliente',               6, 1, 1),
    ('Cajón de Pino', 'item',      'Tela Rasgada',                  6, 1, 1),
    ('Cajón de Pino', 'gold',      NULL,                           30, 100, 300),
    -- Baúl de Roble (Zona 2)
    ('Baúl de Roble', 'gather',    NULL,                          720, 1, 1),
    ('Baúl de Roble', 'zone_drop', NULL,                          150, 1, 1),
    ('Baúl de Roble', 'item',      'Mate Amargo',                  28, 1, 1),
    ('Baúl de Roble', 'item',      'Empanada de Carne',            42, 1, 1),
    ('Baúl de Roble', 'item',      'Corona de Cerdas',             10, 1, 1),
    ('Baúl de Roble', 'item',      'Garra Maldita',                10, 1, 1),
    ('Baúl de Roble', 'item',      'Hueso Añejo',                  10, 1, 1),
    ('Baúl de Roble', 'item',      'Rama Carbonizada',             10, 1, 1),
    ('Baúl de Roble', 'item',      'Cajón de Pino',                20, 1, 1),
    ('Baúl de Roble', 'gold',      NULL,                           30, 1000, 3000),
    -- Arcón de Hierro (Zona 3)
    ('Arcón de Hierro', 'gather',    NULL,                        700, 1, 1),
    ('Arcón de Hierro', 'zone_drop', NULL,                        170, 1, 1),
    ('Arcón de Hierro', 'item',      'Empanada de Carne',          28, 1, 1),
    ('Arcón de Hierro', 'item',      'Asado de Tira',              42, 1, 1),
    ('Arcón de Hierro', 'item',      'Garra del Alfa',             13, 1, 1),
    ('Arcón de Hierro', 'item',      'Núcleo Ígneo',               13, 1, 1),
    ('Arcón de Hierro', 'item',      'Polvo de Mina Sagrada',      14, 1, 1),
    ('Arcón de Hierro', 'item',      'Baúl de Roble',              20, 1, 1),
    ('Arcón de Hierro', 'gold',      NULL,                         30, 3500, 10000),
    -- Cofre de Oro (Zona 4)
    ('Cofre de Oro', 'gather',    NULL,                           680, 1, 1),
    ('Cofre de Oro', 'zone_drop', NULL,                           190, 1, 1),
    ('Cofre de Oro', 'item',      'Asado de Tira',                 21, 1, 1),
    ('Cofre de Oro', 'item',      'Cordero Patagónico',            49, 1, 1),
    ('Cofre de Oro', 'item',      'Colmillo del Señor del Volcán', 13, 1, 1),
    ('Cofre de Oro', 'item',      'Martillo del Capataz',          13, 1, 1),
    ('Cofre de Oro', 'item',      'Roca Volcánica Pura',           14, 1, 1),
    ('Cofre de Oro', 'item',      'Arcón de Hierro',               20, 1, 1),
    ('Cofre de Oro', 'gold',      NULL,                            30, 11000, 33000),
    -- Arca del Soberano (solo premio de logros): lo único que da Corteza y Meteorito de a muchos, y banquetes
    ('Arca del Soberano', 'gather',    NULL,                      610, 1, 1),
    ('Arca del Soberano', 'zone_drop', NULL,                      150, 1, 1),
    ('Arca del Soberano', 'item',      'Corteza del Árbol de Vida',                 15, 1, 1),
    ('Arca del Soberano', 'item',      'Fragmento de Meteorito',                    15, 1, 1),
    ('Arca del Soberano', 'item',      'Gema de Zafiro',                            40, 1, 1),
    ('Arca del Soberano', 'item',      'Madera de Ébano',                           40, 1, 1),
    ('Arca del Soberano', 'item',      'Cofre de Oro',                              10, 1, 1),
    ('Arca del Soberano', 'item',      'Asado Completo del Domingo en Familia',     30, 1, 1),
    ('Arca del Soberano', 'item',      'Mate Dulce de la Abuela',                   30, 1, 1),
    ('Arca del Soberano', 'item',      'Corona de Escoria Viva',                    30, 1, 1),
    ('Arca del Soberano', 'item',      'Escoria Pura del Cráter',                   30, 1, 1),
    ('Arca del Soberano', 'gold',      NULL,                                       500, 2000, 6000);

DO $$
DECLARE
    v_missing TEXT;
BEGIN
    SELECT string_agg(DISTINCT n, ', ') INTO v_missing FROM (
        SELECT box_name AS n FROM rb_loot UNION SELECT item_name FROM rb_loot WHERE item_name IS NOT NULL
    ) names WHERE NOT EXISTS (SELECT 1 FROM items i WHERE i.name = names.n);
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'rework_boxes: faltan estos ítems en el catálogo: [%]. ¿Se corrieron antes seed_boxes.sql y los seeds de materiales?', v_missing;
    END IF;
END $$;

DELETE FROM box_loot WHERE box_item_id IN (SELECT item_id FROM items WHERE name IN (SELECT name FROM rb_boxes));

INSERT INTO box_loot (box_item_id, kind, item_id, weight, min_qty, max_qty)
SELECT b.item_id, l.kind, i.item_id, l.weight, l.min_qty, l.max_qty
FROM rb_loot l
JOIN items b ON b.name = l.box_name
LEFT JOIN items i ON i.name = l.item_name;

-- 3) Verificación: el estado final tiene que ser EXACTAMENTE el diseño.
DO $$
DECLARE
    v_bad TEXT;
BEGIN
    SELECT string_agg(r.name, ', ') INTO v_bad
    FROM rb_boxes r
    WHERE NOT EXISTS (SELECT 1 FROM boxes b JOIN items i ON i.item_id = b.box_item_id
                      WHERE i.name = r.name AND b.min_items = r.min_items AND b.max_items = r.max_items AND i.buy_price = r.buy_price AND i.sell_price = 0);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rework_boxes: estas cajas no quedaron con su rango, precio y venta 0: [%]', v_bad;
    END IF;

    -- Las tiradas (todo menos el oro) suman 1000 por caja, y cada caja tiene UNA entrada de recolección y UNA de drops de zona.
    SELECT string_agg(i.name, ', ') INTO v_bad
    FROM boxes b JOIN items i ON i.item_id = b.box_item_id
    WHERE (SELECT COALESCE(SUM(weight), 0) FROM box_loot l WHERE l.box_item_id = b.box_item_id AND l.kind <> 'gold') <> 1000
       OR (SELECT COUNT(*) FROM box_loot l WHERE l.box_item_id = b.box_item_id AND l.kind = 'gather') <> 1
       OR (SELECT COUNT(*) FROM box_loot l WHERE l.box_item_id = b.box_item_id AND l.kind = 'zone_drop') <> 1;
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rework_boxes: el botín de estas cajas no suma 1000 o le falta recolección / drops de zona: [%]', v_bad;
    END IF;

    -- Los trofeos de colección (materiales que ningún monstruo suelta) tienen que seguir saliendo de alguna caja: el logro Coleccionista los pide todos.
    SELECT string_agg(i.name, ', ') INTO v_bad
    FROM items i
    WHERE i.type = 'Material'
      AND NOT EXISTS (SELECT 1 FROM monster_drops d WHERE d.item_id = i.item_id)
      AND NOT EXISTS (SELECT 1 FROM box_loot l WHERE l.item_id = i.item_id);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rework_boxes: estos trofeos ya no salen de ninguna caja: [%]', v_bad;
    END IF;

    -- Ninguna caja de la tienda regala el Mítico fijo (Corteza / Meteorito): solo el Arca (las cajas compradas lo dan, con 0,1% por ítem, por la recolección).
    SELECT string_agg(DISTINCT bi.name, ', ') INTO v_bad
    FROM box_loot l JOIN items bi ON bi.item_id = l.box_item_id JOIN items i ON i.item_id = l.item_id
    WHERE i.name IN ('Corteza del Árbol de Vida', 'Fragmento de Meteorito') AND bi.name <> 'Arca del Soberano';
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rework_boxes: estas cajas dan Corteza o Meteorito como ítem fijo: [%]', v_bad;
    END IF;
END $$;

DROP TABLE rb_loot;
DROP TABLE rb_boxes;

-- Chequeo rápido: rango, precio y cómo se reparte cada caja.
-- SELECT i.name, b.min_items, b.max_items, i.buy_price,
--        (SELECT string_agg(l.kind || ':' || l.weight, ' ' ORDER BY l.kind) FROM box_loot l WHERE l.box_item_id = b.box_item_id AND l.item_id IS NULL) AS dinamicas
-- FROM boxes b JOIN items i ON i.item_id = b.box_item_id ORDER BY i.buy_price;
