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
-- AJUSTE DE SUERTE (2026-10-01, a pedido del dueño: "salía algo muy bueno demasiado seguido"): se recortó a la mitad la chance de lo
-- mejor de cada caja (jackpots de oro, materiales Legendarios/Épicos, trofeos) y se subió el peso del oro común. Medido con una
-- simulación de 100.000 aperturas por caja: el Cofre de Oro trae algo Legendario en ~36% de las aperturas (antes ~64%), el Arcón
-- de Hierro algo Épico en ~50% (antes ~70%), y devolver MÁS de lo que costó la caja pasó de 11-16% a 6-9%.
--
-- CALIBRACIÓN: el valor esperado (oro + lo que valdrían los ítems vendidos) de las cajas de la tienda ronda el 52-60% de su
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
('Cajón de Pino', 'gold', NULL, 80,  40,  90),
('Cajón de Pino', 'gold', NULL,  3, 160, 300),
('Cajón de Pino', 'item', 'Madera de Pino',          26, 3, 6),
('Cajón de Pino', 'item', 'Piedra',                  26, 3, 6),
('Cajón de Pino', 'item', 'Hierro',                   3, 1, 1),
('Cajón de Pino', 'item', 'Mate Amargo',             12, 1, 1),
('Cajón de Pino', 'item', 'Collar de Cuero Viejo',    3, 1, 1),
('Cajón de Pino', 'item', 'Cuero Curtido de Pradera', 3, 1, 1),
('Cajón de Pino', 'item', 'Pelaje Oscuro',            3, 1, 1),
('Cajón de Pino', 'item', 'Piedra Caliente',          3, 1, 1),
('Cajón de Pino', 'item', 'Tela Rasgada',             3, 1, 1),
-- Baúl de Roble (Raro): materiales de zona 2, comida y la caja de abajo
('Baúl de Roble', 'gold', NULL, 80,  110,  240),
('Baúl de Roble', 'gold', NULL,  3,  700, 1100),
('Baúl de Roble', 'item', 'Madera de Roble',  14, 2, 4),
('Baúl de Roble', 'item', 'Carbón',           10, 2, 4),
('Baúl de Roble', 'item', 'Hierro',           10, 2, 4),
('Baúl de Roble', 'item', 'Empanada de Carne', 8, 1, 1),
('Baúl de Roble', 'item', 'Cajón de Pino',    14, 1, 1),
('Baúl de Roble', 'item', 'Corona de Cerdas',  3, 1, 1),
('Baúl de Roble', 'item', 'Garra Maldita',     3, 1, 1),
('Baúl de Roble', 'item', 'Hueso Añejo',       3, 1, 1),
('Baúl de Roble', 'item', 'Rama Carbonizada',  3, 1, 1),
-- Arcón de Hierro (Épico)
('Arcón de Hierro', 'gold', NULL, 86,  260,  580),
('Arcón de Hierro', 'gold', NULL,  3, 1800, 3000),
('Arcón de Hierro', 'item', 'Hierro',                12, 3, 6),
('Arcón de Hierro', 'item', 'Madera de Nogal',        9, 2, 4),
('Arcón de Hierro', 'item', 'Oro Puro',               6, 2, 3),
('Arcón de Hierro', 'item', 'Asado de Tira',          6, 1, 1),
('Arcón de Hierro', 'item', 'Baúl de Roble',         16, 1, 1),
('Arcón de Hierro', 'item', 'Núcleo Ígneo',           3, 1, 1),
('Arcón de Hierro', 'item', 'Garra del Alfa',         3, 1, 1),
('Arcón de Hierro', 'item', 'Polvo de Mina Sagrada',  3, 1, 1),
-- Cofre de Oro (Legendario)
('Cofre de Oro', 'gold', NULL, 84,  320,  700),
('Cofre de Oro', 'gold', NULL,  3, 3200, 5000),
('Cofre de Oro', 'item', 'Oro Puro',                      10, 3, 5),
('Cofre de Oro', 'item', 'Madera de Ébano',                4, 1, 1),
('Cofre de Oro', 'item', 'Gema de Zafiro',                 4, 1, 1),
('Cofre de Oro', 'item', 'Cordero Patagónico',             8, 1, 1),
('Cofre de Oro', 'item', 'Arcón de Hierro',               14, 1, 1),
('Cofre de Oro', 'item', 'Martillo del Capataz',           2, 1, 1),
('Cofre de Oro', 'item', 'Roca Volcánica Pura',            2, 1, 1),
('Cofre de Oro', 'item', 'Colmillo del Señor del Volcán',  2, 1, 1),
-- Arca del Soberano (Mítico, solo premio): lo único que da los objetivos de largo plazo
('Arca del Soberano', 'gold', NULL, 80, 1500, 3500),
('Arca del Soberano', 'item', 'Cofre de Oro',              14, 1, 1),
('Arca del Soberano', 'item', 'Madera de Ébano',           12, 2, 3),
('Arca del Soberano', 'item', 'Gema de Zafiro',            12, 2, 3),
('Arca del Soberano', 'item', 'Escoria Pura del Cráter',    8, 1, 1),
('Arca del Soberano', 'item', 'Corona de Escoria Viva',     8, 1, 1),
('Arca del Soberano', 'item', 'Corteza del Árbol de Vida',  3, 1, 1),
('Arca del Soberano', 'item', 'Fragmento de Meteorito',     3, 1, 1),
('Arca del Soberano', 'item', 'Asado Completo del Domingo en Familia', 8, 1, 1),
('Arca del Soberano', 'item', 'Mate Dulce de la Abuela',    8, 1, 1);

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
