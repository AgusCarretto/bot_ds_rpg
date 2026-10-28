-- =========================================================
-- v0.36.0: el /travel deja de soltar un material y pasa a dar oro, EXP y una CHANCE DE CAJA de la zona (el dueño, 2026-10-28: «si sacamos el drop solo del travel… y que el travel sea esa suma
-- de xp y gold con probabilidad de sacar una caja de la zona»; «sumale algún drop a cada uno» = más unidades de los drops de cacería en cada receta para que el ritmo no cambie).
--
--   psql -d asado-y-acero -v ON_ERROR_STOP=1 -f remove_travel_drops.sql
--
-- IMPORTANTE para una base viva: correrlo ANTES de arrancar el bot v0.36.0 (el código lee la fila "travel" de zone_boxes en cada /travel ganado y ya no espera el drop). Hacé un backup antes
-- (DEPLOY.md, sección 6). Sirve para una base que ya existe y para una instalación nueva (va en run_fresh_install.sql después de add_dragon_food.sql). Es RE-EJECUTABLE: sin drops de viaje no
-- toca recetas ni mochilas, y las filas de zone_boxes que ya existen no se pisan (la chance se retoca con un UPDATE).
--
-- Qué hace, todo en UNA transacción:
--   1. zone_boxes admite el rol 'travel' y se cargan las cajas del viaje de las 5 zonas: Cajón de Pino 8 % / Cajón de Pino 15 % / Baúl de Roble 6 % / Arcón de Hierro 2 % / Arcón de Hierro 3 %
--      (la misma caja chica que da repetir el jefe; las chances son BAJAS a propósito: se puede viajar cada 30 minutos y cada caja vale muchos minutos de farmeo. Valen ~10-15 % de lo que
--      paga el viaje en oro. Se retocan con UPDATE, sin redeploy más allá del caché de 5 minutos. Los bonus de chance de drop —Fuego Nuevo, mascotas, bendición, dragón— multiplican esta chance.)
--   2. Canje para quien tenía drops de viaje en la mochila: cada unidad vale 1 de CADA drop de cacería de su zona (2 unidades de ~33 minutos de farmeo cada una por una de ~75 minutos).
--   3. Recetas de las 5 zonas que usaban un drop de viaje (las 20 armas de clase y los 5 amuletos): se les saca ese ingrediente (pasan de 4 tipos de ingrediente a 3) y los drops de CACERÍA
--      suben para que el tiempo total no cambie: armas de las zonas 2-5 ×4 → ×5 (~167 min de drops, antes ~150 con el viaje), amuletos de las zonas 2-5 ×4 → ×7 de cada drop de cacería
--      (~233 min, antes 225), amuleto de la Zona 1 ×4 → ×5 (167, antes 150). Las armas de la Zona 1 solo pierden el cuero (eran lentas por la recolección, 95-100 min como antes).
--   4. Se borran los 5 drops de viaje (Cuero Grueso, Ceniza Bendita, Escoria Metálica Densa, Aliento de Fuego Eterno, Ceniza del Abismo): los monstruos de viaje quedan SIN drop.
--
-- OJO: después de esto NO vuelvas a correr rework_drops_and_recipes.sql ni rebalance_dust.sql SOLOS contra esa base (traen de vuelta los drops de viaje y sus recetas o fallan porque ya no existen);
-- sobre una base nueva van en el orden de run_fresh_install.sql y esto va después.
-- =========================================================

BEGIN;

-- 1. El rol 'travel' en zone_boxes (el CHECK de la tabla lo lista) y las cajas de cada zona.
DO $$
DECLARE
    v_name TEXT;
BEGIN
    FOR v_name IN
        SELECT conname FROM pg_constraint
        WHERE conrelid = 'zone_boxes'::regclass AND contype = 'c' AND pg_get_constraintdef(oid) ILIKE '%role%'
    LOOP
        EXECUTE format('ALTER TABLE zone_boxes DROP CONSTRAINT %I', v_name);
    END LOOP;

    ALTER TABLE zone_boxes ADD CONSTRAINT zone_boxes_role_check CHECK (role IN ('repeat', 'daily', 'weekly', 'prize', 'prize_top', 'travel'));
END $$;

INSERT INTO zone_boxes (zone_id, role, box_item_id, chance_percent)
SELECT t.zone_id, 'travel', i.item_id, t.chance
FROM (VALUES
    (1, 'Cajón de Pino',   8),
    (2, 'Cajón de Pino',  15),
    (3, 'Baúl de Roble',   6),
    (4, 'Arcón de Hierro', 2),
    (5, 'Arcón de Hierro', 3)
) AS t (zone_id, box_name, chance)
JOIN zones z ON z.zone_id = t.zone_id
JOIN items i ON i.name = t.box_name AND i.type = 'Caja'
ON CONFLICT (zone_id, role) DO NOTHING;

DO $$
BEGIN
    IF (SELECT count(*) FROM zone_boxes WHERE role = 'travel' AND zone_id BETWEEN 1 AND 5) <> 5 THEN
        RAISE EXCEPTION 'remove_travel_drops: faltan cajas de viaje en zone_boxes (se esperaban 5 para las zonas 1 a 5, hay %)',
            (SELECT count(*) FROM zone_boxes WHERE role = 'travel' AND zone_id BETWEEN 1 AND 5);
    END IF;
END $$;

-- Guardas antes de tocar nada: un drop de viaje que también suelta otro monstruo, que está en el botín de una caja o es el huevo de una mascota no se puede borrar sin más.
DO $$
DECLARE
    v_bad TEXT;
BEGIN
    SELECT string_agg(DISTINCT i.name, ', ') INTO v_bad
    FROM items i
    WHERE i.item_id IN (SELECT md.item_id FROM monsters m JOIN monster_drops md ON md.monster_id = m.monster_id WHERE m.is_travel AND m.zone_id > 0)
      AND (EXISTS (SELECT 1 FROM monster_drops md2 JOIN monsters m2 ON m2.monster_id = md2.monster_id WHERE md2.item_id = i.item_id AND NOT m2.is_travel)
           OR EXISTS (SELECT 1 FROM box_loot bl WHERE bl.item_id = i.item_id)
           OR EXISTS (SELECT 1 FROM pet_species ps WHERE ps.egg_item_id = i.item_id)
           OR EXISTS (SELECT 1 FROM item_buffs ib WHERE ib.item_id = i.item_id)
           OR i.type <> 'Material');
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'remove_travel_drops: se usan en otro lado (otro monstruo, botín de una caja, mascota, buff) o no son Material: %', v_bad;
    END IF;
END $$;

-- 2. El canje: cada unidad de un drop de viaje que haya en una mochila vale 1 de CADA drop de cacería de su zona.
DO $$
DECLARE
    v_holders INTEGER;
    v_units   BIGINT;
BEGIN
    SELECT count(DISTINCT inv.discord_id), COALESCE(sum(inv.quantity), 0) INTO v_holders, v_units
    FROM inventory inv
    WHERE inv.quantity > 0
      AND inv.item_id IN (SELECT md.item_id FROM monsters m JOIN monster_drops md ON md.monster_id = m.monster_id WHERE m.is_travel AND m.zone_id > 0);

    RAISE NOTICE 'remove_travel_drops: % jugadores tienen % drops de viaje en la mochila (se canjean por drops de cacería de su zona).', v_holders, v_units;
END $$;

WITH travel AS (
    SELECT DISTINCT m.zone_id, md.item_id AS travel_item
    FROM monsters m JOIN monster_drops md ON md.monster_id = m.monster_id
    WHERE m.is_travel AND m.zone_id > 0
), hunt AS (
    SELECT DISTINCT m.zone_id, md.item_id AS hunt_item
    FROM monsters m JOIN monster_drops md ON md.monster_id = m.monster_id
    WHERE NOT m.is_travel AND NOT m.is_boss AND m.zone_id > 0
), gifts AS (
    SELECT inv.discord_id, h.hunt_item, inv.quantity
    FROM inventory inv
    JOIN travel t ON t.travel_item = inv.item_id
    JOIN hunt h ON h.zone_id = t.zone_id
    WHERE inv.quantity > 0
)
INSERT INTO inventory (discord_id, item_id, quantity)
SELECT discord_id, hunt_item, LEAST(SUM(quantity), 2147483647)
FROM gifts
GROUP BY discord_id, hunt_item
ON CONFLICT (discord_id, item_id) DO UPDATE
    SET quantity = LEAST(inventory.quantity::bigint + EXCLUDED.quantity, 2147483647)::integer;

-- 3. Las recetas que pedían un drop de viaje: sube la cantidad de los drops de cacería (valor ABSOLUTO, así correrlo dos veces no suma dos veces) y después se saca el ingrediente de viaje.
WITH travel_items AS (
    SELECT DISTINCT md.item_id FROM monsters m JOIN monster_drops md ON md.monster_id = m.monster_id WHERE m.is_travel AND m.zone_id > 0
), hunt_items AS (
    SELECT DISTINCT md.item_id FROM monsters m JOIN monster_drops md ON md.monster_id = m.monster_id WHERE NOT m.is_travel AND NOT m.is_boss AND m.zone_id > 0
), affected AS (
    SELECT DISTINCT r.recipe_id, r.zone_id, (res.type = 'Amulet') AS is_amulet
    FROM recipes r
    JOIN items res ON res.item_id = r.result_item_id
    JOIN recipe_ingredients ri ON ri.recipe_id = r.recipe_id
    WHERE ri.item_id IN (SELECT item_id FROM travel_items)
)
UPDATE recipe_ingredients ri
SET quantity = CASE
        WHEN a.is_amulet AND a.zone_id = 1 THEN 5      -- Hombreras de Cuero Grueso: Colmillo y Pluma 4 → 5
        WHEN a.is_amulet                   THEN 7      -- amuletos de las zonas 2-5: los dos drops de cacería 4 → 7
        WHEN a.zone_id = 1                 THEN ri.quantity   -- armas de la Zona 1: solo pierden el cuero (las frena la recolección)
        ELSE 5                                         -- armas de las zonas 2-5: el drop de cacería 4 → 5
    END
FROM affected a
WHERE ri.recipe_id = a.recipe_id
  AND ri.item_id IN (SELECT item_id FROM hunt_items);

DELETE FROM recipe_ingredients
WHERE item_id IN (SELECT md.item_id FROM monsters m JOIN monster_drops md ON md.monster_id = m.monster_id WHERE m.is_travel AND m.zone_id > 0);

-- 4. Se borran los 5 drops de viaje (en cascada: sus filas de monster_drops y lo que quedara en mochilas, ya canjeado arriba).
DELETE FROM items
WHERE item_id IN (SELECT md.item_id FROM monsters m JOIN monster_drops md ON md.monster_id = m.monster_id WHERE m.is_travel AND m.zone_id > 0);

-- Verificación del estado final.
DO $$
DECLARE
    v_n INTEGER;
    v_bad TEXT;
BEGIN
    SELECT count(*) INTO v_n FROM monster_drops md JOIN monsters m ON m.monster_id = md.monster_id WHERE m.is_travel;
    IF v_n <> 0 THEN
        RAISE EXCEPTION 'remove_travel_drops: quedaron % drops en monstruos de viaje', v_n;
    END IF;

    SELECT count(DISTINCT md.item_id) INTO v_n FROM monster_drops md JOIN monsters m ON m.monster_id = md.monster_id WHERE NOT m.is_boss AND m.zone_id > 0;
    IF v_n <> 10 THEN
        RAISE EXCEPTION 'remove_travel_drops: se esperaban 10 drops de monstruos comunes (2 por zona) y hay %', v_n;
    END IF;

    -- Ningún arma de clase ni amuleto de las zonas 1 a 5 pide más de 3 tipos de ingrediente (las herramientas del Carpintero y el equipo del Fogón no cuentan: son otros).
    SELECT string_agg(res.name || ' (' || x.n || ')', ', ') INTO v_bad
    FROM recipes r
    JOIN items res ON res.item_id = r.result_item_id AND res.type IN ('Weapon', 'Amulet')
    JOIN LATERAL (SELECT count(*) AS n FROM recipe_ingredients ri WHERE ri.recipe_id = r.recipe_id) x ON TRUE
    WHERE r.zone_id BETWEEN 1 AND 5 AND x.n > 3;
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'remove_travel_drops: recetas con más de 3 tipos de ingrediente: %', v_bad;
    END IF;

    SELECT count(*) INTO v_n FROM recipe_ingredients ri
    JOIN items i ON i.item_id = ri.item_id
    WHERE i.type = 'Material' AND NOT EXISTS (SELECT 1 FROM monster_drops md WHERE md.item_id = i.item_id);
    IF v_n <> 0 THEN
        RAISE EXCEPTION 'remove_travel_drops: % ingredientes de receta son un Material que ningún monstruo suelta', v_n;
    END IF;

    RAISE NOTICE 'remove_travel_drops: listo. Cajas de viaje: %, recetas de armas y amuletos de las zonas 1-5: %.',
        (SELECT count(*) FROM zone_boxes WHERE role = 'travel'),
        (SELECT count(*) FROM recipes r JOIN items res ON res.item_id = r.result_item_id WHERE r.zone_id BETWEEN 1 AND 5 AND res.type IN ('Weapon', 'Amulet'));
END $$;

COMMIT;
