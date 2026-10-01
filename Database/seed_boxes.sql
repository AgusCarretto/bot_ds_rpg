-- =========================================================
-- Asado y Acero RPG — Las cajas: 5 tiers (uno por rareza) con su botín. Cuatro se venden en /shop y la Mítica es solo premio
-- (misiones, logros, torneo). Es la FUENTE DE VERDAD del botín: re-ejecutable (borra y recarga el botín de estas cajas) y se
-- verifica solo — FALLA en voz alta si falta una caja o un ítem de los que nombra.
--
-- POR QUÉ EXISTEN: el oro sobra (/travel paga x10), así que hacía falta algo en qué gastarlo que además sea divertido. Una caja
-- es una apuesta: la mayoría de las veces da un poco de oro y materiales, y de vez en cuando un premio grande.
--
-- LO QUE NO DAN (a propósito): los materiales de drop de la zona que se usan en las recetas (Garra de Puma, Esencia
-- Espectral, Escama Ígnea...). Si una caja de 1.000 de oro diera esos, comprarlas sería ~5 veces más rápido que farmear los
-- drops y se rompe el ritmo que se calibró a propósito (ver report_recipe_pacing.sql). El botín es:
--   · oro (con un "jackpot" raro),
--   · materiales de recolección (Pino, Piedra, Roble, Carbón, Hierro, Nogal, Oro Puro, Ébano, Zafiro): el Hierro, que era el
--     cuello de botella, aparece desde la caja más barata,
--   · los 17 "trofeos": materiales de monstruo que ya no suelta ningún monstruo (Pelaje Oscuro, Garra Maldita, Núcleo Ígneo...);
--     se venden y cuentan para el logro de coleccionista,
--   · comida, y cajas de menor tier,
--   · y la caja Mítica (solo premio) es la ÚNICA que da lo larguísimo (Corteza del Árbol de Vida, Fragmento de Meteorito), así
--     que no hay forma de comprar con oro esos objetivos de largo plazo.
--
-- CALIBRACIÓN: el valor esperado (oro + lo que valdrían los ítems vendidos) de las cajas de la tienda ronda el 55-67% de su
-- precio — es un sumidero de oro, no un negocio — y lo calcula Database/report_box_economy.sql. Si se tocan pesos o precios,
-- volver a correrlo. Los precios de las cajas de abajo se pensaron contra el oro por cacería de la zona de cada tier
-- (Z1 ~14, Z2 ~58, Z3 ~118, Z4 ~215, Z5 ~375): ~10-15 cacerías de oro.
--
-- Ejecutar DESPUÉS de add_boxes.sql (o schema.sql) y de los seeds que crean los ítems que nombra (seed.sql, zonas y jefes,
-- comida). Los nombres de abajo son los de la base real.
-- =========================================================

-- Las cajas como ítems (upsert por nombre). buy_price 0 = no se vende en la tienda; sell_price 0 = no se puede vender.
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price) VALUES
('Cajón de Pino',      'Caja', 'Común',      0,   60,  150),
('Baúl de Roble',      'Caja', 'Raro',       0,  280,  700),
('Arcón de Hierro',    'Caja', 'Épico',      0,  720, 1800),
('Cofre de Oro',       'Caja', 'Legendario', 0, 1400, 3500),
('Arca del Soberano',  'Caja', 'Mítico',     0,    0,    0)
ON CONFLICT (name) DO UPDATE SET
    type = EXCLUDED.type, rarity = EXCLUDED.rarity, stat_value = EXCLUDED.stat_value,
    sell_price = EXCLUDED.sell_price, buy_price = EXCLUDED.buy_price;

CREATE TEMP TABLE box_defs (box_name TEXT, rolls INTEGER);
INSERT INTO box_defs VALUES
('Cajón de Pino',     2),
('Baúl de Roble',     3),
('Arcón de Hierro',   3),
('Cofre de Oro',      4),
('Arca del Soberano', 5);

-- kind 'gold' = oro (el ítem va NULL); kind 'item' = un ítem por nombre. weight: la chance de cada entrada es su peso sobre
-- la suma de pesos de la caja. Los de oro con rango grande y peso chico son los "jackpot".
CREATE TEMP TABLE box_entries (box_name TEXT, kind TEXT, item_name TEXT, weight INTEGER, min_qty INTEGER, max_qty INTEGER);
INSERT INTO box_entries VALUES
-- Cajón de Pino (Común): lo básico, con el Hierro como anzuelo y un jackpot chico
('Cajón de Pino', 'gold', NULL, 36,  40,  90),
('Cajón de Pino', 'gold', NULL,  3, 160, 300),
('Cajón de Pino', 'item', 'Madera de Pino',          12, 3, 6),
('Cajón de Pino', 'item', 'Piedra',                  12, 3, 6),
('Cajón de Pino', 'item', 'Hierro',                   3, 1, 1),
('Cajón de Pino', 'item', 'Mate Amargo',              6, 1, 1),
('Cajón de Pino', 'item', 'Collar de Cuero Viejo',    2, 1, 1),
('Cajón de Pino', 'item', 'Cuero Curtido de Pradera', 2, 1, 1),
('Cajón de Pino', 'item', 'Pelaje Oscuro',            2, 1, 1),
('Cajón de Pino', 'item', 'Piedra Caliente',          2, 1, 1),
('Cajón de Pino', 'item', 'Tela Rasgada',             2, 1, 1),
-- Baúl de Roble (Raro): materiales de zona 2, comida y la caja de abajo
('Baúl de Roble', 'gold', NULL, 34,  120,  260),
('Baúl de Roble', 'gold', NULL,  3,  700, 1200),
('Baúl de Roble', 'item', 'Madera de Roble',  10, 2, 4),
('Baúl de Roble', 'item', 'Carbón',            8, 2, 4),
('Baúl de Roble', 'item', 'Hierro',            8, 2, 4),
('Baúl de Roble', 'item', 'Empanada de Carne', 6, 1, 1),
('Baúl de Roble', 'item', 'Cajón de Pino',     6, 1, 1),
('Baúl de Roble', 'item', 'Corona de Cerdas',  2, 1, 1),
('Baúl de Roble', 'item', 'Garra Maldita',     2, 1, 1),
('Baúl de Roble', 'item', 'Hueso Añejo',       2, 1, 1),
('Baúl de Roble', 'item', 'Rama Carbonizada',  2, 1, 1),
-- Arcón de Hierro (Épico)
('Arcón de Hierro', 'gold', NULL, 34,  300,  700),
('Arcón de Hierro', 'gold', NULL,  3, 1800, 3200),
('Arcón de Hierro', 'item', 'Hierro',               8, 3, 6),
('Arcón de Hierro', 'item', 'Madera de Nogal',      8, 2, 4),
('Arcón de Hierro', 'item', 'Oro Puro',             6, 2, 3),
('Arcón de Hierro', 'item', 'Asado de Tira',        5, 1, 1),
('Arcón de Hierro', 'item', 'Baúl de Roble',        6, 1, 1),
('Arcón de Hierro', 'item', 'Núcleo Ígneo',         2, 1, 1),
('Arcón de Hierro', 'item', 'Garra del Alfa',       2, 1, 1),
('Arcón de Hierro', 'item', 'Polvo de Mina Sagrada',2, 1, 1),
-- Cofre de Oro (Legendario)
('Cofre de Oro', 'gold', NULL, 34,  380,  850),
('Cofre de Oro', 'gold', NULL,  3, 3200, 5200),
('Cofre de Oro', 'item', 'Oro Puro',                      7, 3, 5),
('Cofre de Oro', 'item', 'Madera de Ébano',               5, 1, 1),
('Cofre de Oro', 'item', 'Gema de Zafiro',                5, 1, 1),
('Cofre de Oro', 'item', 'Cordero Patagónico',            5, 1, 1),
('Cofre de Oro', 'item', 'Arcón de Hierro',               6, 1, 1),
('Cofre de Oro', 'item', 'Martillo del Capataz',          2, 1, 1),
('Cofre de Oro', 'item', 'Roca Volcánica Pura',           2, 1, 1),
('Cofre de Oro', 'item', 'Colmillo del Señor del Volcán', 2, 1, 1),
-- Arca del Soberano (Mítico, solo premio): lo único que da los objetivos de largo plazo
('Arca del Soberano', 'gold', NULL, 30, 1500, 3500),
('Arca del Soberano', 'item', 'Cofre de Oro',              10, 1, 1),
('Arca del Soberano', 'item', 'Madera de Ébano',            8, 2, 3),
('Arca del Soberano', 'item', 'Gema de Zafiro',             8, 2, 3),
('Arca del Soberano', 'item', 'Escoria Pura del Cráter',    5, 1, 1),
('Arca del Soberano', 'item', 'Corona de Escoria Viva',     5, 1, 1),
('Arca del Soberano', 'item', 'Corteza del Árbol de Vida',  2, 1, 1),
('Arca del Soberano', 'item', 'Fragmento de Meteorito',     2, 1, 1),
('Arca del Soberano', 'item', 'Asado Completo del Domingo en Familia', 5, 1, 1),
('Arca del Soberano', 'item', 'Mate Dulce de la Abuela',    5, 1, 1);

-- Cargar: las cajas, y su botín desde cero (este archivo es la fuente de verdad).
INSERT INTO boxes (box_item_id, rolls)
SELECT i.item_id, d.rolls FROM box_defs d JOIN items i ON i.name = d.box_name
ON CONFLICT (box_item_id) DO UPDATE SET rolls = EXCLUDED.rolls;

DELETE FROM box_loot WHERE box_item_id IN (SELECT i.item_id FROM box_defs d JOIN items i ON i.name = d.box_name);

INSERT INTO box_loot (box_item_id, kind, item_id, weight, min_qty, max_qty)
SELECT b.item_id, e.kind, it.item_id, e.weight, e.min_qty, e.max_qty
FROM box_entries e
JOIN items b ON b.name = e.box_name
LEFT JOIN items it ON it.name = e.item_name;

-- Verificación: todo lo de arriba TIENE que haberse cargado (los JOIN omiten en silencio lo que no existe).
DO $$
DECLARE
    missing_boxes TEXT;
    missing_items TEXT;
    loaded        INTEGER;
    expected      INTEGER;
BEGIN
    SELECT string_agg(d.box_name, ', ') INTO missing_boxes
    FROM box_defs d WHERE NOT EXISTS (SELECT 1 FROM items i JOIN boxes b ON b.box_item_id = i.item_id WHERE i.name = d.box_name);

    SELECT string_agg(DISTINCT e.item_name, ', ') INTO missing_items
    FROM box_entries e WHERE e.kind = 'item' AND NOT EXISTS (SELECT 1 FROM items i WHERE i.name = e.item_name);

    SELECT COUNT(*) INTO loaded FROM box_loot WHERE box_item_id IN (SELECT i.item_id FROM box_defs d JOIN items i ON i.name = d.box_name);
    SELECT COUNT(*) INTO expected FROM box_entries;

    IF missing_boxes IS NOT NULL OR missing_items IS NOT NULL OR loaded <> expected THEN
        RAISE EXCEPTION 'seed_boxes: no se pudo cargar el botín (revisar el orden de los scripts). Cajas que faltan: [%]. Ítems que faltan: [%]. Cargadas % de % entradas.',
            COALESCE(missing_boxes, '-'), COALESCE(missing_items, '-'), loaded, expected;
    END IF;
END $$;

DROP TABLE box_entries;
DROP TABLE box_defs;

-- Chequeo rápido: Database/report_box_economy.sql (valor esperado de cada caja contra su precio).
