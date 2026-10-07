-- =========================================================
-- v0.14.0: cofres parejos (docs/superpowers/specs/2026-10-07-cofres-parejos-design.md). Re-ejecutable y se verifica solo. Va DESPUÉS de todo lo demás (usa cajas, comida, trofeos
-- y la zona 0 que crean los scripts de arriba) y lo incluyen run_fresh_install.sql (instalación nueva) y add_zone_boxes.sql (base que ya existe). La TABLA zone_boxes y el
-- valor 'gather' de pet_species.bonus_kind están en schema.sql / add_zone_boxes.sql.
--
--   1. Dos cajas nuevas, solo premio (no se compran ni se venden): Cofre de Escoria (el jefe de la Zona 5, 35-65 ítems) y Brasero del Fogón (el Asador, 25-45 ítems).
--   2. El cofre del jefe de la 5 pasa a ser el de Escoria y el Asador suelta el Brasero (monster_drops: es el cofre de la PRIMERA vez por vuelta).
--   3. El Asador paga menos EXP (la XP se pierde al hacer un Fuego Nuevo) y más oro (el oro se queda): bonus 3000 / 300.
--   4. zone_boxes: qué caja da cada zona en cada rol (repeat / daily / weekly / prize / prize_top), con su chance. La fuente de verdad de las cajas gratis.
--   5. Una 6.ª mascota, la de la Zona 0 (bonus de cantidad de /chop y /mine), con su huevo.
-- Ojo: rework_drops_and_recipes.sql (v0.7.0) pone el Cofre de Oro como cofre del jefe de la 5: este script va después y lo pisa. No re-correr aquel sin correr este de nuevo.
-- =========================================================

-- 1) Los ítems: las dos cajas y el huevo. No se toca el emoji si ya se lo cargaron.
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price, emoji) VALUES
    ('Cofre de Escoria',  'Caja', 'Mítico', 0, 0, 0, '📦'),
    ('Brasero del Fogón', 'Caja', 'Mítico', 0, 0, 0, '📦'),
    ('Huevo del Fogón',   'Huevo', 'Mítico', 0, 0, 0, '🥚')
ON CONFLICT (name) DO UPDATE SET
    type = EXCLUDED.type, rarity = EXCLUDED.rarity, stat_value = 0, sell_price = 0, buy_price = 0;

INSERT INTO boxes (box_item_id, rolls, min_items, max_items)
SELECT i.item_id, 1, v.min_items, v.max_items
FROM (VALUES ('Cofre de Escoria', 35, 65), ('Brasero del Fogón', 25, 45)) AS v (name, min_items, max_items)
JOIN items i ON i.name = v.name
ON CONFLICT (box_item_id) DO UPDATE SET rolls = 1, min_items = EXCLUDED.min_items, max_items = EXCLUDED.max_items;

-- 2) Su botín (se borra y se vuelve a cargar: este archivo es la fuente de verdad). Las tiradas suman 1000 por caja; el oro es un bono aparte (peso = chance en milésimos).
--    Cofre de Escoria: lo de la Zona 5 (drops de las 5 zonas, los trofeos de la 5, banquetes, el Cofre de Oro de abajo) y ~3 % de un bono grande de oro.
--    Brasero del Fogón: lo que SOBREVIVE al Fuego Nuevo (la Comida para Mascotas y el oro, 100 % seguro) más un poco de materiales y banquetes por si todavía no se reinicia.
CREATE TEMP TABLE zb_loot (box_name TEXT, kind TEXT, item_name TEXT, weight INTEGER, min_qty INTEGER, max_qty INTEGER);
INSERT INTO zb_loot VALUES
    ('Cofre de Escoria', 'gather',    NULL,                                    612, 1, 1),
    ('Cofre de Escoria', 'zone_drop', NULL,                                    220, 1, 1),
    ('Cofre de Escoria', 'item',      'Asado de Tira',                          20, 1, 1),
    ('Cofre de Escoria', 'item',      'Cordero Patagónico',                     40, 1, 1),
    ('Cofre de Escoria', 'item',      'Asado Completo del Domingo en Familia',  25, 1, 1),
    ('Cofre de Escoria', 'item',      'Mate Dulce de la Abuela',                25, 1, 1),
    ('Cofre de Escoria', 'item',      'Corona de Escoria Viva',                 25, 1, 1),
    ('Cofre de Escoria', 'item',      'Escoria Pura del Cráter',                25, 1, 1),
    ('Cofre de Escoria', 'item',      'Cofre de Oro',                            8, 1, 1),
    ('Cofre de Escoria', 'gold',      NULL,                                     30, 20000, 60000),
    ('Brasero del Fogón', 'gather',    NULL,                                   100, 1, 1),
    ('Brasero del Fogón', 'zone_drop', NULL,                                   100, 1, 1),
    ('Brasero del Fogón', 'item',      'Comida para Mascotas',                 650, 2, 4),
    ('Brasero del Fogón', 'item',      'Asado Completo del Domingo en Familia', 75, 1, 1),
    ('Brasero del Fogón', 'item',      'Mate Dulce de la Abuela',               75, 1, 1),
    ('Brasero del Fogón', 'gold',      NULL,                                  1000, 40000, 80000);

DO $$
DECLARE
    v_missing TEXT;
BEGIN
    SELECT string_agg(n, ', ') INTO v_missing FROM (
        SELECT DISTINCT item_name AS n FROM zb_loot WHERE item_name IS NOT NULL
    ) names WHERE NOT EXISTS (SELECT 1 FROM items i WHERE i.name = names.n);
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'seed_zone_boxes: faltan estos ítems en el catálogo: [%]', v_missing;
    END IF;
END $$;

DELETE FROM box_loot WHERE box_item_id IN (SELECT item_id FROM items WHERE name IN ('Cofre de Escoria', 'Brasero del Fogón'));

INSERT INTO box_loot (box_item_id, kind, item_id, weight, min_qty, max_qty)
SELECT b.item_id, l.kind, i.item_id, l.weight, l.min_qty, l.max_qty
FROM zb_loot l
JOIN items b ON b.name = l.box_name
LEFT JOIN items i ON i.name = l.item_name;

DROP TABLE zb_loot;

-- 3) El cofre de la PRIMERA vez por vuelta de cada jefe (monster_drops): el de la Zona 5 pasa a ser el de Escoria y el Asador suelta el Brasero.
DELETE FROM monster_drops WHERE monster_id IN (SELECT monster_id FROM monsters WHERE name IN ('Soberano de la Escoria', 'El Asador Eterno'));

INSERT INTO monster_drops (monster_id, item_id)
SELECT m.monster_id, i.item_id
FROM (VALUES ('Soberano de la Escoria', 'Cofre de Escoria'), ('El Asador Eterno', 'Brasero del Fogón')) AS v (monster_name, box_name)
JOIN monsters m ON m.name = v.monster_name
JOIN items i ON i.name = v.box_name;

-- 4) El Asador: oro 3000 (se queda al reiniciar) y EXP 300 (se pierde al reiniciar). Era 1500 / 1500, igual que el jefe de la Zona 5.
UPDATE monsters SET gold_reward = 3000, xp_reward = 300 WHERE name = 'El Asador Eterno';

-- 5) Las cajas gratis de cada zona. repeat = la caja que da CADA victoria repetida sobre el jefe (con su chance en %); daily / weekly = el premio por completar todas las misiones del
--    día / de la semana; prize / prize_top = los tramos II y III de los logros y el campeón de la Arena. Valores de cada caja (report_box_economy.sql): Pino ~0,75k, Roble ~8k,
--    Arcón ~43k, Cofre de Oro ~211k; una sesión de 2 h de farmeo vale ~3k / 14k / 28k / 52k / 90k en las zonas 1 a 5.
INSERT INTO zone_boxes (zone_id, role, box_item_id, chance_percent)
SELECT z.zone_id, v.role, i.item_id, v.chance_percent
FROM (VALUES
    ('Praderas del Mate',    'repeat',    'Cajón de Pino',   100),
    ('Praderas del Mate',    'daily',     'Cajón de Pino',   100),
    ('Praderas del Mate',    'weekly',    'Baúl de Roble',   100),
    ('Praderas del Mate',    'prize',     'Cajón de Pino',   100),
    ('Praderas del Mate',    'prize_top', 'Baúl de Roble',   100),
    ('Bosque de Cenizas',    'repeat',    'Cajón de Pino',   100),
    ('Bosque de Cenizas',    'daily',     'Baúl de Roble',   100),
    ('Bosque de Cenizas',    'weekly',    'Arcón de Hierro', 100),
    ('Bosque de Cenizas',    'prize',     'Baúl de Roble',   100),
    ('Bosque de Cenizas',    'prize_top', 'Arcón de Hierro', 100),
    ('Minas del Yunque',     'repeat',    'Baúl de Roble',   100),
    ('Minas del Yunque',     'daily',     'Baúl de Roble',   100),
    ('Minas del Yunque',     'weekly',    'Arcón de Hierro', 100),
    ('Minas del Yunque',     'prize',     'Arcón de Hierro', 100),
    ('Minas del Yunque',     'prize_top', 'Cofre de Oro',    100),
    ('Cordillera del Fuego', 'repeat',    'Arcón de Hierro',  60),
    ('Cordillera del Fuego', 'daily',     'Arcón de Hierro', 100),
    ('Cordillera del Fuego', 'weekly',    'Cofre de Oro',    100),
    ('Cordillera del Fuego', 'prize',     'Cofre de Oro',    100),
    ('Cordillera del Fuego', 'prize_top', 'Cofre de Oro',    100),
    ('Cráter de la Escoria', 'repeat',    'Arcón de Hierro', 100),
    ('Cráter de la Escoria', 'daily',     'Arcón de Hierro', 100),
    ('Cráter de la Escoria', 'weekly',    'Cofre de Oro',    100),
    ('Cráter de la Escoria', 'prize',     'Cofre de Oro',    100),
    ('Cráter de la Escoria', 'prize_top', 'Cofre de Oro',    100),
    ('El Fogón Eterno',      'repeat',    'Arcón de Hierro', 100)
) AS v (zone_name, role, box_name, chance_percent)
JOIN zones z ON z.name = v.zone_name
JOIN items i ON i.name = v.box_name
ON CONFLICT (zone_id, role) DO UPDATE SET box_item_id = EXCLUDED.box_item_id, chance_percent = EXCLUDED.chance_percent;

-- 6) La 6.ª mascota: la de la Zona 0. Su huevo llega la primera vez que se vence al Asador (y solo si todavía no la tenés). Bonus 'gather': % más de cantidad en /chop y /mine.
INSERT INTO pet_species (zone_id, name, emoji, bonus_kind, max_bonus_percent, egg_item_id)
SELECT z.zone_id, 'Chispa del Asador', '🔥', 'gather', 10.0, e.item_id
FROM zones z JOIN items e ON e.name = 'Huevo del Fogón'
WHERE z.name = 'El Fogón Eterno'
ON CONFLICT (name) DO UPDATE SET
    zone_id = EXCLUDED.zone_id, bonus_kind = EXCLUDED.bonus_kind, max_bonus_percent = EXCLUDED.max_bonus_percent, egg_item_id = EXCLUDED.egg_item_id;

-- 7) Verificación: el estado final tiene que ser EXACTAMENTE el diseño.
DO $$
DECLARE
    v_bad TEXT;
BEGIN
    -- Las dos cajas: rango, precio 0, y el botín suma 1000 con UNA entrada de recolección y UNA de drops de zona.
    SELECT string_agg(i.name, ', ') INTO v_bad
    FROM boxes b JOIN items i ON i.item_id = b.box_item_id
    WHERE i.name IN ('Cofre de Escoria', 'Brasero del Fogón')
      AND ((SELECT COALESCE(SUM(weight), 0) FROM box_loot l WHERE l.box_item_id = b.box_item_id AND l.kind <> 'gold') <> 1000
        OR (SELECT COUNT(*) FROM box_loot l WHERE l.box_item_id = b.box_item_id AND l.kind = 'gather') <> 1
        OR (SELECT COUNT(*) FROM box_loot l WHERE l.box_item_id = b.box_item_id AND l.kind = 'zone_drop') <> 1
        OR i.buy_price <> 0 OR i.sell_price <> 0);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'seed_zone_boxes: el botín o el precio de estas cajas está mal: [%]', v_bad;
    END IF;

    -- Cada zona normal tiene sus cinco roles y la puerta tiene el de repetición.
    SELECT string_agg(z.name || ':' || r.role, ', ') INTO v_bad
    FROM zones z CROSS JOIN (VALUES ('repeat'), ('daily'), ('weekly'), ('prize'), ('prize_top')) AS r (role)
    WHERE z.kind = 'normal' AND NOT EXISTS (SELECT 1 FROM zone_boxes zb WHERE zb.zone_id = z.zone_id AND zb.role = r.role);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'seed_zone_boxes: a estas zonas les falta una caja en zone_boxes: [%]', v_bad;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM zone_boxes zb JOIN zones z ON z.zone_id = zb.zone_id WHERE z.kind = 'gate' AND zb.role = 'repeat') THEN
        RAISE EXCEPTION 'seed_zone_boxes: la puerta (zona 0) no tiene la caja de repetición';
    END IF;

    -- El cofre de la primera vez de cada jefe: UNO solo y es una Caja; Zona 5 = Cofre de Escoria, Zona 0 = Brasero del Fogón.
    SELECT string_agg(m.name, ', ') INTO v_bad
    FROM monsters m
    WHERE m.is_boss AND ((SELECT COUNT(*) FROM monster_drops d WHERE d.monster_id = m.monster_id) <> 1
        OR EXISTS (SELECT 1 FROM monster_drops d JOIN items i ON i.item_id = d.item_id WHERE d.monster_id = m.monster_id AND i.type <> 'Caja'));
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'seed_zone_boxes: estos jefes no tienen exactamente un cofre (una Caja): [%]', v_bad;
    END IF;
    IF (SELECT i.name FROM monster_drops d JOIN monsters m ON m.monster_id = d.monster_id JOIN items i ON i.item_id = d.item_id WHERE m.name = 'Soberano de la Escoria') <> 'Cofre de Escoria'
       OR (SELECT i.name FROM monster_drops d JOIN monsters m ON m.monster_id = d.monster_id JOIN items i ON i.item_id = d.item_id WHERE m.name = 'El Asador Eterno') <> 'Brasero del Fogón' THEN
        RAISE EXCEPTION 'seed_zone_boxes: el cofre del Soberano tiene que ser el de Escoria y el del Asador el Brasero';
    END IF;

    -- Las mascotas: 6 especies (una por zona, la 6.ª en la puerta) y 6 huevos.
    IF (SELECT count(*) FROM pet_species) <> 6 OR (SELECT count(*) FROM items WHERE type = 'Huevo') <> 6 THEN
        RAISE EXCEPTION 'seed_zone_boxes: tienen que ser 6 mascotas y 6 huevos (hay % y %)', (SELECT count(*) FROM pet_species), (SELECT count(*) FROM items WHERE type = 'Huevo');
    END IF;

    -- Los trofeos de colección tienen que seguir saliendo de alguna caja, y el Mítico fijo (Corteza / Meteorito) SOLO del Arca.
    SELECT string_agg(i.name, ', ') INTO v_bad
    FROM items i
    WHERE i.type = 'Material'
      AND NOT EXISTS (SELECT 1 FROM monster_drops d WHERE d.item_id = i.item_id)
      AND NOT EXISTS (SELECT 1 FROM box_loot l WHERE l.item_id = i.item_id);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'seed_zone_boxes: estos trofeos ya no salen de ninguna caja: [%]', v_bad;
    END IF;
    SELECT string_agg(DISTINCT bi.name, ', ') INTO v_bad
    FROM box_loot l JOIN items bi ON bi.item_id = l.box_item_id JOIN items i ON i.item_id = l.item_id
    WHERE i.name IN ('Corteza del Árbol de Vida', 'Fragmento de Meteorito') AND bi.name <> 'Arca del Soberano';
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'seed_zone_boxes: estas cajas dan Corteza o Meteorito como ítem fijo: [%]', v_bad;
    END IF;
END $$;
