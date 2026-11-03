-- =========================================================
-- v0.47.0: las Zonas 6 a 10, que abre el Fuego Nuevo (design: docs/superpowers/specs/2026-11-01-zonas-6-a-10-mazmorras-design.md). RE-EJECUTABLE.
-- Correr en una base viva ANTES del binario v0.47.0: toda consulta de zonas lee zones.min_fuego_nuevo. Va también en run_fresh_install.sql (después de todo lo demás).
--
--   · La columna zones.min_fuego_nuevo: cuántos Fuegos Nuevos hacen falta para que la zona exista para el jugador (0 = zonas 1 a 5). GameData/ZoneRanking.ForPlayer filtra la escalera con eso.
--   · Cinco zonas, con zone_id fijo 6..10 (identidad GENERATED ALWAYS, de ahí OVERRIDING SYSTEM VALUE), niveles 30/35/40/45/50 y Fuego Nuevo 5/8/11/14/17 (una cada 3).
--   · Cada zona: UN monstruo de caza que suelta UN drop sencillo (con la chance de siempre de la caza) y un monstruo de viaje que no suelta nada (el viaje paga oro, EXP y la caja de
--     zone_boxes «travel»: acá el Cofre de Escoria al 15 %, que es lo que pide la Reliquia). No hay jefe: se sale por la mazmorra (GameData/Dungeons), con la Reliquia de la zona.
--   · Los números de los monstruos salen de tools/balance2 parte N (el CombatTurnResolver real): con el equipo de la zona ANTERIOR al nivel mínimo una pelea cuesta ~40 % de la vida, con el
--     de la zona (nivel mínimo + 3) un 5-10 %, y el monstruo dura ~4 golpes. El de viaje es el común ×1,25 de vida y ×1,1 de daño (como en las zonas 1 a 5).
--   · Equipo por zona: arma de clase (ATQ 180/270/400/600/900) y amuleto (DEF 165/215/280/365/475), más la Reliquia (tipo «Reliquia», no se vende ni se desmantela, se gasta al entrar a la
--     mazmorra). Las recetas piden MUCHOS materiales y más drops cuanto más alta la zona (el dueño). Sin mascotas nuevas (el dueño).
-- =========================================================

ALTER TABLE zones ADD COLUMN IF NOT EXISTS min_fuego_nuevo INTEGER NOT NULL DEFAULT 0 CHECK (min_fuego_nuevo >= 0);

DROP TABLE IF EXISTS z610;
CREATE TEMP TABLE z610 (
    k INTEGER, zone_id INTEGER, zone_name TEXT, zone_desc TEXT, zone_emoji TEXT, min_level INTEGER, min_fn INTEGER,
    suffix TEXT, drop_name TEXT, drop_sell INTEGER,
    hunt_name TEXT, hunt_emoji TEXT, travel_name TEXT, travel_emoji TEXT,
    hp_min INTEGER, hp_max INTEGER, dmg_min INTEGER, dmg_max INTEGER, t_hp_min INTEGER, t_hp_max INTEGER, t_dmg_min INTEGER, t_dmg_max INTEGER,
    gold_b INTEGER, xp_b INTEGER, weapon_atk INTEGER, amulet_def INTEGER, weapon_gold INTEGER, amulet_gold INTEGER, relic_gold INTEGER);
INSERT INTO z610 VALUES
 (0, 6, 'Salinas del Olvido', 'Más allá del Fogón, un mar de sal blanca donde el viento borra las huellas. Lo que se pierde acá no se vuelve a encontrar.', '🧂', 30, 5,
  'Sal', 'Cristal de Sal', 600, 'Guanaco de Sal', '🦙', 'Coloso de Sal', '🗿',
  864, 1168, 174, 260, 1080, 1460, 191, 286, 484, 325, 180, 165, 4000, 5000, 5000),
 (1, 7, 'Selva de Humo', 'Un monte que arde por dentro sin que se vea el fuego: solo humo espeso como lana, y lo que se mueve adentro.', '🌫️', 35, 8,
  'Humo', 'Colmillo de Humo', 700, 'Yaguareté de Humo', '🐆', 'Sombra del Monte', '👤',
  1204, 1628, 222, 332, 1505, 2035, 244, 365, 754, 420, 270, 215, 7000, 8500, 7500),
 (2, 8, 'Pantano de Brea', 'Un barro negro y tibio que se pega a las botas y a la memoria. Lo que se hunde no vuelve.', '🛢️', 40, 11,
  'Brea', 'Gota de Brea', 800, 'Sapo de Brea', '🐸', 'Bestia del Pantano', '🐊',
  1680, 2272, 274, 410, 2100, 2840, 301, 451, 1164, 550, 400, 280, 11000, 13000, 10000),
 (3, 9, 'Glaciar del Silencio', 'Hielo azul que no deja oír ni los propios pasos. Acá el frío también quema.', '🧊', 45, 14,
  'Hielo', 'Escama de Hielo', 900, 'Lobo de Hielo', '🐺', 'Gigante Escarchado', '❄️',
  2394, 3238, 334, 502, 2993, 4048, 367, 552, 1784, 715, 600, 365, 16000, 19000, 12500),
 (4, 10, 'Cumbre del Rayo', 'La última cima conocida: tormentas que no terminan y un cielo que pega como un martillo.', '⚡', 50, 17,
  'Rayo', 'Pluma de Rayo', 1000, 'Cóndor del Rayo', '🦅', 'Titán de Tormenta', '⛈️',
  3448, 4664, 414, 620, 4310, 5830, 455, 682, 2724, 930, 900, 475, 22000, 26000, 15000);

-- Las zonas.
INSERT INTO zones (zone_id, name, description, min_level, emoji, kind, min_fuego_nuevo)
OVERRIDING SYSTEM VALUE
SELECT zone_id, zone_name, zone_desc, min_level, zone_emoji, 'normal', min_fn FROM z610
ON CONFLICT (zone_id) DO UPDATE
    SET name = EXCLUDED.name, description = EXCLUDED.description, min_level = EXCLUDED.min_level, emoji = EXCLUDED.emoji, kind = 'normal', min_fuego_nuevo = EXCLUDED.min_fuego_nuevo;
-- La identidad tiene que quedar por encima de 10 (una zona nueva cargada a mano no puede chocar con estas).
SELECT setval(pg_get_serial_sequence('zones', 'zone_id'), GREATEST((SELECT max(zone_id) FROM zones), 1));

-- Los ítems: el drop, el arma de cada clase, el amuleto y la Reliquia. No toca el emoji si ya se lo cargaron.
DROP TABLE IF EXISTS z610_items;
CREATE TEMP TABLE z610_items AS
SELECT z.k, z.zone_id, z.drop_name AS name, 'Material' AS type, z.drop_sell AS sell, (z.drop_sell * 13 / 10) AS buy, 0 AS stat, NULL::TEXT AS family, NULL::TEXT AS class, 17 AS dust, 'drop' AS role FROM z610 z
UNION ALL SELECT z.k, z.zone_id, 'Espada de ' || z.suffix, 'Weapon', z.weapon_atk * 4, z.weapon_atk * 52 / 10, z.weapon_atk, 'Espadas', 'Guerrero', NULL, 'weapon_metal' FROM z610 z
UNION ALL SELECT z.k, z.zone_id, 'Dagas de ' || z.suffix, 'Weapon', z.weapon_atk * 4, z.weapon_atk * 52 / 10, z.weapon_atk, 'Dagas', 'Ninja', NULL, 'weapon_metal' FROM z610 z
UNION ALL SELECT z.k, z.zone_id, 'Arco de ' || z.suffix, 'Weapon', z.weapon_atk * 4, z.weapon_atk * 52 / 10, z.weapon_atk, 'Arcos', 'Arquero', NULL, 'weapon_wood' FROM z610 z
UNION ALL SELECT z.k, z.zone_id, 'Grimorio de ' || z.suffix, 'Weapon', z.weapon_atk * 4, z.weapon_atk * 52 / 10, z.weapon_atk, 'Grimorios', 'Hechicero', NULL, 'weapon_wood' FROM z610 z
UNION ALL SELECT z.k, z.zone_id, 'Amuleto de ' || z.suffix, 'Amulet', z.amulet_def * 6, z.amulet_def * 78 / 10, z.amulet_def, NULL, NULL, NULL, 'amulet' FROM z610 z
UNION ALL SELECT z.k, z.zone_id, 'Reliquia de ' || z.suffix, 'Reliquia', 0, 0, 0, NULL, NULL, NULL, 'relic' FROM z610 z;

-- Emoji unicode de relleno hasta que llegue el arte propio (los prompts están en docs/arte-zonas-6-10.md): se pone solo si el ítem no tiene uno, así el arte
-- cargado después con update_item_emojis.sql nunca se pisa. Mismo criterio que la 🍖 de la Comida de Dragón en add_dragon_food.sql.
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price, weapon_family, class_requirement, dust_value, emoji)
SELECT name, type, 'Mítico', stat, sell, buy, family, class, dust,
       CASE
           WHEN role = 'relic' THEN '🗝️'
           WHEN type = 'Amulet' THEN '📿'
           WHEN family = 'Espadas' THEN '🗡️'
           WHEN family = 'Dagas' THEN '🔪'
           WHEN family = 'Arcos' THEN '🏹'
           WHEN family = 'Grimorios' THEN '📖'
           ELSE CASE k WHEN 0 THEN '🧂' WHEN 1 THEN '🦷' WHEN 2 THEN '🌑' WHEN 3 THEN '🧊' ELSE '🪶' END
       END
FROM z610_items
ON CONFLICT (name) DO UPDATE
    SET type = EXCLUDED.type, rarity = EXCLUDED.rarity, stat_value = EXCLUDED.stat_value, sell_price = EXCLUDED.sell_price, buy_price = EXCLUDED.buy_price,
        weapon_family = EXCLUDED.weapon_family, class_requirement = EXCLUDED.class_requirement, dust_value = EXCLUDED.dust_value,
        emoji = COALESCE(items.emoji, EXCLUDED.emoji);

-- Los monstruos: uno de caza (suelta el drop) y uno de viaje (no suelta nada). Sin jefe.
INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward, is_boss, is_travel)
SELECT zone_id, hunt_name, hunt_emoji, hp_min, hp_max, dmg_min, dmg_max, gold_b, xp_b, false, false FROM z610
UNION ALL
SELECT zone_id, travel_name, travel_emoji, t_hp_min, t_hp_max, t_dmg_min, t_dmg_max, gold_b, xp_b, false, true FROM z610
ON CONFLICT (name) DO UPDATE
    SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji, min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage, max_damage = EXCLUDED.max_damage,
        gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward, is_boss = EXCLUDED.is_boss, is_travel = EXCLUDED.is_travel;

DELETE FROM monster_drops WHERE monster_id IN (SELECT m.monster_id FROM monsters m JOIN z610 z ON m.name IN (z.hunt_name, z.travel_name));
INSERT INTO monster_drops (monster_id, item_id)
SELECT m.monster_id, i.item_id FROM z610 z JOIN monsters m ON m.name = z.hunt_name JOIN items i ON i.name = z.drop_name;

-- Las recetas (zona, afinidad para las armas de clase como en las zonas 1 a 5) y sus ingredientes, reescritos enteros.
INSERT INTO recipes (result_item_id, gold_cost, zone_id, affinity)
SELECT i.item_id,
       CASE x.role WHEN 'amulet' THEN z.amulet_gold WHEN 'relic' THEN z.relic_gold ELSE z.weapon_gold END,
       x.zone_id, x.role LIKE 'weapon%'
FROM z610_items x JOIN z610 z ON z.k = x.k JOIN items i ON i.name = x.name
WHERE x.role <> 'drop'
ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost, zone_id = EXCLUDED.zone_id, affinity = EXCLUDED.affinity;

DELETE FROM recipe_ingredients WHERE recipe_id IN (SELECT r.recipe_id FROM recipes r JOIN items i ON i.item_id = r.result_item_id JOIN z610_items x ON x.name = i.name);

DROP TABLE IF EXISTS z610_ing;
CREATE TEMP TABLE z610_ing (role TEXT, item_name TEXT, base INTEGER, per_zone INTEGER);
-- cantidad = base + per_zone × k (k = 0 en la Zona 6 … 4 en la 10). '{drop}' = el drop de la zona. A diferencia de las zonas 1 a 5, acá los Legendarios (Ébano, Zafiro) van en CANTIDAD (el dueño:
-- «el 1 de ébano y zafiro, cualquiera; poné cantidades grandes, con FN 7 salen bien») y cada arma de clase pide UN Mítico de recolección: el Meteorito las de metal y la Corteza las de
-- madera (el dueño: «es buena idea»). Se compran también en el Tabernero del Inframundo.
INSERT INTO z610_ing VALUES
    ('weapon_metal', '{drop}', 8, 2), ('weapon_metal', 'Hierro', 20, 6), ('weapon_metal', 'Gema de Zafiro', 4, 2), ('weapon_metal', 'Carbón', 30, 12), ('weapon_metal', 'Fragmento de Meteorito', 1, 0),
    ('weapon_wood', '{drop}', 8, 2), ('weapon_wood', 'Madera de Nogal', 12, 5), ('weapon_wood', 'Madera de Ébano', 4, 2), ('weapon_wood', 'Madera de Roble', 40, 12), ('weapon_wood', 'Corteza del Árbol de Vida', 1, 0),
    ('amulet', '{drop}', 10, 2), ('amulet', 'Oro Puro', 10, 4), ('amulet', 'Gema de Zafiro', 4, 2), ('amulet', 'Piedra', 100, 50),
    ('relic', '{drop}', 5, 1), ('relic', 'Cofre de Escoria', 1, 0), ('relic', 'Madera de Pino', 100, 50), ('relic', 'Piedra', 100, 50);

INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT r.recipe_id, ing_item.item_id, g.base + g.per_zone * x.k
FROM z610_items x
JOIN z610 z ON z.k = x.k
JOIN items res ON res.name = x.name
JOIN recipes r ON r.result_item_id = res.item_id
JOIN z610_ing g ON g.role = x.role
JOIN items ing_item ON ing_item.name = CASE g.item_name WHEN '{drop}' THEN z.drop_name ELSE g.item_name END;

-- Las cajas de cada zona (zone_boxes): las mismas de la Zona 5, salvo el viaje, que da el Cofre de Escoria (lo que pide la Reliquia) al 15 %.
INSERT INTO zone_boxes (zone_id, role, box_item_id, chance_percent)
SELECT z.zone_id, b.role, b.box_item_id, b.chance_percent FROM z610 z CROSS JOIN zone_boxes b WHERE b.zone_id = 5 AND b.role <> 'travel'
ON CONFLICT (zone_id, role) DO UPDATE SET box_item_id = EXCLUDED.box_item_id, chance_percent = EXCLUDED.chance_percent;
INSERT INTO zone_boxes (zone_id, role, box_item_id, chance_percent)
SELECT z.zone_id, 'travel', (SELECT item_id FROM items WHERE name = 'Cofre de Escoria'), 15 FROM z610 z
ON CONFLICT (zone_id, role) DO UPDATE SET box_item_id = EXCLUDED.box_item_id, chance_percent = EXCLUDED.chance_percent;

-- Verificación: si algo no quedó como se espera, se corta acá (el script entero corre en la transacción de psql -1 o se ve el error).
DO $$
DECLARE bad TEXT;
BEGIN
    IF (SELECT count(*) FROM zones WHERE zone_id BETWEEN 6 AND 10 AND kind = 'normal' AND min_fuego_nuevo > 0) <> 5 THEN
        RAISE EXCEPTION 'add_zones_6_10: faltan zonas 6 a 10';
    END IF;
    SELECT string_agg(z.zone_id::TEXT, ', ') INTO bad FROM z610 z
    WHERE (SELECT count(*) FROM monsters m WHERE m.zone_id = z.zone_id AND NOT m.is_boss AND NOT m.is_travel) <> 1
       OR (SELECT count(*) FROM monsters m WHERE m.zone_id = z.zone_id AND m.is_travel) <> 1
       OR (SELECT count(*) FROM monsters m WHERE m.zone_id = z.zone_id AND m.is_boss) <> 0
       OR (SELECT count(*) FROM recipes r WHERE r.zone_id = z.zone_id) <> 6
       OR (SELECT count(*) FROM zone_boxes b WHERE b.zone_id = z.zone_id) <> 6
       OR EXISTS (SELECT 1 FROM recipes r JOIN items res ON res.item_id = r.result_item_id WHERE r.zone_id = z.zone_id
                  AND (SELECT count(*) FROM recipe_ingredients ri WHERE ri.recipe_id = r.recipe_id) <> CASE res.type WHEN 'Weapon' THEN 5 ELSE 4 END);
    IF bad IS NOT NULL THEN
        RAISE EXCEPTION 'add_zones_6_10: zonas incompletas: %', bad;
    END IF;
END $$;
