-- =========================================================
-- Asado y Acero RPG — REWORK de drops, jefes y recetas (v0.7.0). Diseño: docs/superpowers/specs/2026-10-05-drops-y-recetas-rework-design.md
--
-- Lleva la base al estado final, sea una instalación nueva (va al final de run_fresh_install.sql) o la base viva:
--   · 20 monstruos: 2 de /hunt + 1 de /travel + 1 jefe por zona (sale el Perro Cimarrón de la Zona 1);
--   · 3 drops de material por zona (los 2 de /hunt y el de /travel; chances 6% y 20% sin tocar);
--   · el jefe NO suelta material: su único "drop" es el COFRE de su zona (la chance 100% la primera vez / 40% después
--     vive en GameData/CombatRewardCalculator, no acá);
--   · 6 recetas por zona (30 en total): 4 armas de clase, 1 general SIN familia (no recibe el boost de clase) y 1 amuleto
--     que pide los 3 drops de la zona; ATQ base 9/20/32/50/80 y DEF 10/18/30/46/75 (las de la escalera de zonas);
--   · salen 6 materiales (Colmillo de Cimarrón y los 5 drops de jefe) y 5 amuletos (los "bajos" de cada zona).
-- Las cantidades salen del modelo de Database/report_recipe_pacing.sql con el rendimiento de GatheringYield (Común 1-5, Raro 1-3,
-- Épico 1-2, Legendario/Mítico 1): general ~100 min, de clase ~150, amuleto ~200 (Zona 1 más corto). Se generó desde ese modelo.
--
-- DESTRUCTIVO A PROPÓSITO (el dueño lo autorizó: todo lo de la base viva es reemplazable): borrar un ítem se lleva en cascada las
-- mochilas, recetas y drops que lo usaban, y deja en NULL el arma/amuleto equipado de quien lo llevaba puesto. SIN reembolso. Avisa por
-- NOTICE cuánto se perdió. Hacé un pg_dump antes de correrlo contra una base con jugadores.
--
-- Re-ejecutable (la segunda vez no cambia nada) y FALLA EN VOZ ALTA si algo no quedó como el spec. Va DESPUÉS de seed_boxes.sql
-- (usa los cofres) y ANTES de update_item_emojis.sql. Una vez aplicado, NO se vuelven a correr SOLOS finalize_monster_roster.sql,
-- seed_zone_bosses.sql, seed_recipes.sql ni seed_zoneN_gear_and_recipes.sql: devolverían el estado viejo (o fallarían por los
-- ítems que ya no existen); este archivo va siempre después de ellos.
-- =========================================================

-- 0) Chequeo previo: que existan las zonas, los cofres y todo lo que las recetas nombran.
CREATE TEMP TABLE rw_gear (result_name TEXT, kind TEXT, zone_name TEXT, stat INTEGER, gold INTEGER);
INSERT INTO rw_gear VALUES
    ('Hoja de Acero Puro', 'general', 'Praderas del Mate', 9, 40),
    ('Espada de Madera', 'clase', 'Praderas del Mate', 9, 100),
    ('Daga Oxidada', 'clase', 'Praderas del Mate', 9, 100),
    ('Arco Corto de Sauce', 'clase', 'Praderas del Mate', 9, 100),
    ('Grimorio Desgastado', 'clase', 'Praderas del Mate', 9, 100),
    ('Hombreras de Cuero Grueso', 'amuleto', 'Praderas del Mate', 10, 200),
    ('Cuchilla de Cenizas', 'general', 'Bosque de Cenizas', 20, 300),
    ('Hacha de Hierro MK3', 'clase', 'Bosque de Cenizas', 20, 400),
    ('Colmillo Nocturno', 'clase', 'Bosque de Cenizas', 20, 400),
    ('Arco Élfico Ancestral', 'clase', 'Bosque de Cenizas', 20, 400),
    ('Grimorio de las Tormentas', 'clase', 'Bosque de Cenizas', 20, 400),
    ('Talismán de Ceniza Bendita', 'amuleto', 'Bosque de Cenizas', 18, 400),
    ('Pico de Minero Reforzado', 'general', 'Minas del Yunque', 32, 450),
    ('Mazo de Escoria', 'clase', 'Minas del Yunque', 32, 600),
    ('Dagas de Garra Maldita', 'clase', 'Minas del Yunque', 32, 600),
    ('Boleadoras de Escoria', 'clase', 'Minas del Yunque', 32, 600),
    ('Báculo de Tizón', 'clase', 'Minas del Yunque', 32, 600),
    ('Peto de Escoria Templada', 'amuleto', 'Minas del Yunque', 30, 650),
    ('Lanza de Magma', 'general', 'Cordillera del Fuego', 50, 700),
    ('Facón de Hueso Añejo', 'clase', 'Cordillera del Fuego', 50, 900),
    ('Cuchillos de Ceniza', 'clase', 'Cordillera del Fuego', 50, 900),
    ('Arco de Caza Mayor', 'clase', 'Cordillera del Fuego', 50, 900),
    ('Códice de las Brasas', 'clase', 'Cordillera del Fuego', 50, 900),
    ('Talismán del Volcán', 'amuleto', 'Cordillera del Fuego', 46, 1000),
    ('Martillo del Titán', 'general', 'Cráter de la Escoria', 80, 1100),
    ('Espada del Abismo', 'clase', 'Cráter de la Escoria', 80, 1400),
    ('Colmillo del Cráter', 'clase', 'Cráter de la Escoria', 80, 1400),
    ('Arco del Alma Errante', 'clase', 'Cráter de la Escoria', 80, 1400),
    ('Báculo del Árbol de Vida', 'clase', 'Cráter de la Escoria', 80, 1400),
    ('Corazón de Titán Engarzado', 'amuleto', 'Cráter de la Escoria', 75, 1500);

CREATE TEMP TABLE rw_ingredients (result_name TEXT, ingredient_name TEXT, quantity INTEGER);
INSERT INTO rw_ingredients VALUES
    ('Hoja de Acero Puro', 'Hierro', 2),
    ('Hoja de Acero Puro', 'Colmillo de Jabalí', 1),
    ('Hoja de Acero Puro', 'Pluma de Ñandú', 1),
    ('Espada de Madera', 'Madera de Pino', 12),
    ('Espada de Madera', 'Hierro', 4),
    ('Espada de Madera', 'Colmillo de Jabalí', 2),
    ('Espada de Madera', 'Cuero Grueso', 1),
    ('Daga Oxidada', 'Hierro', 4),
    ('Daga Oxidada', 'Piedra', 10),
    ('Daga Oxidada', 'Colmillo de Jabalí', 2),
    ('Daga Oxidada', 'Cuero Grueso', 1),
    ('Arco Corto de Sauce', 'Madera de Pino', 15),
    ('Arco Corto de Sauce', 'Hierro', 2),
    ('Arco Corto de Sauce', 'Pluma de Ñandú', 3),
    ('Arco Corto de Sauce', 'Cuero Grueso', 1),
    ('Grimorio Desgastado', 'Madera de Pino', 12),
    ('Grimorio Desgastado', 'Piedra', 8),
    ('Grimorio Desgastado', 'Pluma de Ñandú', 3),
    ('Grimorio Desgastado', 'Cuero Grueso', 1),
    ('Hombreras de Cuero Grueso', 'Piedra', 25),
    ('Hombreras de Cuero Grueso', 'Colmillo de Jabalí', 4),
    ('Hombreras de Cuero Grueso', 'Pluma de Ñandú', 4),
    ('Hombreras de Cuero Grueso', 'Cuero Grueso', 2),
    ('Cuchilla de Cenizas', 'Carbón', 4),
    ('Cuchilla de Cenizas', 'Garra de Puma Cenizo', 3),
    ('Cuchilla de Cenizas', 'Esencia Espectral', 3),
    ('Hacha de Hierro MK3', 'Hierro', 6),
    ('Hacha de Hierro MK3', 'Madera de Roble', 6),
    ('Hacha de Hierro MK3', 'Garra de Puma Cenizo', 4),
    ('Hacha de Hierro MK3', 'Ceniza Bendita', 3),
    ('Colmillo Nocturno', 'Hierro', 6),
    ('Colmillo Nocturno', 'Carbón', 3),
    ('Colmillo Nocturno', 'Garra de Puma Cenizo', 4),
    ('Colmillo Nocturno', 'Ceniza Bendita', 3),
    ('Arco Élfico Ancestral', 'Madera de Roble', 12),
    ('Arco Élfico Ancestral', 'Carbón', 3),
    ('Arco Élfico Ancestral', 'Esencia Espectral', 4),
    ('Arco Élfico Ancestral', 'Ceniza Bendita', 3),
    ('Grimorio de las Tormentas', 'Madera de Roble', 12),
    ('Grimorio de las Tormentas', 'Oro Puro', 2),
    ('Grimorio de las Tormentas', 'Esencia Espectral', 4),
    ('Grimorio de las Tormentas', 'Ceniza Bendita', 3),
    ('Talismán de Ceniza Bendita', 'Oro Puro', 4),
    ('Talismán de Ceniza Bendita', 'Garra de Puma Cenizo', 4),
    ('Talismán de Ceniza Bendita', 'Esencia Espectral', 4),
    ('Talismán de Ceniza Bendita', 'Ceniza Bendita', 4),
    ('Pico de Minero Reforzado', 'Hierro', 4),
    ('Pico de Minero Reforzado', 'Yunque Fragmentado', 3),
    ('Pico de Minero Reforzado', 'Gema en Bruto', 3),
    ('Mazo de Escoria', 'Hierro', 6),
    ('Mazo de Escoria', 'Madera de Nogal', 3),
    ('Mazo de Escoria', 'Yunque Fragmentado', 4),
    ('Mazo de Escoria', 'Escoria Metálica Densa', 3),
    ('Dagas de Garra Maldita', 'Hierro', 6),
    ('Dagas de Garra Maldita', 'Oro Puro', 3),
    ('Dagas de Garra Maldita', 'Yunque Fragmentado', 4),
    ('Dagas de Garra Maldita', 'Escoria Metálica Densa', 3),
    ('Boleadoras de Escoria', 'Madera de Nogal', 3),
    ('Boleadoras de Escoria', 'Hierro', 3),
    ('Boleadoras de Escoria', 'Gema en Bruto', 4),
    ('Boleadoras de Escoria', 'Escoria Metálica Densa', 3),
    ('Báculo de Tizón', 'Madera de Nogal', 3),
    ('Báculo de Tizón', 'Oro Puro', 3),
    ('Báculo de Tizón', 'Gema en Bruto', 4),
    ('Báculo de Tizón', 'Escoria Metálica Densa', 3),
    ('Peto de Escoria Templada', 'Oro Puro', 4),
    ('Peto de Escoria Templada', 'Yunque Fragmentado', 4),
    ('Peto de Escoria Templada', 'Gema en Bruto', 4),
    ('Peto de Escoria Templada', 'Escoria Metálica Densa', 4),
    ('Lanza de Magma', 'Hierro', 4),
    ('Lanza de Magma', 'Escama Ígnea', 3),
    ('Lanza de Magma', 'Núcleo de Magma', 3),
    ('Facón de Hueso Añejo', 'Hierro', 6),
    ('Facón de Hueso Añejo', 'Gema de Zafiro', 1),
    ('Facón de Hueso Añejo', 'Escama Ígnea', 4),
    ('Facón de Hueso Añejo', 'Aliento de Fuego Eterno', 3),
    ('Cuchillos de Ceniza', 'Hierro', 6),
    ('Cuchillos de Ceniza', 'Oro Puro', 3),
    ('Cuchillos de Ceniza', 'Escama Ígnea', 4),
    ('Cuchillos de Ceniza', 'Aliento de Fuego Eterno', 3),
    ('Arco de Caza Mayor', 'Madera de Ébano', 1),
    ('Arco de Caza Mayor', 'Madera de Nogal', 3),
    ('Arco de Caza Mayor', 'Núcleo de Magma', 4),
    ('Arco de Caza Mayor', 'Aliento de Fuego Eterno', 3),
    ('Códice de las Brasas', 'Gema de Zafiro', 1),
    ('Códice de las Brasas', 'Madera de Nogal', 3),
    ('Códice de las Brasas', 'Núcleo de Magma', 4),
    ('Códice de las Brasas', 'Aliento de Fuego Eterno', 3),
    ('Talismán del Volcán', 'Gema de Zafiro', 1),
    ('Talismán del Volcán', 'Escama Ígnea', 4),
    ('Talismán del Volcán', 'Núcleo de Magma', 4),
    ('Talismán del Volcán', 'Aliento de Fuego Eterno', 4),
    ('Martillo del Titán', 'Hierro', 4),
    ('Martillo del Titán', 'Corazón de Titán', 3),
    ('Martillo del Titán', 'Fragmento de Alma', 3),
    ('Espada del Abismo', 'Fragmento de Meteorito', 1),
    ('Espada del Abismo', 'Hierro', 6),
    ('Espada del Abismo', 'Corazón de Titán', 4),
    ('Espada del Abismo', 'Ceniza del Abismo', 3),
    ('Colmillo del Cráter', 'Fragmento de Meteorito', 1),
    ('Colmillo del Cráter', 'Hierro', 6),
    ('Colmillo del Cráter', 'Corazón de Titán', 4),
    ('Colmillo del Cráter', 'Ceniza del Abismo', 3),
    ('Arco del Alma Errante', 'Corteza del Árbol de Vida', 1),
    ('Arco del Alma Errante', 'Madera de Ébano', 1),
    ('Arco del Alma Errante', 'Fragmento de Alma', 4),
    ('Arco del Alma Errante', 'Ceniza del Abismo', 3),
    ('Báculo del Árbol de Vida', 'Corteza del Árbol de Vida', 1),
    ('Báculo del Árbol de Vida', 'Madera de Ébano', 1),
    ('Báculo del Árbol de Vida', 'Fragmento de Alma', 4),
    ('Báculo del Árbol de Vida', 'Ceniza del Abismo', 3),
    ('Corazón de Titán Engarzado', 'Gema de Zafiro', 1),
    ('Corazón de Titán Engarzado', 'Corazón de Titán', 4),
    ('Corazón de Titán Engarzado', 'Fragmento de Alma', 4),
    ('Corazón de Titán Engarzado', 'Ceniza del Abismo', 4);

CREATE TEMP TABLE rw_boss_chest (boss_name TEXT, chest_name TEXT);
INSERT INTO rw_boss_chest VALUES
    ('Rey Jabalí', 'Cajón de Pino'),
    ('Lobisón Alfa', 'Baúl de Roble'),
    ('Capataz de Hierro', 'Arcón de Hierro'),
    ('Señor del Volcán', 'Cofre de Oro'),
    ('Soberano de la Escoria', 'Cofre de Oro');

DO $$
DECLARE
    v_missing TEXT;
BEGIN
    SELECT string_agg(DISTINCT n, ', ') INTO v_missing FROM (
        SELECT result_name AS n FROM rw_gear UNION SELECT ingredient_name FROM rw_ingredients UNION SELECT chest_name FROM rw_boss_chest
    ) names WHERE NOT EXISTS (SELECT 1 FROM items i WHERE i.name = names.n);
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'rework_drops_and_recipes: faltan estos ítems en el catálogo: [%]. ¿Se corrieron antes los seeds de zonas, recetas y cajas?', v_missing;
    END IF;

    SELECT string_agg(DISTINCT zone_name, ', ') INTO v_missing FROM rw_gear g WHERE NOT EXISTS (SELECT 1 FROM zones z WHERE z.name = g.zone_name);
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'rework_drops_and_recipes: faltan estas zonas: [%]', v_missing;
    END IF;

    SELECT string_agg(chest_name, ', ') INTO v_missing FROM rw_boss_chest c
    WHERE NOT EXISTS (SELECT 1 FROM items i JOIN boxes b ON b.box_item_id = i.item_id WHERE i.name = c.chest_name);
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'rework_drops_and_recipes: estos cofres no están cargados como caja: [%]', v_missing;
    END IF;
END $$;

-- 1) Zona 1 pasa de 3 monstruos de /hunt a 2 (sus drops se borran en cascada; el ítem se retira más abajo).
DELETE FROM monsters
WHERE name = 'Perro Cimarrón' AND zone_id = (SELECT zone_id FROM zones WHERE name = 'Praderas del Mate');

-- 2) Cada jefe suelta ÚNICAMENTE el cofre de su zona.
DELETE FROM monster_drops
WHERE monster_id IN (SELECT m.monster_id FROM monsters m JOIN rw_boss_chest b ON b.boss_name = m.name WHERE m.is_boss);

INSERT INTO monster_drops (monster_id, item_id)
SELECT m.monster_id, i.item_id
FROM rw_boss_chest b
JOIN monsters m ON m.name = b.boss_name AND m.is_boss
JOIN items i ON i.name = b.chest_name;

-- 3) Stats y precios de las 30 piezas. Venta = lo menor entre 6 x el stat y el 60% del oro de la receta (forjar y vender nunca da
-- ganancia); compra = 1,3 x la venta. Las 5 armas generales quedan sin familia: nadie recibe el boost de clase con ellas.
UPDATE items i
SET stat_value = g.stat,
    sell_price = LEAST(6 * g.stat, g.gold * 6 / 10),
    buy_price  = ROUND(LEAST(6 * g.stat, g.gold * 6 / 10) * 1.3)::INTEGER
FROM rw_gear g
WHERE i.name = g.result_name;

UPDATE items SET weapon_family = NULL
WHERE name IN (SELECT result_name FROM rw_gear WHERE kind = 'general');

-- Las 20 armas de clase son exclusivas de la clase de su familia (hasta ahora solo las de Zona 3 y 4 lo tenían cargado).
UPDATE items SET class_requirement = CASE weapon_family
        WHEN 'Espadas' THEN 'Guerrero' WHEN 'Dagas' THEN 'Ninja' WHEN 'Arcos' THEN 'Arquero' WHEN 'Grimorios' THEN 'Hechicero' END
WHERE name IN (SELECT result_name FROM rw_gear WHERE kind = 'clase') AND weapon_family IS NOT NULL;

-- 4) Recetas: las 30 de arriba, y fuera cualquier otra (las de los 5 amuletos que salen).
INSERT INTO recipes (result_item_id, gold_cost, zone_id, affinity)
SELECT i.item_id, g.gold, z.zone_id, g.kind = 'clase'
FROM rw_gear g
JOIN items i ON i.name = g.result_name
JOIN zones z ON z.name = g.zone_name
ON CONFLICT (result_item_id) DO UPDATE SET
    gold_cost = EXCLUDED.gold_cost, zone_id = EXCLUDED.zone_id, affinity = EXCLUDED.affinity;

DELETE FROM recipes
WHERE result_item_id NOT IN (SELECT i.item_id FROM rw_gear g JOIN items i ON i.name = g.result_name);

-- Los ingredientes los define ESTE archivo: se borran los que tuvieran y se cargan de nuevo.
DELETE FROM recipe_ingredients
WHERE recipe_id IN (
    SELECT rec.recipe_id FROM rw_gear g
    JOIN items i ON i.name = g.result_name
    JOIN recipes rec ON rec.result_item_id = i.item_id);

INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT rec.recipe_id, ing.item_id, a.quantity
FROM rw_ingredients a
JOIN items res ON res.name = a.result_name
JOIN recipes rec ON rec.result_item_id = res.item_id
JOIN items ing ON ing.name = a.ingredient_name;

-- 5) Se retiran los ítems que ya no existen en el diseño. SIN reembolso (ver arriba).
DO $$
DECLARE
    v_retired  TEXT[] := ARRAY['Colmillo de Cimarrón', 'Colmillo del Rey Jabalí', 'Pelaje Plateado del Alfa', 'Yunque del Capataz', 'Brasa Eterna', 'Corazón del Soberano', 'Amuleto del Levantador', 'Mate Tallado en Cenizas', 'Casco de Capataz', 'Coraza de Escamas Ígneas', 'Égida del Devorador'];
    v_holders  INTEGER;
    v_units    BIGINT;
    v_equipped INTEGER;
BEGIN
    SELECT COUNT(DISTINCT discord_id), COALESCE(SUM(quantity), 0) INTO v_holders, v_units
    FROM inventory WHERE item_id IN (SELECT item_id FROM items WHERE name = ANY (v_retired));

    SELECT COUNT(*) INTO v_equipped
    FROM users WHERE weapon_id IN (SELECT item_id FROM items WHERE name = ANY (v_retired))
                  OR amulet_id IN (SELECT item_id FROM items WHERE name = ANY (v_retired));

    RAISE NOTICE 'rework_drops_and_recipes: se retiran % ítem(s); % jugador(es) pierden % unidad(es) de la mochila y % quedan sin su equipo puesto.',
        (SELECT COUNT(*) FROM items WHERE name = ANY (v_retired)), v_holders, v_units, v_equipped;

    DELETE FROM items WHERE name = ANY (v_retired);
END $$;

-- 6) Verificación: el estado final tiene que ser EXACTAMENTE el del diseño.
DO $$
DECLARE
    v_bad TEXT;
    v_n   INTEGER;
BEGIN
    SELECT COUNT(*) INTO v_n FROM monsters;
    IF v_n <> 20 THEN
        RAISE EXCEPTION 'rework_drops_and_recipes: hay % monstruos y tienen que ser 20', v_n;
    END IF;

    SELECT string_agg(z.name || ' (hunt ' || x.hunt || ', travel ' || x.travel || ', jefe ' || x.boss || ')', ', ') INTO v_bad
    FROM zones z
    JOIN (SELECT zone_id,
                 COUNT(*) FILTER (WHERE NOT is_boss AND NOT is_travel) AS hunt,
                 COUNT(*) FILTER (WHERE is_travel) AS travel,
                 COUNT(*) FILTER (WHERE is_boss) AS boss
          FROM monsters GROUP BY zone_id) x USING (zone_id)
    WHERE z.zone_id > 0 AND NOT (x.hunt = 2 AND x.travel = 1 AND x.boss = 1);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rework_drops_and_recipes: estas zonas no tienen 2 de hunt + 1 de travel + 1 jefe: [%]', v_bad;
    END IF;

    SELECT string_agg(m.name, ', ') INTO v_bad
    FROM monsters m
    WHERE (SELECT COUNT(*) FROM monster_drops d JOIN items i USING (item_id) WHERE d.monster_id = m.monster_id
             AND i.type = CASE WHEN m.is_boss THEN 'Caja' ELSE 'Material' END) <> 1
       OR (SELECT COUNT(*) FROM monster_drops d WHERE d.monster_id = m.monster_id) <> 1;
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rework_drops_and_recipes: estos monstruos no tienen exactamente 1 drop del tipo que corresponde (Material, o Caja si es jefe): [%]', v_bad;
    END IF;

    SELECT string_agg(b.boss_name, ', ') INTO v_bad FROM rw_boss_chest b
    WHERE NOT EXISTS (SELECT 1 FROM monsters m JOIN monster_drops d USING (monster_id) JOIN items i USING (item_id)
                      WHERE m.name = b.boss_name AND m.is_boss AND i.name = b.chest_name);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rework_drops_and_recipes: estos jefes no sueltan su cofre: [%]', v_bad;
    END IF;

    SELECT COUNT(*) INTO v_n FROM recipes;
    IF v_n <> 30 THEN
        RAISE EXCEPTION 'rework_drops_and_recipes: hay % recetas y tienen que ser 30', v_n;
    END IF;

    SELECT string_agg(z.name, ', ') INTO v_bad
    FROM zones z
    JOIN (SELECT r.zone_id,
                 COUNT(*) FILTER (WHERE r.affinity AND i.type = 'Weapon') AS clase,
                 COUNT(*) FILTER (WHERE NOT r.affinity AND i.type = 'Weapon') AS general,
                 COUNT(*) FILTER (WHERE i.type = 'Amulet') AS amuleto
          FROM recipes r JOIN items i ON i.item_id = r.result_item_id GROUP BY r.zone_id) x USING (zone_id)
    WHERE z.zone_id > 0 AND NOT (x.clase = 4 AND x.general = 1 AND x.amuleto = 1);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rework_drops_and_recipes: estas zonas no tienen 4 armas de clase + 1 general + 1 amuleto: [%]', v_bad;
    END IF;

    -- Cada receta tiene EXACTAMENTE los ingredientes de la tabla (ni uno de menos ni uno de más), el oro, la zona y el stat.
    SELECT string_agg(x.d, '; ') INTO v_bad FROM (
        SELECT 'falta ' || a.result_name || ' <- ' || a.quantity || 'x ' || a.ingredient_name AS d
        FROM rw_ingredients a
        WHERE NOT EXISTS (SELECT 1 FROM recipe_ingredients ri JOIN recipes rec USING (recipe_id)
                          JOIN items res ON res.item_id = rec.result_item_id JOIN items ing ON ing.item_id = ri.item_id
                          WHERE res.name = a.result_name AND ing.name = a.ingredient_name AND ri.quantity = a.quantity)
        UNION ALL
        SELECT 'sobra ' || res.name || ' <- ' || ri.quantity || 'x ' || ing.name
        FROM recipe_ingredients ri JOIN recipes rec USING (recipe_id)
        JOIN items res ON res.item_id = rec.result_item_id JOIN items ing ON ing.item_id = ri.item_id
        WHERE NOT EXISTS (SELECT 1 FROM rw_ingredients a WHERE a.result_name = res.name AND a.ingredient_name = ing.name AND a.quantity = ri.quantity)
        UNION ALL
        SELECT 'receta distinta ' || g.result_name
        FROM rw_gear g
        WHERE NOT EXISTS (SELECT 1 FROM recipes rec JOIN items i ON i.item_id = rec.result_item_id JOIN zones z ON z.zone_id = rec.zone_id
                          WHERE i.name = g.result_name AND z.name = g.zone_name AND rec.gold_cost = g.gold AND rec.affinity = (g.kind = 'clase')
                            AND i.stat_value = g.stat)
    ) x;
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rework_drops_and_recipes: las recetas no quedaron como el diseño: [%]', v_bad;
    END IF;

    SELECT string_agg(i.name, ', ') INTO v_bad FROM items i
    WHERE (i.name IN (SELECT result_name FROM rw_gear WHERE kind = 'general') AND i.weapon_family IS NOT NULL)
       OR (i.name IN (SELECT result_name FROM rw_gear WHERE kind = 'clase') AND (i.weapon_family IS NULL OR i.class_requirement IS NULL));
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rework_drops_and_recipes: familia/clase mal en: [%] (los generales sin familia, las de clase con familia y clase)', v_bad;
    END IF;

    SELECT string_agg(name, ', ') INTO v_bad FROM items
    WHERE name = ANY (ARRAY['Colmillo de Cimarrón', 'Colmillo del Rey Jabalí', 'Pelaje Plateado del Alfa', 'Yunque del Capataz', 'Brasa Eterna', 'Corazón del Soberano', 'Amuleto del Levantador', 'Mate Tallado en Cenizas', 'Casco de Capataz', 'Coraza de Escamas Ígneas', 'Égida del Devorador']);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rework_drops_and_recipes: siguen existiendo ítems que tenían que salir: [%]', v_bad;
    END IF;
END $$;

DROP TABLE rw_boss_chest;
DROP TABLE rw_ingredients;
DROP TABLE rw_gear;

-- Chequeo rápido: las recetas por zona.
-- SELECT z.min_level, CASE WHEN r.affinity THEN 'clase' WHEN i.type = 'Weapon' THEN 'general' ELSE 'amuleto' END AS tipo, i.name, i.stat_value, r.gold_cost
-- FROM recipes r JOIN items i ON i.item_id = r.result_item_id JOIN zones z USING (zone_id) ORDER BY z.min_level, tipo, i.name;
