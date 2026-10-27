-- =========================================================
-- v0.30.2: las recetas de RECOLECCIÓN suben, porque con las herramientas (v0.29.0) y el Fuego Nuevo se junta mucho más material que cuando se calibraron (el dueño, 2026-10-28: «el pico y el hacha salen muy baratos
-- para como multiplican las cosas, pedí más; y la receta para ir a la zona 0 tiene que pedir mucha piedra y madera, tipo 300 y 300… una persona FN2 sacó 27 maderas recién»):
--   · Las 4 herramientas (Hacha / Pico de Hierro Forjado y de Escoria Templada) piden el TRIPLE que antes (solo materiales de recolección, sin oro).
--   · El Trinche del Asador Eterno suma 300 de Madera de Pino y la Brasa del Fogón Eterno 300 de Piedra (las dos piezas que hay que llevar puestas para entrar a la Zona 0). Pasan a tener 9 ingredientes.
-- Para una base que ya estaba andando (database/add_tools.sql y seed_fogon.sql ya traen estos números, así que una instalación nueva NO necesita correr esto). Valores ABSOLUTOS y re-ejecutable: correrlo dos veces deja lo mismo.
-- No toca lo que los jugadores ya forjaron ni lo que tienen en la mochila. No hace falta correrlo antes del binario (es solo datos), pero hasta que se corra las recetas siguen con los números viejos.
-- Se corre con psql (NO con el panel de consultas de Railway, que le agrega un LIMIT).
-- =========================================================

DROP TABLE IF EXISTS rb_gather;
CREATE TEMP TABLE rb_gather (result_name TEXT, item_name TEXT, quantity INTEGER);
INSERT INTO rb_gather VALUES
    ('Hacha de Hierro Forjado',   'Madera de Roble', 12),
    ('Hacha de Hierro Forjado',   'Madera de Nogal',  3),
    ('Hacha de Hierro Forjado',   'Hierro',           9),
    ('Hacha de Hierro Forjado',   'Piedra',          18),
    ('Pico de Hierro Forjado',    'Hierro',           6),
    ('Pico de Hierro Forjado',    'Carbón',           6),
    ('Pico de Hierro Forjado',    'Madera de Roble',  9),
    ('Pico de Hierro Forjado',    'Piedra',          18),
    ('Hacha de Escoria Templada', 'Madera de Ébano',  3),
    ('Hacha de Escoria Templada', 'Madera de Nogal',  6),
    ('Hacha de Escoria Templada', 'Hierro',           9),
    ('Hacha de Escoria Templada', 'Carbón',           6),
    ('Pico de Escoria Templada',  'Gema de Zafiro',   3),
    ('Pico de Escoria Templada',  'Oro Puro',         3),
    ('Pico de Escoria Templada',  'Hierro',           9),
    ('Pico de Escoria Templada',  'Madera de Roble', 12),
    ('Trinche del Asador Eterno', 'Madera de Pino',  300),
    ('Brasa del Fogón Eterno',    'Piedra',          300);

DO $$
DECLARE
    v_missing TEXT;
BEGIN
    -- Una receta o un ingrediente que no existe se omitiría en silencio: se avisa en voz alta.
    SELECT string_agg(DISTINCT g.result_name, ', ') INTO v_missing
    FROM rb_gather g WHERE NOT EXISTS (SELECT 1 FROM items i JOIN recipes r ON r.result_item_id = i.item_id WHERE i.name = g.result_name);
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'rebalance_gather_recipes: no existe la receta de [%] (¿falta correr add_tools.sql o seed_fogon.sql?)', v_missing;
    END IF;
    SELECT string_agg(DISTINCT g.item_name, ', ') INTO v_missing FROM rb_gather g WHERE NOT EXISTS (SELECT 1 FROM items i WHERE i.name = g.item_name);
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'rebalance_gather_recipes: faltan estos materiales en el catálogo: [%]', v_missing;
    END IF;
END $$;

-- UPSERT: las herramientas ya tienen esas filas (se les cambia la cantidad) y el Trinche y la Brasa suman la fila nueva.
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT r.recipe_id, ing.item_id, g.quantity
FROM rb_gather g
JOIN items res ON res.name = g.result_name
JOIN recipes r ON r.result_item_id = res.item_id
JOIN items ing ON ing.name = g.item_name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

DO $$
DECLARE
    v_bad TEXT;
BEGIN
    -- Las herramientas: 4 ingredientes y las cantidades de arriba; el Fogón: 9 ingredientes cada pieza.
    SELECT string_agg(i.name, ', ') INTO v_bad
    FROM recipes r JOIN items i ON i.item_id = r.result_item_id
    WHERE (i.type IN ('Hacha', 'Pico') AND (SELECT count(*) FROM recipe_ingredients ri WHERE ri.recipe_id = r.recipe_id) <> 4)
       OR (i.name IN ('Trinche del Asador Eterno', 'Brasa del Fogón Eterno') AND (SELECT count(*) FROM recipe_ingredients ri WHERE ri.recipe_id = r.recipe_id) <> 9);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rebalance_gather_recipes: estas recetas no quedaron con los ingredientes esperados: [%]', v_bad;
    END IF;

    SELECT string_agg(g.result_name || ' / ' || g.item_name, ', ') INTO v_bad
    FROM rb_gather g
    JOIN items res ON res.name = g.result_name JOIN recipes r ON r.result_item_id = res.item_id
    JOIN items ing ON ing.name = g.item_name
    LEFT JOIN recipe_ingredients ri ON ri.recipe_id = r.recipe_id AND ri.item_id = ing.item_id
    WHERE ri.quantity IS DISTINCT FROM g.quantity;
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rebalance_gather_recipes: cantidades que no quedaron como se pidió: [%]', v_bad;
    END IF;
END $$;

DROP TABLE IF EXISTS rb_gather;
