-- =========================================================
-- Asado y Acero RPG — Recetas del herrero de ZONA 5 (Cráter de la Escoria), con el molde de cada zona:
-- 4 armas de AFINIDAD (1 por clase) + 1 arma GENERAL + 2 amuletos (siempre generales) = 7 recetas (4 visibles por
-- jugador: 2 armas y 2 amuletos). La segunda arma general (Guadaña de Almas) se sacó al pasar a UN drop por monstruo.
-- Es el equipo FINAL de la run 1 (derrotar al Soberano de la Escoria es terminar las 5 zonas; ahí se desbloquea el
-- reset a futuro, que va a subir los drops y las cantidades).
--
-- ESCALERA DE ZONAS (ver seed_zone2_gear_and_recipes.sql para la tabla completa de stats y el criterio):
--     Zona 4: +50 · general +44 · amuletos +46/+60
--     Zona 5: +80 · general +70 · amuletos +75/+95     <- ESTE archivo
-- Al entrar con el equipo de Zona 4 una pelea común cuesta ~47% de la vida; con el de esta zona, ~16%.
--
-- TODOS los ítems de esta zona son NUEVOS y Míticos (las 4 armas de afinidad con su familia, la general sin
-- familia y los 2 amuletos). Hierro, Ébano y Zafiro (~4.5% por acción), Oro Puro, y — para las 4 armas de
-- afinidad — UN material Mítico de recolección (Fragmento de Meteorito de /mine o Corteza del Árbol de Vida de
-- /chop, ~0.5% por acción: ~17 horas por unidad, el objetivo de largo plazo de la run), y 4 drops de la zona, UNO
-- por monstruo (finalize_monster_roster.sql, seed_travel_monsters.sql):
--   · a granel (/hunt, 5% por cacería cada uno = ~20 cacerías por unidad): Fragmento de Alma (Devorador de Almas)
--     y Corazón de Titán (Titán de Escoria);
--   · escaso (/travel, ~50 min por unidad): Ceniza del Abismo (Quimera del Abismo);
--   · raro (el jefe final, ~200 min por unidad): Corazón del Soberano (Soberano de la Escoria).
-- Calibrado contra las tasas BASE de la run 1.
--
-- Ejecutar DESPUÉS de seed_zone_bosses.sql (drops del jefe) y seed_zone4_gear_and_recipes.sql. Re-ejecutable,
-- y FALLA EN VOZ ALTA si falta un ítem o una zona.
-- =========================================================

-- Ítems nuevos (upsert por nombre). Precio de venta = 6 x el stat (por debajo del costo de forja).
-- Las 4 armas de afinidad llevan su familia (la sinergia de clase x1.5 sale de ahí).
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price, weapon_family) VALUES
('Espada del Abismo',          'Weapon', 'Mítico', 80, 480, 624, 'Espadas'),
('Colmillo del Cráter',        'Weapon', 'Mítico', 80, 480, 624, 'Dagas'),
('Arco del Alma Errante',      'Weapon', 'Mítico', 80, 480, 624, 'Arcos'),
('Báculo del Árbol de Vida',   'Weapon', 'Mítico', 80, 480, 624, 'Grimorios'),
-- General: sin familia
('Martillo del Titán',         'Weapon', 'Mítico', 70, 420, 546, NULL)
ON CONFLICT (name) DO UPDATE SET
    type = EXCLUDED.type, rarity = EXCLUDED.rarity, stat_value = EXCLUDED.stat_value,
    sell_price = EXCLUDED.sell_price, buy_price = EXCLUDED.buy_price, weapon_family = EXCLUDED.weapon_family;

INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price) VALUES
('Égida del Devorador',        'Amulet', 'Mítico', 75, 450, 585),
('Corazón de Titán Engarzado', 'Amulet', 'Mítico', 95, 570, 741)
ON CONFLICT (name) DO UPDATE SET
    type = EXCLUDED.type, rarity = EXCLUDED.rarity, stat_value = EXCLUDED.stat_value,
    sell_price = EXCLUDED.sell_price, buy_price = EXCLUDED.buy_price;

CREATE TEMP TABLE z5_recipes (result_name TEXT, gold INTEGER, affinity BOOLEAN);
INSERT INTO z5_recipes VALUES
-- Armas de afinidad (Míticas +80)
('Espada del Abismo',           1400, true),   -- Guerrero (Espadas)
('Colmillo del Cráter',         1400, true),   -- Ninja (Dagas)
('Arco del Alma Errante',       1400, true),   -- Arquero (Arcos)
('Báculo del Árbol de Vida',    1400, true),   -- Hechicero (Grimorios)
-- Arma general
('Martillo del Titán',          1100, false),
-- Amuletos (generales)
('Égida del Devorador',         1100, false),
('Corazón de Titán Engarzado',  1500, false);

CREATE TEMP TABLE z5_ingredients (result_name TEXT, ingredient_name TEXT, quantity INTEGER);
INSERT INTO z5_ingredients VALUES
-- Espada del Abismo: Hierro, fragmentos de alma y corazones de titán, 2 Ceniza del Abismo, y UN fragmento de meteorito
-- (Mítico de /mine)
('Espada del Abismo',           'Hierro',                       4),
('Espada del Abismo',           'Fragmento de Alma',            2),
('Espada del Abismo',           'Corazón de Titán',             3),
('Espada del Abismo',           'Ceniza del Abismo',            2),
('Espada del Abismo',           'Fragmento de Meteorito',       1),
-- Colmillo del Cráter: Hierro, fragmentos de alma y corazones de titán, 2 Ceniza del Abismo, y UN fragmento de meteorito
('Colmillo del Cráter',         'Hierro',                       4),
('Colmillo del Cráter',         'Fragmento de Alma',            3),
('Colmillo del Cráter',         'Corazón de Titán',             2),
('Colmillo del Cráter',         'Ceniza del Abismo',            2),
('Colmillo del Cráter',         'Fragmento de Meteorito',       1),
-- Arco del Alma Errante: fragmentos de alma + corazones de titán, Ébano, 2 Ceniza del Abismo, y UNA corteza del árbol
-- de vida (Mítico de /chop)
('Arco del Alma Errante',       'Madera de Ébano',              1),
('Arco del Alma Errante',       'Fragmento de Alma',            3),
('Arco del Alma Errante',       'Corazón de Titán',             2),
('Arco del Alma Errante',       'Ceniza del Abismo',            2),
('Arco del Alma Errante',       'Corteza del Árbol de Vida',    1),
-- Báculo del Árbol de Vida: fragmentos de alma + corazones de titán, Ébano, 2 Ceniza del Abismo, y UNA corteza del
-- árbol de vida
('Báculo del Árbol de Vida',    'Madera de Ébano',              1),
('Báculo del Árbol de Vida',    'Fragmento de Alma',            2),
('Báculo del Árbol de Vida',    'Corazón de Titán',             3),
('Báculo del Árbol de Vida',    'Ceniza del Abismo',            2),
('Báculo del Árbol de Vida',    'Corteza del Árbol de Vida',    1),
-- Martillo del Titán (general +70): Hierro, Zafiro y solo drops de /hunt (4 + 4, ~80 min), sin /travel ni jefe
('Martillo del Titán',          'Hierro',                       4),
('Martillo del Titán',          'Gema de Zafiro',               1),
('Martillo del Titán',          'Fragmento de Alma',            4),
('Martillo del Titán',          'Corazón de Titán',             4),
-- Égida del Devorador (amuleto +75): Oro Puro, Zafiro, 5 + 5 de /hunt y 1 Ceniza del Abismo
('Égida del Devorador',         'Gema de Zafiro',               1),
('Égida del Devorador',         'Oro Puro',                     2),
('Égida del Devorador',         'Fragmento de Alma',            5),
('Égida del Devorador',         'Corazón de Titán',             5),
('Égida del Devorador',         'Ceniza del Abismo',            1),
-- Corazón de Titán Engarzado (amuleto +95): el corazón del jefe final (~200 min) + 3 Ceniza del Abismo
('Corazón de Titán Engarzado',  'Gema de Zafiro',               2),
('Corazón de Titán Engarzado',  'Corazón del Soberano',         1),
('Corazón de Titán Engarzado',  'Ceniza del Abismo',            3),
('Corazón de Titán Engarzado',  'Fragmento de Alma',            3);

INSERT INTO recipes (result_item_id, gold_cost, zone_id, affinity)
SELECT i.item_id, r.gold, z.zone_id, r.affinity
FROM z5_recipes r
JOIN items i ON i.name = r.result_name
CROSS JOIN (SELECT zone_id FROM zones WHERE name = 'Cráter de la Escoria') z
ON CONFLICT (result_item_id) DO UPDATE SET
    gold_cost = EXCLUDED.gold_cost, zone_id = EXCLUDED.zone_id, affinity = EXCLUDED.affinity;

-- Los ingredientes de estas recetas los define ESTE archivo: se borran los que tuvieran y se cargan de nuevo.
DELETE FROM recipe_ingredients
WHERE recipe_id IN (
    SELECT rec.recipe_id FROM z5_recipes r
    JOIN items i ON i.name = r.result_name
    JOIN recipes rec ON rec.result_item_id = i.item_id);

INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT rec.recipe_id, ing.item_id, a.quantity
FROM z5_ingredients a
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
    FROM z5_recipes r
    WHERE NOT EXISTS (
        SELECT 1 FROM recipes rec JOIN items i ON i.item_id = rec.result_item_id
        JOIN zones z ON z.zone_id = rec.zone_id
        WHERE i.name = r.result_name AND z.name = 'Cráter de la Escoria'
          AND rec.gold_cost = r.gold AND rec.affinity = r.affinity);

    SELECT string_agg(a.result_name || ' <- ' || a.ingredient_name, ', ') INTO missing_ingredients
    FROM z5_ingredients a
    WHERE NOT EXISTS (
        SELECT 1 FROM recipe_ingredients ri
        JOIN recipes rec ON rec.recipe_id = ri.recipe_id
        JOIN items res ON res.item_id = rec.result_item_id
        JOIN items ing ON ing.item_id = ri.item_id
        WHERE res.name = a.result_name AND ing.name = a.ingredient_name AND ri.quantity = a.quantity);

    IF missing_recipes IS NOT NULL OR missing_ingredients IS NOT NULL THEN
        RAISE EXCEPTION 'seed_zone5_gear_and_recipes: no se pudieron cargar recetas (revisar que existan los ítems/zonas y el orden de los scripts). Recetas: [%]. Ingredientes: [%]',
            COALESCE(missing_recipes, '-'), COALESCE(missing_ingredients, '-');
    END IF;
END $$;

DROP TABLE z5_ingredients;
DROP TABLE z5_recipes;

-- Chequeo rápido: 7 recetas en Zona 5 (4 de afinidad + 1 general + 2 amuletos).
-- SELECT i.name, r.gold_cost, r.affinity FROM recipes r JOIN items i ON i.item_id = r.result_item_id
-- JOIN zones z ON z.zone_id = r.zone_id WHERE z.name = 'Cráter de la Escoria' ORDER BY r.affinity DESC, i.stat_value;
