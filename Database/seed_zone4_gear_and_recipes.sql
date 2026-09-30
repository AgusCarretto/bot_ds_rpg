-- =========================================================
-- Asado y Acero RPG — Recetas del herrero de ZONA 4 (Cordillera del Fuego), con el molde de cada zona:
-- 4 armas de AFINIDAD (1 por clase) + 2 armas GENERALES + 2 amuletos (siempre generales) = 8 recetas.
--
-- ESCALERA DE ZONAS (ver seed_zone2_gear_and_recipes.sql para la tabla completa de stats y el criterio):
--     Zona 3: +32 · generales +28/+40 · amuletos +30/+38
--     Zona 4: +50 · generales +44/+66 · amuletos +46/+60     <- ESTE archivo
--     Zona 5: +80 · +70/+105 · +75/+95
-- Al entrar con el equipo de Zona 3 una pelea común cuesta ~47% de la vida; con el de esta zona, ~18%.
--
-- Las 4 armas de afinidad son Legendarias que YA existían como ítems, una por clase (Facón de Hueso Añejo
-- Espadas/Guerrero, Cuchillos de Ceniza Dagas/Ninja, Arco de Caza Mayor Arcos/Arquero, Códice de las Brasas
-- Grimorios/Hechicero), con el stat de esta escalera. Las 2 generales y los 2 amuletos son ítems NUEVOS. Materiales:
-- drops de la zona (Salamandra Infernal: Escama Ígnea y Aliento de Fuego Eterno; Coloso de Magma: Núcleo de Magma y
-- Roca Volcánica Pura, ~7.5% por cacería cada uno), Madera de Nogal/Ébano, Oro Puro, Gema de Zafiro (las dos
-- últimas ~4.5% por acción) y Hierro; en las dos mejores un drop del jefe (Señor del Volcán: Colmillo del Señor
-- del Volcán y Brasa Eterna). Calibrado contra las tasas BASE de la run 1 (el reset post-Zona 5 las sube).
--
-- Ejecutar DESPUÉS de seed_zone_bosses.sql (drops del jefe), seed_class_gear_and_monster_drops.sql y
-- seed_zone3_gear_and_recipes.sql. Re-ejecutable, y FALLA EN VOZ ALTA si falta un ítem o una zona.
-- =========================================================

-- Re-stat de las 4 Legendarias de afinidad: +50. Precio de venta = 6 x el stat. No se toca su class_requirement.
UPDATE items SET rarity = 'Legendario', stat_value = 50, sell_price = 300, buy_price = 390
WHERE name IN ('Facón de Hueso Añejo', 'Cuchillos de Ceniza', 'Arco de Caza Mayor', 'Códice de las Brasas');

-- Ítems nuevos (upsert por nombre): 2 armas generales sin familia + 2 amuletos.
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price) VALUES
('Lanza de Magma',            'Weapon', 'Legendario', 44, 264, 343),
('Hacha de Obsidiana',        'Weapon', 'Legendario', 66, 396, 515),
('Coraza de Escamas Ígneas',  'Amulet', 'Legendario', 46, 276, 359),
('Talismán del Volcán',       'Amulet', 'Legendario', 60, 360, 468)
ON CONFLICT (name) DO UPDATE SET
    type = EXCLUDED.type, rarity = EXCLUDED.rarity, stat_value = EXCLUDED.stat_value,
    sell_price = EXCLUDED.sell_price, buy_price = EXCLUDED.buy_price;

CREATE TEMP TABLE z4_recipes (result_name TEXT, gold INTEGER, affinity BOOLEAN);
INSERT INTO z4_recipes VALUES
-- Armas de afinidad (Legendarias +50)
('Facón de Hueso Añejo',      900, true),   -- Guerrero (Espadas)
('Cuchillos de Ceniza',       900, true),   -- Ninja (Dagas)
('Arco de Caza Mayor',        900, true),   -- Arquero (Arcos)
('Códice de las Brasas',      900, true),   -- Hechicero (Grimorios)
-- Armas generales
('Lanza de Magma',            700, false),
('Hacha de Obsidiana',       1000, false),
-- Amuletos (generales)
('Coraza de Escamas Ígneas',  700, false),
('Talismán del Volcán',      1000, false);

CREATE TEMP TABLE z4_ingredients (result_name TEXT, ingredient_name TEXT, quantity INTEGER);
INSERT INTO z4_ingredients VALUES
-- Facón de Hueso Añejo: Hierro, roca volcánica para el filo y una Gema de Zafiro
('Facón de Hueso Añejo',      'Hierro',                   4),
('Facón de Hueso Añejo',      'Roca Volcánica Pura',      3),
('Facón de Hueso Añejo',      'Gema de Zafiro',           1),
-- Cuchillos de Ceniza: Hierro, escamas ígneas y aliento de fuego eterno
('Cuchillos de Ceniza',       'Hierro',                   3),
('Cuchillos de Ceniza',       'Escama Ígnea',             3),
('Cuchillos de Ceniza',       'Aliento de Fuego Eterno',  2),
-- Arco de Caza Mayor: Ébano y Nogal para el arco, aliento de fuego eterno para la cuerda
('Arco de Caza Mayor',        'Madera de Ébano',          1),
('Arco de Caza Mayor',        'Madera de Nogal',          3),
('Arco de Caza Mayor',        'Aliento de Fuego Eterno',  3),
-- Códice de las Brasas: núcleos de magma y escamas ígneas, tapas de Nogal
('Códice de las Brasas',      'Núcleo de Magma',          2),
('Códice de las Brasas',      'Escama Ígnea',             3),
('Códice de las Brasas',      'Madera de Nogal',          2),
-- Lanza de Magma (general +44): sin drop de jefe
('Lanza de Magma',            'Hierro',                   4),
('Lanza de Magma',            'Núcleo de Magma',          2),
('Lanza de Magma',            'Oro Puro',                 1),
-- Hacha de Obsidiana (general +66): el colmillo del jefe (Señor del Volcán)
('Hacha de Obsidiana',        'Hierro',                   4),
('Hacha de Obsidiana',        'Colmillo del Señor del Volcán', 1),
('Hacha de Obsidiana',        'Roca Volcánica Pura',      3),
('Hacha de Obsidiana',        'Gema de Zafiro',           1),
-- Coraza de Escamas Ígneas (amuleto +46)
('Coraza de Escamas Ígneas',  'Escama Ígnea',             4),
('Coraza de Escamas Ígneas',  'Núcleo de Magma',          2),
('Coraza de Escamas Ígneas',  'Oro Puro',                 2),
-- Talismán del Volcán (amuleto +60): la brasa eterna del jefe
('Talismán del Volcán',       'Brasa Eterna',             1),
('Talismán del Volcán',       'Aliento de Fuego Eterno',  3),
('Talismán del Volcán',       'Gema de Zafiro',           1),
('Talismán del Volcán',       'Oro Puro',                 2);

INSERT INTO recipes (result_item_id, gold_cost, zone_id, affinity)
SELECT i.item_id, r.gold, z.zone_id, r.affinity
FROM z4_recipes r
JOIN items i ON i.name = r.result_name
CROSS JOIN (SELECT zone_id FROM zones WHERE name = 'Cordillera del Fuego') z
ON CONFLICT (result_item_id) DO UPDATE SET
    gold_cost = EXCLUDED.gold_cost, zone_id = EXCLUDED.zone_id, affinity = EXCLUDED.affinity;

-- Los ingredientes de estas recetas los define ESTE archivo: se borran los que tuvieran y se cargan de nuevo.
DELETE FROM recipe_ingredients
WHERE recipe_id IN (
    SELECT rec.recipe_id FROM z4_recipes r
    JOIN items i ON i.name = r.result_name
    JOIN recipes rec ON rec.result_item_id = i.item_id);

INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT rec.recipe_id, ing.item_id, a.quantity
FROM z4_ingredients a
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
    FROM z4_recipes r
    WHERE NOT EXISTS (
        SELECT 1 FROM recipes rec JOIN items i ON i.item_id = rec.result_item_id
        JOIN zones z ON z.zone_id = rec.zone_id
        WHERE i.name = r.result_name AND z.name = 'Cordillera del Fuego'
          AND rec.gold_cost = r.gold AND rec.affinity = r.affinity);

    SELECT string_agg(a.result_name || ' <- ' || a.ingredient_name, ', ') INTO missing_ingredients
    FROM z4_ingredients a
    WHERE NOT EXISTS (
        SELECT 1 FROM recipe_ingredients ri
        JOIN recipes rec ON rec.recipe_id = ri.recipe_id
        JOIN items res ON res.item_id = rec.result_item_id
        JOIN items ing ON ing.item_id = ri.item_id
        WHERE res.name = a.result_name AND ing.name = a.ingredient_name AND ri.quantity = a.quantity);

    IF missing_recipes IS NOT NULL OR missing_ingredients IS NOT NULL THEN
        RAISE EXCEPTION 'seed_zone4_gear_and_recipes: no se pudieron cargar recetas (revisar que existan los ítems/zonas y el orden de los scripts). Recetas: [%]. Ingredientes: [%]',
            COALESCE(missing_recipes, '-'), COALESCE(missing_ingredients, '-');
    END IF;
END $$;

DROP TABLE z4_ingredients;
DROP TABLE z4_recipes;

-- Chequeo rápido: 8 recetas en Zona 4 (4 de afinidad + 4 generales).
-- SELECT i.name, r.gold_cost, r.affinity FROM recipes r JOIN items i ON i.item_id = r.result_item_id
-- JOIN zones z ON z.zone_id = r.zone_id WHERE z.name = 'Cordillera del Fuego' ORDER BY r.affinity DESC, i.stat_value;
