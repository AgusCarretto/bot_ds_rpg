-- =========================================================
-- Asado y Acero RPG — Recetas del herrero de ZONA 2 (Bosque de Cenizas), con el molde de cada zona:
-- 4 armas de AFINIDAD (1 por clase) + 1 arma GENERAL + 2 amuletos (siempre generales) = 7 recetas (4 visibles por
-- jugador: 2 armas y 2 amuletos). La segunda arma general (Alabarda del Alfa) se sacó al pasar a UN drop por monstruo.
--
-- ESCALERA DE ZONAS: el equipo de cada zona es un SALTO sobre el de la anterior (x1.6 por zona), y los monstruos
-- están calibrados para que al entrar con el equipo viejo cueste y con el propio se vuelva cómodo — sin ser un
-- paseo (ver los comentarios de la escalera en seed_zones_and_monsters.sql / seed_zone_bosses.sql y MEJORAS.md).
-- Stats de la escalera (arma de afinidad · generales bajo/alto · amuletos bajo/alto):
--
--     Zona 1: +5 (inicial) / +15 general (Hoja)  · +10/+15   · +10/+16
--     Zona 2: +20                                · +18        · +18/+24    <- ESTE archivo
--     Zona 3: +32 · +28 · +30/+38      Zona 4: +50 · +44 · +46/+60      Zona 5: +80 · +70 · +75/+95
-- (Las generales "altas" +26/+40/+66/+105 —con drop de jefe— se sacaron; las afinidades de cada clase ya las superan.)
--
-- (Son MENOS que los primeros números que se habían puesto — +35/+55/+72/+85 —: con esos, al salir de cada zona
-- la pelea común costaba 2-8% de la vida, un paseo; se bajaron y los monstruos se recalcularon encima.)
-- Las 4 armas de afinidad son las Épicas que YA existían como ítems (una por familia: Hacha de Hierro MK3
-- Espadas/Guerrero, Colmillo Nocturno Dagas/Ninja, Arco Élfico Ancestral Arcos/Arquero, Grimorio de las
-- Tormentas Grimorios/Hechicero) — no tenían receta ni este stat (eran +35). La general y 1 amuleto son
-- ítems NUEVOS, sin familia (nadie tiene sinergia con ellas). Madera de Roble (25% por /chop), Hierro/Carbón (12.5%
-- por /mine cada uno) y 4 drops de la zona, UNO por monstruo (finalize_monster_roster.sql, seed_travel_monsters.sql):
--   · a granel (/hunt, 5% por cacería cada uno = ~20 cacerías por unidad): Garra de Puma Cenizo (Puma de las
--     Cenizas) y Esencia Espectral (Espíritu del Monte);
--   · escaso (/travel, 20% y un viaje cada 10 min = ~50 min por unidad): Ceniza Bendita (Ciervo Sagrado);
--   · raro (el jefe, 15% y una pelea cada 30 min = ~200 min por unidad): Pelaje Plateado del Alfa (Lobisón Alfa).
-- Las cantidades de abajo salen de esas tasas (hunt 10% / travel 20% / jefe 15%, ver CombatRewardCalculator): las
-- armas de afinidad piden 2 Ceniza Bendita (~100 min), la general ~80 min de cazar, el amuleto bajo ~100 y el alto
-- el drop del jefe (~200) — más o menos lo que tarda subir de nivel 5 a 10. Ojo: los bonos de drop y de cantidad del
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

-- Ítems nuevos (upsert por nombre): 1 arma general sin familia + 1 amuleto.
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price) VALUES
('Cuchilla de Cenizas',        'Weapon', 'Épico', 18, 108, 140),
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
-- Arma general
('Cuchilla de Cenizas',          300, false),
-- Amuletos (generales)
('Mate Tallado en Cenizas',      300, false),
('Talismán de Ceniza Bendita',   400, false);

-- Materiales de RECOLECCIÓN (Madera, Piedra, Hierro, Carbón, Oro Puro): las cantidades son ~3x (Común) y ~2x (Raro/Épico)
-- lo de antes porque /chop y /mine ahora dan varias unidades por acción (GameData/GatheringYield: Común 1-5, Raro/Épico
-- 1-3, Legendario/Mítico 1); el ritmo de la run 1 no cambió. Los Legendarios/Míticos de recolección siguen en 1.
CREATE TEMP TABLE z2_ingredients (result_name TEXT, ingredient_name TEXT, quantity INTEGER);
INSERT INTO z2_ingredients VALUES
-- Hacha de Hierro MK3: la hoja de Hierro, el mango de Roble, garras del bosque y 2 Ceniza Bendita (~100 min)
('Hacha de Hierro MK3',          'Hierro',                    10),
('Hacha de Hierro MK3',          'Madera de Roble',           4),
('Hacha de Hierro MK3',          'Garra de Puma Cenizo',      5),
('Hacha de Hierro MK3',          'Ceniza Bendita',            2),
-- Colmillo Nocturno: Hierro, garras y esencia espectral para el filo que no se ve, y 2 Ceniza Bendita
('Colmillo Nocturno',            'Hierro',                    8),
('Colmillo Nocturno',            'Garra de Puma Cenizo',      3),
('Colmillo Nocturno',            'Esencia Espectral',         3),
('Colmillo Nocturno',            'Ceniza Bendita',            2),
-- Arco Élfico Ancestral: la madera noble, un hilo espectral y 2 Ceniza Bendita
('Arco Élfico Ancestral',        'Madera de Roble',           8),
('Arco Élfico Ancestral',        'Esencia Espectral',         5),
('Arco Élfico Ancestral',        'Ceniza Bendita',            2),
-- Grimorio de las Tormentas: tapas de Roble, esencia espectral y la ceniza bendita como tinta
('Grimorio de las Tormentas',    'Madera de Roble',           6),
('Grimorio de las Tormentas',    'Esencia Espectral',         5),
('Grimorio de las Tormentas',    'Ceniza Bendita',            2),
-- Cuchilla de Cenizas (general +18): Hierro y Carbón, y solo drops de /hunt (4 + 4, ~80 min), sin /travel ni jefe
('Cuchilla de Cenizas',          'Hierro',                    6),
('Cuchilla de Cenizas',          'Carbón',                    4),
('Cuchilla de Cenizas',          'Garra de Puma Cenizo',      4),
('Cuchilla de Cenizas',          'Esencia Espectral',         4),
-- Mate Tallado en Cenizas (amuleto +18): Carbón y Roble, 5 + 5 de /hunt y 1 Ceniza Bendita (~100 min)
('Mate Tallado en Cenizas',      'Carbón',                    6),
('Mate Tallado en Cenizas',      'Madera de Roble',           4),
('Mate Tallado en Cenizas',      'Garra de Puma Cenizo',      5),
('Mate Tallado en Cenizas',      'Esencia Espectral',         5),
('Mate Tallado en Cenizas',      'Ceniza Bendita',            1),
-- Talismán de Ceniza Bendita (amuleto +24): el pelaje plateado del jefe (~200 min) + 3 Ceniza Bendita + Oro Puro
('Talismán de Ceniza Bendita',   'Oro Puro',                  2),
('Talismán de Ceniza Bendita',   'Pelaje Plateado del Alfa',  1),
('Talismán de Ceniza Bendita',   'Ceniza Bendita',            3),
('Talismán de Ceniza Bendita',   'Garra de Puma Cenizo',      3);

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

-- Chequeo rápido: 7 recetas en Zona 2 (4 de afinidad + 1 general + 2 amuletos).
-- SELECT i.name, r.gold_cost, r.affinity FROM recipes r JOIN items i ON i.item_id = r.result_item_id
-- JOIN zones z ON z.zone_id = r.zone_id WHERE z.name = 'Bosque de Cenizas' ORDER BY r.affinity DESC, i.stat_value;
