-- =========================================================
-- Asado y Acero RPG — Recetas del herrero de ZONA 2 (Bosque de Cenizas), con el molde de cada zona:
-- 4 armas de AFINIDAD (1 por clase) + 2 armas GENERALES + 2 amuletos (siempre generales) = 8 recetas.
--
-- ESCALERA DE ZONAS: el equipo de cada zona es un SALTO sobre el de la anterior (x1.6 por zona), y los monstruos
-- están calibrados para que al entrar con el equipo viejo cueste y con el propio se vuelva cómodo — sin ser un
-- paseo (ver los comentarios de la escalera en seed_zones_and_monsters.sql / seed_zone_bosses.sql y MEJORAS.md).
-- Stats de la escalera (arma de afinidad · generales bajo/alto · amuletos bajo/alto):
--
--     Zona 1: +5 (inicial) / +15 general (Hoja)  · +10/+15   · +10/+16
--     Zona 2: +20                                · +18/+26   · +18/+24    <- ESTE archivo
--     Zona 3: +32 · +28/+40 · +30/+38      Zona 4: +50 · +44/+66 · +46/+60      Zona 5: +80 · +70/+105 · +75/+95
--
-- (Son MENOS que los primeros números que se habían puesto — +35/+55/+72/+85 —: con esos, al salir de cada zona
-- la pelea común costaba 2-8% de la vida, un paseo; se bajaron y los monstruos se recalcularon encima.)
-- Las 4 armas de afinidad son las Épicas que YA existían como ítems (una por familia: Hacha de Hierro MK3
-- Espadas/Guerrero, Colmillo Nocturno Dagas/Ninja, Arco Élfico Ancestral Arcos/Arquero, Grimorio de las
-- Tormentas Grimorios/Hechicero) — no tenían receta ni este stat (eran +35). Las 2 generales y 1 amuleto son
-- ítems NUEVOS, sin familia (nadie tiene sinergia con ellas). Materiales: drops de la zona (~7.5% por cacería
-- cada uno, ~13 cacerías por unidad), Madera de Roble (25% por /chop), Hierro/Carbón (12.5% por /mine cada uno)
-- y, en las dos mejores, un drop del jefe de la zona (Lobisón Alfa). Ojo: los bonos de drop y de cantidad del
-- reset que se planean (se desbloquea al terminar la Zona 5) van a acelerar todo esto; se calibra contra las
-- tasas BASE de la run 1.
--
-- Ejecutar DESPUÉS de seed_zone_bosses.sql (drops del jefe) y seed_recipes.sql (Zona 1: que ya no incluye el
-- Hacha — este archivo la pasa a Zona 2). Re-ejecutable, y FALLA EN VOZ ALTA si falta un ítem o una zona.
-- =========================================================

-- Re-stat de lo que YA existía: las 4 Épicas de afinidad (eran +35, y valían 500 — el Hacha 60 —, un precio
-- arbitrario que dejaba forjar y revender con ganancia o regalado) y el Mate Tallado en Cenizas (era +28).
-- Precio de venta = 6 x el stat (siempre por debajo del costo de forja), compra = 1.3 x la venta.
UPDATE items SET rarity = 'Épico', stat_value = 20, sell_price = 120, buy_price = 156
WHERE name IN ('Hacha de Hierro MK3', 'Colmillo Nocturno', 'Arco Élfico Ancestral', 'Grimorio de las Tormentas');
UPDATE items SET rarity = 'Épico', stat_value = 18, sell_price = 108, buy_price = 140
WHERE name = 'Mate Tallado en Cenizas';

-- Ítems nuevos (upsert por nombre): 2 armas generales sin familia + 1 amuleto.
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price) VALUES
('Cuchilla de Cenizas',        'Weapon', 'Épico', 18, 108, 140),
('Alabarda del Alfa',          'Weapon', 'Épico', 26, 156, 203),
('Talismán de Ceniza Bendita', 'Amulet', 'Épico', 24, 144, 187)
ON CONFLICT (name) DO UPDATE SET
    type = EXCLUDED.type, rarity = EXCLUDED.rarity, stat_value = EXCLUDED.stat_value,
    sell_price = EXCLUDED.sell_price, buy_price = EXCLUDED.buy_price;

CREATE TEMP TABLE z2_recipes (result_name TEXT, gold INTEGER, affinity BOOLEAN);
INSERT INTO z2_recipes VALUES
-- Armas de afinidad (Épicas +20)
('Hacha de Hierro MK3',          400, true),   -- Guerrero (Espadas)
('Colmillo Nocturno',            400, true),   -- Ninja (Dagas)
('Arco Élfico Ancestral',        400, true),   -- Arquero (Arcos)
('Grimorio de las Tormentas',    400, true),   -- Hechicero (Grimorios)
-- Armas generales
('Cuchilla de Cenizas',          300, false),
('Alabarda del Alfa',            450, false),
-- Amuletos (generales)
('Mate Tallado en Cenizas',      300, false),
('Talismán de Ceniza Bendita',   400, false);

CREATE TEMP TABLE z2_ingredients (result_name TEXT, ingredient_name TEXT, quantity INTEGER);
INSERT INTO z2_ingredients VALUES
-- Hacha de Hierro MK3: la hoja de Hierro, el mango de Roble y garras del bosque
('Hacha de Hierro MK3',          'Hierro',                    5),
('Hacha de Hierro MK3',          'Garra de Puma Cenizo',      2),
('Hacha de Hierro MK3',          'Madera de Roble',           2),
-- Colmillo Nocturno: Hierro y esencia espectral para el filo que no se ve
('Colmillo Nocturno',            'Hierro',                    4),
('Colmillo Nocturno',            'Esencia Espectral',         2),
('Colmillo Nocturno',            'Garra de Puma Cenizo',      1),
-- Arco Élfico Ancestral: la madera noble, ramas quemadas y un hilo espectral
('Arco Élfico Ancestral',        'Madera de Roble',           4),
('Arco Élfico Ancestral',        'Rama Carbonizada',          2),
('Arco Élfico Ancestral',        'Esencia Espectral',         1),
-- Grimorio de las Tormentas: tapas de Roble, ceniza bendita como tinta y esencia espectral
('Grimorio de las Tormentas',    'Madera de Roble',           3),
('Grimorio de las Tormentas',    'Ceniza Bendita',            2),
('Grimorio de las Tormentas',    'Esencia Espectral',         2),
-- Cuchilla de Cenizas (general +30): mucho Hierro y Carbón, sin drop de jefe
('Cuchilla de Cenizas',          'Hierro',                    3),
('Cuchilla de Cenizas',          'Carbón',                    2),
('Cuchilla de Cenizas',          'Rama Carbonizada',          1),
-- Alabarda del Alfa (general +45): la garra del jefe (Lobisón Alfa) + garras del bosque
('Alabarda del Alfa',            'Hierro',                    4),
('Alabarda del Alfa',            'Garra del Alfa',            1),
('Alabarda del Alfa',            'Garra de Puma Cenizo',      2),
-- Mate Tallado en Cenizas (amuleto +28): Carbón, ceniza bendita y Roble
('Mate Tallado en Cenizas',      'Carbón',                    3),
('Mate Tallado en Cenizas',      'Ceniza Bendita',            3),
('Mate Tallado en Cenizas',      'Madera de Roble',           2),
-- Talismán de Ceniza Bendita (amuleto +35): el pelaje plateado del jefe + ceniza bendita + Oro Puro
('Talismán de Ceniza Bendita',   'Pelaje Plateado del Alfa',  1),
('Talismán de Ceniza Bendita',   'Ceniza Bendita',            3),
('Talismán de Ceniza Bendita',   'Oro Puro',                  1);

INSERT INTO recipes (result_item_id, gold_cost, zone_id, affinity)
SELECT i.item_id, r.gold, z.zone_id, r.affinity
FROM z2_recipes r
JOIN items i ON i.name = r.result_name
CROSS JOIN (SELECT zone_id FROM zones WHERE name = 'Bosque de Cenizas') z
ON CONFLICT (result_item_id) DO UPDATE SET
    gold_cost = EXCLUDED.gold_cost, zone_id = EXCLUDED.zone_id, affinity = EXCLUDED.affinity;

-- Los ingredientes de estas recetas los define ESTE archivo (el Hacha traía los de Zona 1: 5 Hierro + 3 Cuero
-- Grueso, que hay que sacar): se borran los que tuvieran y se cargan de nuevo.
DELETE FROM recipe_ingredients
WHERE recipe_id IN (
    SELECT rec.recipe_id FROM z2_recipes r
    JOIN items i ON i.name = r.result_name
    JOIN recipes rec ON rec.result_item_id = i.item_id);

INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT rec.recipe_id, ing.item_id, a.quantity
FROM z2_ingredients a
JOIN items res ON res.name = a.result_name
JOIN recipes rec ON rec.result_item_id = res.item_id
JOIN items ing ON ing.name = a.ingredient_name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- Verificación: todo lo de arriba TIENE que haberse cargado (los JOIN omiten en silencio lo que no existe).
DO $$
DECLARE
    missing_recipes     TEXT;
    missing_ingredients TEXT;
BEGIN
    SELECT string_agg(r.result_name, ', ') INTO missing_recipes
    FROM z2_recipes r
    WHERE NOT EXISTS (
        SELECT 1 FROM recipes rec JOIN items i ON i.item_id = rec.result_item_id
        JOIN zones z ON z.zone_id = rec.zone_id
        WHERE i.name = r.result_name AND z.name = 'Bosque de Cenizas'
          AND rec.gold_cost = r.gold AND rec.affinity = r.affinity);

    SELECT string_agg(a.result_name || ' <- ' || a.ingredient_name, ', ') INTO missing_ingredients
    FROM z2_ingredients a
    WHERE NOT EXISTS (
        SELECT 1 FROM recipe_ingredients ri
        JOIN recipes rec ON rec.recipe_id = ri.recipe_id
        JOIN items res ON res.item_id = rec.result_item_id
        JOIN items ing ON ing.item_id = ri.item_id
        WHERE res.name = a.result_name AND ing.name = a.ingredient_name AND ri.quantity = a.quantity);

    IF missing_recipes IS NOT NULL OR missing_ingredients IS NOT NULL THEN
        RAISE EXCEPTION 'seed_zone2_gear_and_recipes: no se pudieron cargar recetas (revisar que existan los ítems/zonas y el orden de los scripts). Recetas: [%]. Ingredientes: [%]',
            COALESCE(missing_recipes, '-'), COALESCE(missing_ingredients, '-');
    END IF;
END $$;

DROP TABLE z2_ingredients;
DROP TABLE z2_recipes;

-- Chequeo rápido: 8 recetas en Zona 2 (4 de afinidad + 4 generales).
-- SELECT i.name, r.gold_cost, r.affinity FROM recipes r JOIN items i ON i.item_id = r.result_item_id
-- JOIN zones z ON z.zone_id = r.zone_id WHERE z.name = 'Bosque de Cenizas' ORDER BY r.affinity DESC, i.stat_value;
