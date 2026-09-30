-- =========================================================
-- Asado y Acero RPG — Recetas del herrero de ZONA 3 (Minas del Yunque), con el molde de cada zona:
-- 4 armas de AFINIDAD (1 por clase) + 1 arma GENERAL + 2 amuletos (siempre generales) = 7 recetas (4 visibles por
-- jugador: 2 armas y 2 amuletos). La segunda arma general (Martillo de Fragua) se sacó al pasar a UN drop por monstruo.
--
-- ESCALERA DE ZONAS (ver seed_zone2_gear_and_recipes.sql para la tabla completa de stats y el criterio):
--     Zona 2: +20 · general +18 · amuletos +18/+24
--     Zona 3: +32 · general +28 · amuletos +30/+38     <- ESTE archivo
--     Zona 4: +50 · +44 · +46/+60      Zona 5: +80 · +70 · +75/+95
-- Al entrar con el equipo de Zona 2 una pelea común cuesta ~47% de la vida; con el de esta zona, ~17%.
--
-- Las 4 armas de afinidad son Legendarias que YA existían como ítems, una por clase (Mazo de Escoria Espadas/
-- Guerrero, Dagas de Garra Maldita Dagas/Ninja, Boleadoras de Escoria Arcos/Arquero, Báculo de Tizón Grimorios/
-- Hechicero): antes tenían recetas de Zona 1 con +45/+55 (las sacó el molde por zona) — acá vuelven con el stat
-- de esta escalera. La general y los 2 amuletos son ítems NUEVOS (el arma sin familia). Madera de Nogal y Oro Puro
-- (10% por acción), Hierro, y 4 drops de la zona, UNO por monstruo (finalize_monster_roster.sql, seed_travel_monsters.sql):
--   · a granel (/hunt, 5% por cacería cada uno = ~20 cacerías por unidad): Yunque Fragmentado (Gólem del Yunque) y
--     Gema en Bruto (Excavador Profundo);
--   · escaso (/travel, ~50 min por unidad): Escoria Metálica Densa (Mole de Escoria);
--   · raro (el jefe, ~200 min por unidad): Yunque del Capataz (Capataz de Hierro).
-- Las armas de afinidad piden 2 Escoria Metálica Densa (~100 min con hunt 10% / travel 20% / jefe 15%). Calibrado
-- contra las tasas BASE de la run 1 (el reset post-Zona 5 las sube).
--
-- Ejecutar DESPUÉS de seed_zone_bosses.sql (drops del jefe), seed_class_gear_and_monster_drops.sql (los ítems
-- Legendarios) y seed_zone2_gear_and_recipes.sql. Re-ejecutable, y FALLA EN VOZ ALTA si falta un ítem o una zona.
-- =========================================================

-- Re-stat de las 4 Legendarias de afinidad: +32. Precio de venta = 6 x el stat (por debajo del costo de forja).
-- No se toca su class_requirement (siguen siendo exclusivas de su clase) ni su familia.
UPDATE items SET rarity = 'Legendario', stat_value = 32, sell_price = 192, buy_price = 250
WHERE name IN ('Mazo de Escoria', 'Dagas de Garra Maldita', 'Boleadoras de Escoria', 'Báculo de Tizón');

-- Ítems nuevos (upsert por nombre): 1 arma general sin familia + 2 amuletos.
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price) VALUES
('Pico de Minero Reforzado', 'Weapon', 'Legendario', 28, 168, 218),
('Casco de Capataz',        'Amulet', 'Legendario', 30, 180, 234),
('Peto de Escoria Templada', 'Amulet', 'Legendario', 38, 228, 296)
ON CONFLICT (name) DO UPDATE SET
    type = EXCLUDED.type, rarity = EXCLUDED.rarity, stat_value = EXCLUDED.stat_value,
    sell_price = EXCLUDED.sell_price, buy_price = EXCLUDED.buy_price;

CREATE TEMP TABLE z3_recipes (result_name TEXT, gold INTEGER, affinity BOOLEAN);
INSERT INTO z3_recipes VALUES
-- Armas de afinidad (Legendarias +32)
('Mazo de Escoria',           600, true),   -- Guerrero (Espadas)
('Dagas de Garra Maldita',    600, true),   -- Ninja (Dagas)
('Boleadoras de Escoria',     600, true),   -- Arquero (Arcos)
('Báculo de Tizón',           600, true),   -- Hechicero (Grimorios)
-- Arma general
('Pico de Minero Reforzado',  450, false),
-- Amuletos (generales)
('Casco de Capataz',          450, false),
('Peto de Escoria Templada',  650, false);

CREATE TEMP TABLE z3_ingredients (result_name TEXT, ingredient_name TEXT, quantity INTEGER);
INSERT INTO z3_ingredients VALUES
-- Mazo de Escoria: Hierro de la mina, un mango de Nogal, yunques fragmentados y 2 Escoria Metálica Densa (~100 min)
('Mazo de Escoria',           'Hierro',                  5),
('Mazo de Escoria',           'Madera de Nogal',         2),
('Mazo de Escoria',           'Yunque Fragmentado',      5),
('Mazo de Escoria',           'Escoria Metálica Densa',  2),
-- Dagas de Garra Maldita: hojas de Hierro con gemas en bruto, un trozo de yunque para el filo y 2 Escoria
('Dagas de Garra Maldita',    'Hierro',                  4),
('Dagas de Garra Maldita',    'Gema en Bruto',           3),
('Dagas de Garra Maldita',    'Yunque Fragmentado',      2),
('Dagas de Garra Maldita',    'Escoria Metálica Densa',  2),
-- Boleadoras de Escoria: Nogal, yunque fragmentado y gemas para las bolas, y 2 Escoria
('Boleadoras de Escoria',     'Madera de Nogal',         4),
('Boleadoras de Escoria',     'Yunque Fragmentado',      3),
('Boleadoras de Escoria',     'Gema en Bruto',           2),
('Boleadoras de Escoria',     'Escoria Metálica Densa',  2),
-- Báculo de Tizón: Nogal, gemas en bruto, yunque fragmentado y 2 Escoria
('Báculo de Tizón',           'Madera de Nogal',         3),
('Báculo de Tizón',           'Gema en Bruto',           3),
('Báculo de Tizón',           'Yunque Fragmentado',      2),
('Báculo de Tizón',           'Escoria Metálica Densa',  2),
-- Pico de Minero Reforzado (general +28): Hierro, Oro Puro y solo drops de /hunt (4 + 4, ~80 min), sin /travel ni jefe
('Pico de Minero Reforzado',  'Hierro',                  4),
('Pico de Minero Reforzado',  'Oro Puro',                1),
('Pico de Minero Reforzado',  'Yunque Fragmentado',      4),
('Pico de Minero Reforzado',  'Gema en Bruto',           4),
-- Casco de Capataz (amuleto +30): Hierro, 5 + 5 de /hunt y 1 Escoria (~100 min)
('Casco de Capataz',          'Hierro',                  3),
('Casco de Capataz',          'Yunque Fragmentado',      5),
('Casco de Capataz',          'Gema en Bruto',           5),
('Casco de Capataz',          'Escoria Metálica Densa',  1),
-- Peto de Escoria Templada (amuleto +38): el yunque del jefe (~200 min) + 3 Escoria + Oro Puro
('Peto de Escoria Templada',  'Oro Puro',                2),
('Peto de Escoria Templada',  'Yunque del Capataz',      1),
('Peto de Escoria Templada',  'Escoria Metálica Densa',  3),
('Peto de Escoria Templada',  'Yunque Fragmentado',      3);

INSERT INTO recipes (result_item_id, gold_cost, zone_id, affinity)
SELECT i.item_id, r.gold, z.zone_id, r.affinity
FROM z3_recipes r
JOIN items i ON i.name = r.result_name
CROSS JOIN (SELECT zone_id FROM zones WHERE name = 'Minas del Yunque') z
ON CONFLICT (result_item_id) DO UPDATE SET
    gold_cost = EXCLUDED.gold_cost, zone_id = EXCLUDED.zone_id, affinity = EXCLUDED.affinity;

-- Los ingredientes de estas recetas los define ESTE archivo: se borran los que tuvieran y se cargan de nuevo.
DELETE FROM recipe_ingredients
WHERE recipe_id IN (
    SELECT rec.recipe_id FROM z3_recipes r
    JOIN items i ON i.name = r.result_name
    JOIN recipes rec ON rec.result_item_id = i.item_id);

INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT rec.recipe_id, ing.item_id, a.quantity
FROM z3_ingredients a
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
    FROM z3_recipes r
    WHERE NOT EXISTS (
        SELECT 1 FROM recipes rec JOIN items i ON i.item_id = rec.result_item_id
        JOIN zones z ON z.zone_id = rec.zone_id
        WHERE i.name = r.result_name AND z.name = 'Minas del Yunque'
          AND rec.gold_cost = r.gold AND rec.affinity = r.affinity);

    SELECT string_agg(a.result_name || ' <- ' || a.ingredient_name, ', ') INTO missing_ingredients
    FROM z3_ingredients a
    WHERE NOT EXISTS (
        SELECT 1 FROM recipe_ingredients ri
        JOIN recipes rec ON rec.recipe_id = ri.recipe_id
        JOIN items res ON res.item_id = rec.result_item_id
        JOIN items ing ON ing.item_id = ri.item_id
        WHERE res.name = a.result_name AND ing.name = a.ingredient_name AND ri.quantity = a.quantity);

    IF missing_recipes IS NOT NULL OR missing_ingredients IS NOT NULL THEN
        RAISE EXCEPTION 'seed_zone3_gear_and_recipes: no se pudieron cargar recetas (revisar que existan los ítems/zonas y el orden de los scripts). Recetas: [%]. Ingredientes: [%]',
            COALESCE(missing_recipes, '-'), COALESCE(missing_ingredients, '-');
    END IF;
END $$;

DROP TABLE z3_ingredients;
DROP TABLE z3_recipes;

-- Chequeo rápido: 7 recetas en Zona 3 (4 de afinidad + 1 general + 2 amuletos).
-- SELECT i.name, r.gold_cost, r.affinity FROM recipes r JOIN items i ON i.item_id = r.result_item_id
-- JOIN zones z ON z.zone_id = r.zone_id WHERE z.name = 'Minas del Yunque' ORDER BY r.affinity DESC, i.stat_value;
