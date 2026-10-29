-- =========================================================
-- v0.48.0: la PESCA (/fish, aa fish|pescar). Sirve para una base que ya estaba andando Y para la instalación nueva (run_fresh_install.sql lo corre justo después de add_tools.sql).
-- Re-ejecutable. No hace falta ninguna tabla nueva: el oficio Pescador y sus contadores (fish, fish_xp, fish_caught) salen de player_stats como los demás oficios.
--
--   · TRES PECES (items.type 'Pescado'), uno por rareza, que salen de /fish con su propia tabla de rarezas (GameData/RarityCatalog.cs: Común 78 % / Raro 19 % / Épico 3 %):
--       Mojarra (Común, sale 1 a 5 por tirada, se vende a 2) · Dorado (Raro, 1 a 3, se vende a 10) · Surubí (Épico, 1 a 2, se vende a 30)
--     Los precios son los de Pino / Roble / Nogal, que tienen la misma rareza y el mismo rinde. Se pueden vender y desmantelar (Polvo por rareza, GameData/Dismantling.cs). El pescado NO se comercia con otros
--     jugadores (/trade es de madera y minerales). Pensados para la COCINA (etapa 2, todavía no construida): el dueño quiere cocinar con pescado y carbón para tener bufos.
--   · LA CAÑA, la tercera herramienta del Carpintero (items.type 'Caña', player_tools.kind 'rod'), con dos tiers como el hacha y el pico: la de la Zona 3 (×2) y la de la Zona 5 (×3), solo con materiales de recolección.
--     Multiplican la cantidad de /fish en Común, Raro y Épico (los tres peces). Piden menos que el hacha y el pico porque la pesca sola no abre ninguna receta todavía:
--       Caña de Hierro Forjado (Z3)    ~2,4 h de /mine y ~1,9 h de /chop   (el Hierro manda: 6 al 0,21 por uso)
--       Caña de Escoria Templada (Z5)  ~5,6 h de /mine y ~5,6 h de /chop   (el Zafiro y el Nogal mandan)
--     (modelo de report_recipe_pacing.sql con las chances de v0.38.3: Roble 0,42 u por /chop, Nogal 0,09, Hierro 0,21, Piedra 2,085, Zafiro 0,03.)
--   · player_tools.kind admite 'rod' (el CHECK de schema.sql ya lo trae; acá se reemplaza el viejo en una base que ya tenía la tabla).
--   · Los emojis de los peces y de las cañas son PROVISORIOS (unicode): el dueño sube el arte propio como emoji de aplicación y se conecta en update_item_emojis.sql, que pisa estos
--     (acá nunca se pisa un emoji que ya esté cargado).
--
-- Se corre con psql (NO con el panel de consultas de Railway, que le agrega un LIMIT). Necesita add_tools.sql (la tabla player_tools y los materiales). Va ANTES del binario v0.48.0 si querés que
-- /fish ande el primer día: sin los peces en el catálogo contesta que todavía no hay materiales cargados.
-- =========================================================

DO $$
BEGIN
    IF to_regclass('player_tools') IS NULL THEN
        RAISE EXCEPTION 'add_fishing: falta la tabla player_tools; corré antes add_tools.sql';
    END IF;
END $$;

-- La caña es un tipo de herramienta más: el CHECK de la columna kind se reemplaza (en una base nueva ya viene con 'rod').
DO $$
DECLARE
    v_name TEXT;
BEGIN
    FOR v_name IN
        SELECT conname FROM pg_constraint
        WHERE conrelid = 'player_tools'::regclass AND contype = 'c' AND pg_get_constraintdef(oid) LIKE '%kind%'
    LOOP
        EXECUTE format('ALTER TABLE player_tools DROP CONSTRAINT %I', v_name);
    END LOOP;
    ALTER TABLE player_tools ADD CONSTRAINT player_tools_kind_check CHECK (kind IN ('axe', 'pickaxe', 'rod'));
END $$;

-- Los peces y las cañas. No toca el emoji si ya se lo cargaron.
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price, weapon_family, class_requirement, emoji)
VALUES
    ('Mojarra',                  'Pescado', 'Común',  0,  2,  3,  NULL, NULL, '🐟'),
    ('Dorado',                   'Pescado', 'Raro',   0, 10, 13,  NULL, NULL, '🐠'),
    ('Surubí',                   'Pescado', 'Épico',  0, 30, 39,  NULL, NULL, '👑'),
    ('Caña de Hierro Forjado',   'Caña',    'Épico',  2,  0,  0,  NULL, NULL, '🎣'),
    ('Caña de Escoria Templada', 'Caña',    'Mítico', 3,  0,  0,  NULL, NULL, '🎣')
ON CONFLICT (name) DO UPDATE
    SET type = EXCLUDED.type, rarity = EXCLUDED.rarity, stat_value = EXCLUDED.stat_value, sell_price = EXCLUDED.sell_price,
        buy_price = EXCLUDED.buy_price, weapon_family = EXCLUDED.weapon_family, class_requirement = EXCLUDED.class_requirement,
        emoji = COALESCE(items.emoji, EXCLUDED.emoji);

DROP TABLE IF EXISTS rod_ingredients;
CREATE TEMP TABLE rod_ingredients (result_name TEXT, item_name TEXT, quantity INTEGER, zone_rank INTEGER);
INSERT INTO rod_ingredients VALUES
    ('Caña de Hierro Forjado',   'Madera de Roble',  9, 3),
    ('Caña de Hierro Forjado',   'Madera de Nogal',  2, 3),
    ('Caña de Hierro Forjado',   'Hierro',           6, 3),
    ('Caña de Hierro Forjado',   'Piedra',          12, 3),
    ('Caña de Escoria Templada', 'Madera de Roble',  9, 5),
    ('Caña de Escoria Templada', 'Madera de Nogal',  6, 5),
    ('Caña de Escoria Templada', 'Hierro',           9, 5),
    ('Caña de Escoria Templada', 'Gema de Zafiro',   2, 5);

DO $$
DECLARE
    v_missing TEXT;
BEGIN
    -- Un ingrediente que no existe se omitiría en silencio (la receta nacería sin él): se avisa en voz alta.
    SELECT string_agg(DISTINCT t.item_name, ', ') INTO v_missing FROM rod_ingredients t WHERE NOT EXISTS (SELECT 1 FROM items i WHERE i.name = t.item_name);
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'add_fishing: faltan estos materiales en el catálogo: [%]', v_missing;
    END IF;
    -- Solo materiales de recolección (la herramienta no pide drops de monstruos ni oro).
    SELECT string_agg(DISTINCT t.item_name, ', ') INTO v_missing FROM rod_ingredients t JOIN items i ON i.name = t.item_name WHERE i.type NOT IN ('Madera', 'Mineral');
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'add_fishing: estos ingredientes no son de recolección: [%]', v_missing;
    END IF;
END $$;

-- Las recetas: sin oro, en la zona que las desbloquea (el rank 3 y el 5 de la escalera por nivel, nunca por zone_id crudo). Las ve el Carpintero, no la herrería (RecipeCatalog.IsVisibleTo).
INSERT INTO recipes (result_item_id, gold_cost, zone_id, affinity)
SELECT i.item_id, 0,
       (SELECT z.zone_id FROM zones z WHERE z.kind = 'normal' ORDER BY z.min_level, z.zone_id OFFSET (MAX(t.zone_rank) - 1) LIMIT 1),
       false
FROM rod_ingredients t JOIN items i ON i.name = t.result_name
GROUP BY i.item_id
ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost, zone_id = EXCLUDED.zone_id, affinity = EXCLUDED.affinity;

-- Los ingredientes se reescriben enteros (una receta cambiada nunca conserva filas viejas).
DELETE FROM recipe_ingredients WHERE recipe_id IN (SELECT r.recipe_id FROM recipes r JOIN items i ON i.item_id = r.result_item_id WHERE i.type = 'Caña');
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT r.recipe_id, ing.item_id, t.quantity
FROM rod_ingredients t
JOIN items res ON res.name = t.result_name
JOIN recipes r ON r.result_item_id = res.item_id
JOIN items ing ON ing.name = t.item_name;

DO $$
DECLARE
    v_n INTEGER;
    v_bad TEXT;
BEGIN
    SELECT COUNT(*) INTO v_n FROM items WHERE type = 'Pescado';
    IF v_n <> 3 THEN
        RAISE EXCEPTION 'add_fishing: hay % peces en el catálogo y tienen que ser 3', v_n;
    END IF;
    SELECT COUNT(*) INTO v_n FROM recipes r JOIN items i ON i.item_id = r.result_item_id WHERE i.type = 'Caña';
    IF v_n <> 2 THEN
        RAISE EXCEPTION 'add_fishing: hay % recetas de caña y tienen que ser 2', v_n;
    END IF;
    SELECT string_agg(i.name, ', ') INTO v_bad
    FROM recipes r JOIN items i ON i.item_id = r.result_item_id
    WHERE i.type = 'Caña' AND (r.gold_cost <> 0 OR r.zone_id IS NULL OR (SELECT COUNT(*) FROM recipe_ingredients ri WHERE ri.recipe_id = r.recipe_id) <> 4);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'add_fishing: estas cañas no quedaron con 0 de oro, su zona y 4 ingredientes: [%]', v_bad;
    END IF;
END $$;
