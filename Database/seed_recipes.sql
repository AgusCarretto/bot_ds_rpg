-- =========================================================
-- Asado y Acero RPG — Recetas del herrero de ZONA 1 (Praderas del Mate), con el molde de cada zona:
--
--   4 armas de AFINIDAD (1 por clase: la de la familia de su clase)  +  1 arma GENERAL
--   +  2 amuletos (los amuletos son SIEMPRE generales, de cualquier clase)      = 7 recetas por zona
--
-- Cada jugador ve solo 4 en su zona actual: 2 armas (la de afinidad de su clase + la general) y 2 amuletos.
-- (Antes eran 8 por zona / 5 visibles, con dos armas generales; con UN solo drop por monstruo — ver
-- finalize_monster_roster.sql — no alcanzaban las fuentes para tantas, y la segunda general no le ganaba al arma
-- de afinidad de nadie. Para una base que todavía las tiene: Database/remove_extra_general_recipes.sql.)
--
-- Los drops de Zona 1 son 5, UNO por monstruo (finalize_monster_roster.sql, seed_travel_monsters.sql): Colmillo de
-- Jabalí, Colmillo de Cimarrón y Pluma de Ñandú (los 3 monstruos de /hunt, 10% de drop y 1 de 3 monstruos =
-- ~3.3% por cacería cada uno = ~30 cacerías por unidad), Cuero Grueso (/travel: 20%, un viaje cada 10 min =
-- ~50 min por unidad) y Colmillo del Rey Jabalí (el jefe: 15%, una pelea cada 30 min = ~200 min por unidad).
-- Las cantidades se calibraron contra esas tasas (ver MEJORAS.md): las tres armas iniciales piden 1 drop del
-- hunt (~30 min), el equipo de entrada a Zona 2 (Hoja +15 y Levantador +10) 3 Colmillos de Jabalí en total
-- (~90 min, parecido a lo que tarda subir de nivel 1 a 5) y las Hombreras (+16), que llevan el drop del jefe, ~200 min.
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

-- ESCALERA DE ZONAS: el equipo de Zona 1 llega hasta +15 (Hoja de Acero Puro). Antes el Hacha de Hierro MK3
-- (+35) se forjaba acá y con ella la Zona 1 y la 2 quedaban triviales (a nivel 3, +35 mata todo en 1-2
-- golpes); ahora es el arma de afinidad del Guerrero en Zona 2 (seed_zone2_gear_and_recipes.sql).
-- (El Machete de Chacra, el segundo arma general de la zona, ya no existe: con un drop por monstruo sobraba.)
-- Amuleto del Levantador: Común +10 (sell 100 / buy 130). Es lo que tiene la base real y con lo que se calibró la
-- escalera (el amuleto barato con el que se entra a Zona 2); seed_class_gear_and_monster_drops.sql lo traía
-- Épico +15 (60/78), y una instalación limpia y la base real quedaban distintas — ahora las dos convergen acá.
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price) VALUES
('Amuleto del Levantador', 'Amulet', 'Común', 10, 100, 130),
('Hombreras de Cuero Grueso', 'Amulet', 'Raro', 16, 96, 125)
ON CONFLICT (name) DO UPDATE SET
    type = EXCLUDED.type, rarity = EXCLUDED.rarity, stat_value = EXCLUDED.stat_value,
    sell_price = EXCLUDED.sell_price, buy_price = EXCLUDED.buy_price;

CREATE TEMP TABLE z1_recipes (result_name TEXT, gold INTEGER, affinity BOOLEAN);
INSERT INTO z1_recipes VALUES
-- Armas de afinidad: una por clase (Espadas = Guerrero, Dagas = Ninja, Arcos = Arquero, Grimorios = Hechicero)
('Espada de Madera',          40, true),
('Daga Oxidada',              40, true),
('Arco Corto de Sauce',       40, true),
('Grimorio Desgastado',       40, true),
-- Arma general
('Hoja de Acero Puro',       180, false),
-- Amuletos (generales)
('Amuleto del Levantador',   150, false),
('Hombreras de Cuero Grueso', 200, false);

-- Materiales de RECOLECCIÓN (Madera, Piedra, Hierro, Carbón, Oro Puro): las cantidades son ~3x (Común) y ~2x (Raro/Épico)
-- lo de antes porque /chop y /mine ahora dan varias unidades por acción (GameData/GatheringYield: Común 1-5, Raro/Épico
-- 1-3, Legendario/Mítico 1); el ritmo de la run 1 no cambió. Los Legendarios/Míticos de recolección siguen en 1.
CREATE TEMP TABLE z1_ingredients (result_name TEXT, ingredient_name TEXT, quantity INTEGER);
INSERT INTO z1_ingredients VALUES
-- Espada de Madera: solo recolección
('Espada de Madera',          'Madera de Pino',        9),
('Espada de Madera',          'Hierro',                2),
-- Daga Oxidada: la hoja de Hierro, Piedra para afilarla y un Colmillo de Cimarrón como punta (hunt, ~30 min)
('Daga Oxidada',              'Hierro',                2),
('Daga Oxidada',              'Piedra',                6),
('Daga Oxidada',              'Colmillo de Cimarrón',  1),
-- Arco Corto de Sauce: Madera de Pino + una Pluma de Ñandú (la pluma de las flechas; hunt, ~30 min)
('Arco Corto de Sauce',       'Madera de Pino',        9),
('Arco Corto de Sauce',       'Pluma de Ñandú',        1),
-- Grimorio Desgastado: las tapas de Madera y una Pluma de Ñandú para escribir (hunt, ~30 min)
('Grimorio Desgastado',       'Madera de Pino',        6),
('Grimorio Desgastado',       'Pluma de Ñandú',        1),
-- Hoja de Acero Puro (Raro +15): Hierro + empuñadura de Madera + el filo de 2 Colmillos de Jabalí (hunt, ~60 min)
('Hoja de Acero Puro',        'Hierro',                6),
('Hoja de Acero Puro',        'Madera de Pino',        6),
('Hoja de Acero Puro',        'Colmillo de Jabalí',    2),
-- Amuleto del Levantador (+10): Piedra + 1 Colmillo de Jabalí (hunt, ~30 min; 3 con la Hoja = ~90 min)
('Amuleto del Levantador',    'Piedra',                15),
('Amuleto del Levantador',    'Colmillo de Jabalí',    1),
-- Hombreras de Cuero Grueso (Raro +16 DEF; antes Legendario +20): el amuleto "de fondo" de la zona. Bajó de 20 a 16
-- para que los amuletos de Zona 2 (+18 y +24) sean nominalmente MAYORES (escalera de zonas). Es la que lleva el drop
-- del jefe (Colmillo del Rey Jabalí, ~200 min) más 2 Cuero Grueso del Toro Bravo de /travel (~100 min).
('Hombreras de Cuero Grueso', 'Piedra',                9),
('Hombreras de Cuero Grueso', 'Cuero Grueso',          2),
('Hombreras de Cuero Grueso', 'Colmillo del Rey Jabalí', 1);

INSERT INTO recipes (result_item_id, gold_cost, zone_id, affinity)
SELECT i.item_id, r.gold, z.zone_id, r.affinity
FROM z1_recipes r
JOIN items i ON i.name = r.result_name
CROSS JOIN (SELECT zone_id FROM zones WHERE name = 'Praderas del Mate') z
ON CONFLICT (result_item_id) DO UPDATE SET
    gold_cost = EXCLUDED.gold_cost, zone_id = EXCLUDED.zone_id, affinity = EXCLUDED.affinity;

-- Los ingredientes de estas recetas los define ESTE archivo: se borran los que tuvieran (si una receta cambió
-- de ingredientes, el upsert solo agregaría los nuevos y dejaría los viejos colgados).
DELETE FROM recipe_ingredients
WHERE recipe_id IN (
    SELECT rec.recipe_id FROM z1_recipes r
    JOIN items i ON i.name = r.result_name
    JOIN recipes rec ON rec.result_item_id = i.item_id);

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

-- Chequeo rápido: 7 recetas en Zona 1 (4 de afinidad + 1 general + 2 amuletos).
-- SELECT i.name, r.gold_cost, r.affinity FROM recipes r JOIN items i ON i.item_id = r.result_item_id
-- JOIN zones z ON z.zone_id = r.zone_id WHERE z.name = 'Praderas del Mate' ORDER BY r.affinity DESC, i.stat_value;
