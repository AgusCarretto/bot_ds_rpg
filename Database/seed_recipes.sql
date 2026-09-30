-- =========================================================
-- Asado y Acero RPG — Recetas del herrero de ZONA 1 (Praderas del Mate), con el molde de cada zona:
--
--   4 armas de AFINIDAD (1 por clase: la de la familia de su clase)  +  2 armas GENERALES
--   +  2 amuletos (los amuletos son SIEMPRE generales, de cualquier clase)      = 8 recetas por zona
--
-- Cada jugador ve solo las 5 de su clase en su zona actual (su arma de afinidad + las 2 generales + los 2
-- amuletos), así el mensaje de /forge recipes entra sobrado en los límites de Discord.
--
-- Casi todo se arma con recolección (/chop, /mine) y, como mucho, 1 unidad de 1 drop de la zona: cada drop
-- puntual sale ~2.5% por cacería (~40 cacerías por unidad), así que pedir más lo vuelve un suplicio.
-- El oro está por encima del precio de VENTA del resultado (más el valor de los materiales) para que forjar
-- y revender no sea negocio.
--
-- Este archivo REEMPLAZA a la versión anterior (16 recetas de clase Legendarias +45/+55 y 7 más): esas
-- quedaron fuera del molde y van a rehacerse para las zonas altas (están en el historial de git; para una
-- base que YA las tenía, Database/trim_recipes_to_zone_template.sql las borra).
--
-- Ejecutar DESPUÉS de seed_zones_and_monsters.sql (zona + Pluma de Ñandú / Colmillo de Cimarrón),
-- seed_consumables_and_base_swords.sql (Espada de Madera) y seed_class_gear_and_monster_drops.sql
-- (Hombreras de Cuero Grueso, Cuero Grueso, Piedra Caliente...), y de recipes.zone_id / recipes.affinity
-- (schema.sql, o add_recipe_zone_and_affinity.sql en una base ya existente).
--
-- Re-ejecutable: recetas e ingredientes se pisan por nombre. FALLA EN VOZ ALTA (RAISE EXCEPTION) si alguna
-- receta o ingrediente referencia un ítem que no existe, en vez de omitirla en silencio o crearla sin ese
-- ingrediente (el problema recurrente que documenta CLAUDE.md — ver el orden en run_fresh_install.sql).
-- =========================================================

-- Los amuletos son siempre generales: sin clase exclusiva (antes los Legendarios de clase la tenían).
UPDATE items SET class_requirement = NULL WHERE type = 'Amulet' AND class_requirement IS NOT NULL;

CREATE TEMP TABLE z1_recipes (result_name TEXT, gold INTEGER, affinity BOOLEAN);
INSERT INTO z1_recipes VALUES
-- Armas de afinidad: una por clase (Espadas = Guerrero, Dagas = Ninja, Arcos = Arquero, Grimorios = Hechicero)
('Espada de Madera',          40, true),
('Daga Oxidada',              40, true),
('Arco Corto de Sauce',       40, true),
('Grimorio Desgastado',       40, true),
-- Armas generales
('Hoja de Acero Puro',       180, false),
('Hacha de Hierro MK3',      150, false),
-- Amuletos (generales)
('Amuleto del Levantador',   150, false),
('Hombreras de Cuero Grueso', 200, false);

CREATE TEMP TABLE z1_ingredients (result_name TEXT, ingredient_name TEXT, quantity INTEGER);
INSERT INTO z1_ingredients VALUES
-- Espada de Madera: solo recolección
('Espada de Madera',          'Madera de Pino',        3),
('Espada de Madera',          'Hierro',                1),
-- Daga Oxidada: la hoja de Hierro, Piedra para afilarla y un Colmillo de Cimarrón como punta
('Daga Oxidada',              'Hierro',                1),
('Daga Oxidada',              'Piedra',                2),
('Daga Oxidada',              'Colmillo de Cimarrón',  1),
-- Arco Corto de Sauce: Madera de Pino + Tela Rasgada (la "cuerda": hilo sacado de la tela)
('Arco Corto de Sauce',       'Madera de Pino',        3),
('Arco Corto de Sauce',       'Tela Rasgada',          1),
-- Grimorio Desgastado: las tapas de Madera y una Pluma de Ñandú para escribir
('Grimorio Desgastado',       'Madera de Pino',        2),
('Grimorio Desgastado',       'Pluma de Ñandú',        1),
-- Hoja de Acero Puro (Raro +15): Hierro + empuñadura de Madera + el filo de un Colmillo de Jabalí
('Hoja de Acero Puro',        'Hierro',                3),
('Hoja de Acero Puro',        'Madera de Pino',        2),
('Hoja de Acero Puro',        'Colmillo de Jabalí',    1),
-- Hacha de Hierro MK3 (Épico +35)
('Hacha de Hierro MK3',       'Hierro',                5),
('Hacha de Hierro MK3',       'Cuero Grueso',          3),
-- Amuleto del Levantador (+10): 1 colmillo, no 3 (3 eran ~2 horas de cazar solo eso)
('Amuleto del Levantador',    'Piedra',                5),
('Amuleto del Levantador',    'Colmillo de Jabalí',    1),
-- Hombreras de Cuero Grueso (Legendario +20 DEF): el amuleto "de fondo" de la zona
('Hombreras de Cuero Grueso', 'Cuero Grueso',          5),
('Hombreras de Cuero Grueso', 'Piedra Caliente',       3);

INSERT INTO recipes (result_item_id, gold_cost, zone_id, affinity)
SELECT i.item_id, r.gold, z.zone_id, r.affinity
FROM z1_recipes r
JOIN items i ON i.name = r.result_name
CROSS JOIN (SELECT zone_id FROM zones WHERE name = 'Praderas del Mate') z
ON CONFLICT (result_item_id) DO UPDATE SET
    gold_cost = EXCLUDED.gold_cost, zone_id = EXCLUDED.zone_id, affinity = EXCLUDED.affinity;

INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT rec.recipe_id, ing.item_id, a.quantity
FROM z1_ingredients a
JOIN items res ON res.name = a.result_name
JOIN recipes rec ON rec.result_item_id = res.item_id
JOIN items ing ON ing.name = a.ingredient_name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- Verificación: cada receta e ingrediente de arriba TIENE que haberse cargado. Si un nombre no existe (ítem
-- o zona), los JOIN de arriba lo omitieron en silencio — acá se convierte en un error con el detalle.
DO $$
DECLARE
    missing_recipes     TEXT;
    missing_ingredients TEXT;
BEGIN
    SELECT string_agg(r.result_name, ', ') INTO missing_recipes
    FROM z1_recipes r
    WHERE NOT EXISTS (
        SELECT 1 FROM recipes rec JOIN items i ON i.item_id = rec.result_item_id
        JOIN zones z ON z.zone_id = rec.zone_id
        WHERE i.name = r.result_name AND z.name = 'Praderas del Mate'
          AND rec.gold_cost = r.gold AND rec.affinity = r.affinity);

    SELECT string_agg(a.result_name || ' <- ' || a.ingredient_name, ', ') INTO missing_ingredients
    FROM z1_ingredients a
    WHERE NOT EXISTS (
        SELECT 1 FROM recipe_ingredients ri
        JOIN recipes rec ON rec.recipe_id = ri.recipe_id
        JOIN items res ON res.item_id = rec.result_item_id
        JOIN items ing ON ing.item_id = ri.item_id
        WHERE res.name = a.result_name AND ing.name = a.ingredient_name AND ri.quantity = a.quantity);

    IF missing_recipes IS NOT NULL OR missing_ingredients IS NOT NULL THEN
        RAISE EXCEPTION 'seed_recipes: no se pudieron cargar recetas (revisar que existan los ítems/zonas y el orden de los scripts). Recetas: [%]. Ingredientes: [%]',
            COALESCE(missing_recipes, '-'), COALESCE(missing_ingredients, '-');
    END IF;
END $$;

DROP TABLE z1_ingredients;
DROP TABLE z1_recipes;

-- Chequeo rápido: 8 recetas en Zona 1 (4 de afinidad + 4 generales).
-- SELECT i.name, r.gold_cost, r.affinity FROM recipes r JOIN items i ON i.item_id = r.result_item_id
-- JOIN zones z ON z.zone_id = r.zone_id WHERE z.name = 'Praderas del Mate' ORDER BY r.affinity DESC, i.stat_value;
