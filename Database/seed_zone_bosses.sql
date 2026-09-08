-- =========================================================
-- Asado y Acero RPG — Jefes de Zona (Bloqueo de Progresión): un jefe por cada una de las primeras
-- 3 zonas, marcado is_boss = true, EXCLUIDO del pool aleatorio de /hunt (solo se enfrenta a
-- propósito con /boss — ver Repositories/MonsterRepository.GetMonstersByZoneAsync/GetBossByZoneAsync).
-- Stats muy por encima de los monstruos normales de su zona; derrotarlo sube
-- users.highest_zone_cleared, que Modules/ZoneModule.ExecuteTravelAsync exige para avanzar de zona.
--
-- Ejecutar DESPUÉS de Database/add_zone_bosses.sql (o schema.sql en instalación nueva) y de
-- Database/seed_zones_and_monsters.sql (los ítems que dropean acá no chocan con los de ahí, pero
-- referencia las zonas por nombre, que ese script ya tiene que haber creado).
--
-- Re-ejecutable (mismo patrón WITH...ON CONFLICT que seed_zones_and_monsters.sql).
-- =========================================================

-- ---------------------------------------------------------
-- 🩸 Drops exclusivos de cada jefe (rareza un escalón por encima del drop normal de su zona,
-- para que el loot de jefe se sienta especial incluso dentro de la misma zona).
-- ---------------------------------------------------------
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price) VALUES
-- Rey Jabalí (Zona 1 — normal es Común, el jefe dropea Raro)
('Colmillo del Rey Jabalí', 'Material', 'Raro', 0, 10, 13),
('Corona de Cerdas',        'Material', 'Raro', 0, 10, 13),
-- Lobisón Alfa (Zona 2 — normal es Raro, el jefe dropea Épico)
('Garra del Alfa',              'Material', 'Épico', 0, 30, 39),
('Pelaje Plateado del Alfa',     'Material', 'Épico', 0, 30, 39),
-- Capataz de Hierro (Zona 3 — normal es Épico, el jefe dropea Legendario)
('Martillo del Capataz', 'Material', 'Legendario', 0, 100, 130),
('Yunque del Capataz',   'Material', 'Legendario', 0, 100, 130)
ON CONFLICT (name) DO NOTHING;

-- ---------------------------------------------------------
-- 👑🐗 Zona 1: Rey Jabalí — normal de la zona: HP 20-55, dmg 4-15, bonus 0/0.
-- ---------------------------------------------------------
WITH monster AS (
    INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward, is_boss)
    SELECT zone_id, 'Rey Jabalí', '👑🐗', 100, 140, 20, 32, 50, 45, true FROM zones WHERE name = 'Praderas del Mate'
    ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
        min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward,
        is_boss = EXCLUDED.is_boss
    RETURNING monster_id
)
INSERT INTO monster_drops (monster_id, item_id)
SELECT monster.monster_id, items.item_id FROM monster
CROSS JOIN (VALUES ('Colmillo del Rey Jabalí'), ('Corona de Cerdas')) AS drop(name)
JOIN items ON items.name = drop.name
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------
-- 👑🐺 Zona 2: Lobisón Alfa — normal de la zona: HP 55-95, dmg 13-24, bonus 15/12.
-- ---------------------------------------------------------
WITH monster AS (
    INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward, is_boss)
    SELECT zone_id, 'Lobisón Alfa', '👑🐺', 220, 280, 40, 58, 100, 85, true FROM zones WHERE name = 'Bosque de Cenizas'
    ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
        min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward,
        is_boss = EXCLUDED.is_boss
    RETURNING monster_id
)
INSERT INTO monster_drops (monster_id, item_id)
SELECT monster.monster_id, items.item_id FROM monster
CROSS JOIN (VALUES ('Garra del Alfa'), ('Pelaje Plateado del Alfa')) AS drop(name)
JOIN items ON items.name = drop.name
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------
-- 👑⚒️ Zona 3: Capataz de Hierro — normal de la zona: HP 120-190, dmg 26-45, bonus 35/28.
-- ---------------------------------------------------------
WITH monster AS (
    INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward, is_boss)
    SELECT zone_id, 'Capataz de Hierro', '👑⚒️', 400, 500, 70, 100, 200, 170, true FROM zones WHERE name = 'Minas del Yunque'
    ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
        min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward,
        is_boss = EXCLUDED.is_boss
    RETURNING monster_id
)
INSERT INTO monster_drops (monster_id, item_id)
SELECT monster.monster_id, items.item_id FROM monster
CROSS JOIN (VALUES ('Martillo del Capataz'), ('Yunque del Capataz')) AS drop(name)
JOIN items ON items.name = drop.name
ON CONFLICT DO NOTHING;

-- Chequeo rápido: 3 jefes cargados, cada uno con 2 drops.
-- SELECT name, zone_id, is_boss FROM monsters WHERE is_boss = true ORDER BY zone_id;
