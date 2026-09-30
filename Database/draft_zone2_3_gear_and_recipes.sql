-- =========================================================
-- ⚠️ BORRADOR — NO forma parte de la instalación (no está en run_fresh_install.sql) ni se corre solo.
-- Se guardó para no perder el diseño cuando se decidió dejar SOLO Zona 1 con el molde por zona de
-- seed_recipes.sql (4 armas de afinidad + 2 generales + 2 amuletos = 8 recetas por zona, y cada jugador
-- ve solo las de su zona). Para reincorporarlo hay que REHACERLO con ese molde: ítems como estos pero
-- 4 de afinidad (1 por clase) + 2 generales + 2 amuletos por zona, y cargando recipes.affinity — este
-- borrador NO setea affinity (queda en false). Sirve de punto de partida para las zonas 2 y 3.
--
-- Contenido: equipo de MEJOR NIVEL con recetas de zonas 2 y 3: 8 ítems nuevos (6 armas, 2
-- amuletos) cuyas recetas piden lo que más cuesta farmear — Madera de Ébano y Gema de Zafiro (Legendarios
-- de /chop y /mine, ~4.5% por acción), drops de las zonas altas y de sus jefes, y en el tope una Corteza
-- del Árbol de Vida (Mítico, ~0.5% por /chop: un objetivo de largo plazo, no algo que se consiga de casualidad).
--
-- Si se corriera: DESPUÉS de seed_zones_and_monsters.sql, seed_zone_bosses.sql (drops de los jefes) y
-- seed_recipes.sql. Re-ejecutable: los ítems y las recetas se pisan por nombre (UNIQUE), no se duplican.
--
-- A DIFERENCIA de los otros seeds de recetas, este FALLA EN VOZ ALTA (RAISE EXCEPTION) si alguna receta
-- referencia un ítem o zona que no existe, en vez de omitirla en silencio o crearla sin ese ingrediente
-- (el problema recurrente que documenta CLAUDE.md — ver la nota de orden en run_fresh_install.sql).
--
-- Escala: zona 2 = Legendario +60 ATQ / +35 DEF, zona 3 = +72 ATQ / +45 DEF, y un Mítico +85. Por encima de
-- los +55 de las armas Legendarias que ya existían (esas usan drops de Zona 1). El oro de cada receta está
-- por encima del precio de VENTA del resultado (260/200 en zona 2, 320/240 en zona 3, 650 el Mítico) para
-- que forjar y revender no sea negocio.
-- =========================================================

-- ---------------------------------------------------------
-- Ítems (upsert por nombre: si se corre de nuevo, los valores de acá mandan)
-- ---------------------------------------------------------
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price, weapon_family) VALUES
-- Zona 2 — Bosque de Cenizas: una por familia + un amuleto
('Espadón de Zafiro',           'Weapon', 'Legendario', 60, 260, 340, 'Espadas'),
('Puñal de Ébano',              'Weapon', 'Legendario', 60, 260, 340, 'Dagas'),
('Arco de Ébano',               'Weapon', 'Legendario', 60, 260, 340, 'Arcos'),
('Grimorio de Zafiro',          'Weapon', 'Legendario', 60, 260, 340, 'Grimorios'),
-- Zona 3 — Minas del Yunque
('Maza del Yunque',             'Weapon', 'Legendario', 72, 320, 415, 'Espadas'),
-- Mítico: el objetivo de largo plazo
('Báculo del Árbol de Vida',    'Weapon', 'Mítico',     85, 650, 845, 'Grimorios')
ON CONFLICT (name) DO UPDATE SET
    type = EXCLUDED.type, rarity = EXCLUDED.rarity, stat_value = EXCLUDED.stat_value,
    sell_price = EXCLUDED.sell_price, buy_price = EXCLUDED.buy_price, weapon_family = EXCLUDED.weapon_family;

-- Amuletos (sin weapon_family)
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price) VALUES
('Talismán de Ceniza Bendita',  'Amulet', 'Legendario', 35, 200, 260),
('Coraza de Escoria',           'Amulet', 'Legendario', 45, 240, 310)
ON CONFLICT (name) DO UPDATE SET
    type = EXCLUDED.type, rarity = EXCLUDED.rarity, stat_value = EXCLUDED.stat_value,
    sell_price = EXCLUDED.sell_price, buy_price = EXCLUDED.buy_price;

-- ---------------------------------------------------------
-- Recetas: (resultado, oro, zona) y sus ingredientes (resultado, ingrediente, cantidad).
-- Se cargan en tablas temporales para poder VERIFICAR al final que no se perdió ninguna fila.
-- ---------------------------------------------------------
CREATE TEMP TABLE adv_recipes (result_name TEXT, gold INTEGER, zone_name TEXT);
INSERT INTO adv_recipes VALUES
('Arco de Ébano',              500, 'Bosque de Cenizas'),
('Puñal de Ébano',             500, 'Bosque de Cenizas'),
('Espadón de Zafiro',          500, 'Bosque de Cenizas'),
('Grimorio de Zafiro',         500, 'Bosque de Cenizas'),
('Talismán de Ceniza Bendita', 400, 'Bosque de Cenizas'),
('Maza del Yunque',            700, 'Minas del Yunque'),
('Coraza de Escoria',          600, 'Minas del Yunque'),
('Báculo del Árbol de Vida',   900, 'Minas del Yunque');

CREATE TEMP TABLE adv_ingredients (result_name TEXT, ingredient_name TEXT, quantity INTEGER);
INSERT INTO adv_ingredients VALUES
-- Arco de Ébano: la madera noble + zarpas y ramas del bosque quemado
('Arco de Ébano',              'Madera de Ébano',          2),
('Arco de Ébano',              'Garra de Puma Cenizo',     2),
('Arco de Ébano',              'Rama Carbonizada',         1),
-- Puñal de Ébano: mango de Ébano, hoja de Hierro, filo espectral
('Puñal de Ébano',             'Madera de Ébano',          1),
('Puñal de Ébano',             'Hierro',                   3),
('Puñal de Ébano',             'Esencia Espectral',        2),
-- Espadón de Zafiro: hoja con Zafiro engarzado
('Espadón de Zafiro',          'Gema de Zafiro',           2),
('Espadón de Zafiro',          'Hierro',                   3),
('Espadón de Zafiro',          'Garra de Puma Cenizo',     2),
-- Grimorio de Zafiro: un libro con la cubierta de gema, tinta de ceniza bendita
('Grimorio de Zafiro',         'Gema de Zafiro',           2),
('Grimorio de Zafiro',         'Ceniza Bendita',           2),
('Grimorio de Zafiro',         'Esencia Espectral',        1),
-- Talismán de Ceniza Bendita (amuleto)
('Talismán de Ceniza Bendita', 'Gema de Zafiro',           1),
('Talismán de Ceniza Bendita', 'Ceniza Bendita',           3),
('Talismán de Ceniza Bendita', 'Oro Puro',                 1),
-- Maza del Yunque: fragmentos del yunque + el martillo del jefe (Capataz de Hierro)
('Maza del Yunque',            'Yunque Fragmentado',       2),
('Maza del Yunque',            'Martillo del Capataz',     1),
('Maza del Yunque',            'Hierro',                   3),
('Maza del Yunque',            'Gema de Zafiro',           1),
-- Coraza de Escoria (amuleto): escoria densa + el yunque del jefe
('Coraza de Escoria',          'Escoria Metálica Densa',   3),
('Coraza de Escoria',          'Yunque del Capataz',       1),
('Coraza de Escoria',          'Gema en Bruto',            2),
-- Báculo del Árbol de Vida: el objetivo de largo plazo (1 Corteza Mítica, ~0.5% por /chop)
('Báculo del Árbol de Vida',   'Corteza del Árbol de Vida', 1),
('Báculo del Árbol de Vida',   'Madera de Ébano',          2),
('Báculo del Árbol de Vida',   'Gema de Zafiro',           2),
('Báculo del Árbol de Vida',   'Polvo de Mina Sagrada',    2);

INSERT INTO recipes (result_item_id, gold_cost, zone_id)
SELECT i.item_id, r.gold, z.zone_id
FROM adv_recipes r
JOIN items i ON i.name = r.result_name
JOIN zones z ON z.name = r.zone_name
ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost, zone_id = EXCLUDED.zone_id;

INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT rec.recipe_id, ing.item_id, a.quantity
FROM adv_ingredients a
JOIN items res ON res.name = a.result_name
JOIN recipes rec ON rec.result_item_id = res.item_id
JOIN items ing ON ing.name = a.ingredient_name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- Verificación: cada receta y cada ingrediente de arriba TIENE que haberse cargado. Si un nombre no
-- existe (ítem, zona), el JOIN de arriba lo omitió en silencio — acá se convierte en un error.
DO $$
DECLARE
    missing_recipes     TEXT;
    missing_ingredients TEXT;
BEGIN
    SELECT string_agg(r.result_name, ', ') INTO missing_recipes
    FROM adv_recipes r
    WHERE NOT EXISTS (
        SELECT 1 FROM recipes rec JOIN items i ON i.item_id = rec.result_item_id
        JOIN zones z ON z.zone_id = rec.zone_id
        WHERE i.name = r.result_name AND z.name = r.zone_name AND rec.gold_cost = r.gold);

    SELECT string_agg(a.result_name || ' <- ' || a.ingredient_name, ', ') INTO missing_ingredients
    FROM adv_ingredients a
    WHERE NOT EXISTS (
        SELECT 1 FROM recipe_ingredients ri
        JOIN recipes rec ON rec.recipe_id = ri.recipe_id
        JOIN items res ON res.item_id = rec.result_item_id
        JOIN items ing ON ing.item_id = ri.item_id
        WHERE res.name = a.result_name AND ing.name = a.ingredient_name AND ri.quantity = a.quantity);

    IF missing_recipes IS NOT NULL OR missing_ingredients IS NOT NULL THEN
        RAISE EXCEPTION 'seed_advanced_gear_and_recipes: no se pudieron cargar recetas (revisar que existan los ítems/zonas y el orden de los scripts). Recetas: [%]. Ingredientes: [%]',
            COALESCE(missing_recipes, '-'), COALESCE(missing_ingredients, '-');
    END IF;
END $$;

DROP TABLE adv_ingredients;
DROP TABLE adv_recipes;

-- Chequeo rápido: 8 recetas nuevas, cada una con sus ingredientes.
-- SELECT i.name, r.gold_cost, z.name AS zona FROM recipes r JOIN items i ON i.item_id = r.result_item_id
-- JOIN zones z ON z.zone_id = r.zone_id WHERE z.name <> 'Praderas del Mate' ORDER BY z.min_level, i.name;
