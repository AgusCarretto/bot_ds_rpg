-- =========================================================
-- v0.29.0: las HERRAMIENTAS (hacha y pico) y el Carpintero que las arma (/tools, aa herramientas|equipamiento). Sirve para una base que ya estaba andando Y para la instalación nueva
-- (run_fresh_install.sql lo corre al final, después de los materiales y las recetas). Re-ejecutable. schema.sql ya trae la tabla player_tools.
--
--   · Sin herramienta se recolecta ×1 (lo de siempre). Hay DOS tiers por herramienta: el de la Zona 3 (×2) y el de la Zona 5 (×3), que se arman con el Carpintero apenas esa zona está desbloqueada.
--     Multiplican la cantidad de /chop (hacha) y de /mine (pico) en Común, Raro y Épico; Legendario y Mítico siguen dando 1 (GameData/ToolRules.cs). Se ponen solas (como el arma) y la nueva reemplaza a la vieja.
--   · SOLO materiales de recolección, sin oro (el dueño: «solo con materiales se haga la herramienta»). La cantidad de cada material sale del modelo de report_recipe_pacing.sql con el cooldown de 5 min de
--     /chop y /mine y las chances de RarityCatalog.GatheringWeights (por uso: Pino 2,04 u, Roble 0,42, Nogal 0,105, Ébano 0,035; Piedra 2,04, Hierro 0,21, Carbón 0,21, Oro Puro 0,105, Zafiro 0,035):
--       Hacha de Hierro Forjado (Z3)   ~110 min de /chop y ~80 de /mine     Pico de Hierro Forjado (Z3)    ~110 min de /mine y ~35 de /chop
--       Hacha de Escoria Templada (Z5) ~240 min de /chop y ~120 de /mine    Pico de Escoria Templada (Z5)  ~260 min de /mine y ~50 de /chop
--     (/chop y /mine tienen cooldown aparte: corren en paralelo, así que el tiempo es el del más largo).
--   · Un ítem por herramienta: type 'Hacha' o 'Pico' (de ahí sale el tipo de herramienta), stat_value = el multiplicador (2 o 3), rareza Épico (Z3) o Mítico (Z5); no se compran ni se venden.
--     La receta guarda en zone_id la zona que la desbloquea. Las recetas de herramientas NO las ven /forge ni el recetario (RecipeCatalog.IsVisibleTo las saca): las arma el Carpintero.
--   · El emoji de cada una se carga en update_item_emojis.sql cuando haya arte (NULL no rompe nada).
--
-- Se corre con psql (NO con el panel de consultas de Railway, que le agrega un LIMIT). Va ANTES del binario v0.29.0: PlayerBonusService lee player_tools en cada recolección y cada pelea
-- (si falta la tabla loguea el aviso y sigue sin herramientas, pero nadie puede armar nada).
-- =========================================================

CREATE TABLE IF NOT EXISTS player_tools (
    discord_id BIGINT NOT NULL REFERENCES users(discord_id) ON DELETE CASCADE,
    kind       TEXT   NOT NULL CHECK (kind IN ('axe', 'pickaxe')),
    item_id    INTEGER NOT NULL REFERENCES items(item_id) ON DELETE CASCADE,
    PRIMARY KEY (discord_id, kind)
);

-- Las herramientas (no toca el emoji si ya se lo cargaron).
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price, weapon_family, class_requirement)
VALUES
    ('Hacha de Hierro Forjado',   'Hacha', 'Épico',  2, 0, 0, NULL, NULL),
    ('Pico de Hierro Forjado',    'Pico',  'Épico',  2, 0, 0, NULL, NULL),
    ('Hacha de Escoria Templada', 'Hacha', 'Mítico', 3, 0, 0, NULL, NULL),
    ('Pico de Escoria Templada',  'Pico',  'Mítico', 3, 0, 0, NULL, NULL)
ON CONFLICT (name) DO UPDATE
    SET type = EXCLUDED.type, rarity = EXCLUDED.rarity, stat_value = EXCLUDED.stat_value, sell_price = EXCLUDED.sell_price,
        buy_price = EXCLUDED.buy_price, weapon_family = EXCLUDED.weapon_family, class_requirement = EXCLUDED.class_requirement;

DROP TABLE IF EXISTS tool_ingredients;
CREATE TEMP TABLE tool_ingredients (result_name TEXT, item_name TEXT, quantity INTEGER, zone_rank INTEGER);
INSERT INTO tool_ingredients VALUES
    ('Hacha de Hierro Forjado',   'Madera de Roble',  4, 3),
    ('Hacha de Hierro Forjado',   'Madera de Nogal',  1, 3),
    ('Hacha de Hierro Forjado',   'Hierro',           3, 3),
    ('Hacha de Hierro Forjado',   'Piedra',           6, 3),
    ('Pico de Hierro Forjado',    'Hierro',           2, 3),
    ('Pico de Hierro Forjado',    'Carbón',           2, 3),
    ('Pico de Hierro Forjado',    'Madera de Roble',  3, 3),
    ('Pico de Hierro Forjado',    'Piedra',           6, 3),
    ('Hacha de Escoria Templada', 'Madera de Ébano',  1, 5),
    ('Hacha de Escoria Templada', 'Madera de Nogal',  2, 5),
    ('Hacha de Escoria Templada', 'Hierro',           3, 5),
    ('Hacha de Escoria Templada', 'Carbón',           2, 5),
    ('Pico de Escoria Templada',  'Gema de Zafiro',   1, 5),
    ('Pico de Escoria Templada',  'Oro Puro',         1, 5),
    ('Pico de Escoria Templada',  'Hierro',           3, 5),
    ('Pico de Escoria Templada',  'Madera de Roble',  4, 5);

DO $$
DECLARE
    v_missing TEXT;
BEGIN
    -- Un ingrediente que no existe se omitiría en silencio (la receta nacería sin él): se avisa en voz alta.
    SELECT string_agg(DISTINCT t.item_name, ', ') INTO v_missing FROM tool_ingredients t WHERE NOT EXISTS (SELECT 1 FROM items i WHERE i.name = t.item_name);
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'add_tools: faltan estos materiales en el catálogo: [%]', v_missing;
    END IF;
    -- Solo materiales de recolección (la herramienta no pide drops de monstruos ni oro).
    SELECT string_agg(DISTINCT t.item_name, ', ') INTO v_missing FROM tool_ingredients t JOIN items i ON i.name = t.item_name WHERE i.type NOT IN ('Madera', 'Mineral');
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'add_tools: estos ingredientes no son de recolección: [%]', v_missing;
    END IF;
END $$;

-- Las recetas: sin oro, en la zona que las desbloquea (el rank 3 y el 5 de la escalera por nivel, nunca por zone_id crudo).
INSERT INTO recipes (result_item_id, gold_cost, zone_id, affinity)
SELECT i.item_id, 0,
       (SELECT z.zone_id FROM zones z WHERE z.kind = 'normal' ORDER BY z.min_level, z.zone_id OFFSET (MAX(t.zone_rank) - 1) LIMIT 1),
       false
FROM tool_ingredients t JOIN items i ON i.name = t.result_name
GROUP BY i.item_id
ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost, zone_id = EXCLUDED.zone_id, affinity = EXCLUDED.affinity;

-- Los ingredientes se reescriben enteros (una receta cambiada nunca conserva filas viejas).
DELETE FROM recipe_ingredients WHERE recipe_id IN (SELECT r.recipe_id FROM recipes r JOIN items i ON i.item_id = r.result_item_id WHERE i.type IN ('Hacha', 'Pico'));
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT r.recipe_id, ing.item_id, t.quantity
FROM tool_ingredients t
JOIN items res ON res.name = t.result_name
JOIN recipes r ON r.result_item_id = res.item_id
JOIN items ing ON ing.name = t.item_name;

DO $$
DECLARE
    v_n INTEGER;
    v_bad TEXT;
BEGIN
    SELECT COUNT(*) INTO v_n FROM recipes r JOIN items i ON i.item_id = r.result_item_id WHERE i.type IN ('Hacha', 'Pico');
    IF v_n <> 4 THEN
        RAISE EXCEPTION 'add_tools: hay % recetas de herramientas y tienen que ser 4', v_n;
    END IF;
    SELECT string_agg(i.name, ', ') INTO v_bad
    FROM recipes r JOIN items i ON i.item_id = r.result_item_id
    WHERE i.type IN ('Hacha', 'Pico') AND (r.gold_cost <> 0 OR r.zone_id IS NULL OR (SELECT COUNT(*) FROM recipe_ingredients ri WHERE ri.recipe_id = r.recipe_id) <> 4);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'add_tools: estas herramientas no quedaron con 0 de oro, su zona y 4 ingredientes: [%]', v_bad;
    END IF;
END $$;
