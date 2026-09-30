-- =========================================================
-- Asado y Acero RPG — Rebalance de Zona 1 y de las recompensas de jefe.
--
-- SOLO para una base que YA está corriendo (una instalación nueva ya trae estos valores en
-- seed_zones_and_monsters.sql y seed_zone_bosses.sql — no hace falta correr esto).
--
-- Qué cambia:
--   1) Monstruos de /hunt de Zona 1: HP x2.0 y daño x1.3 (antes caían en ~2 golpes: un click y afuera).
--   2) Rey Jabalí (jefe de Zona 1): HP x1.4 y daño x1.15.
--   3) Recompensa (oro/XP bonus) de los 3 jefes existentes: x3.
--   (La recompensa de /travel vive en código: GameData/CombatRewardCalculator.RollTravelReward.)
--
-- Re-ejecutable: fija valores absolutos (no multiplica), correrlo dos veces deja lo mismo. Se pisa
-- por nombre de monstruo (monsters.name es UNIQUE), no por id, así que sirve aunque los ids difieran.
-- =========================================================

UPDATE monsters AS m SET min_hp = v.min_hp, max_hp = v.max_hp, min_damage = v.min_damage, max_damage = v.max_damage
FROM (VALUES
    ('Jabalí Rabioso',         56,  84,  8, 17),
    ('Lobisón de las Cenizas', 60,  96,  9, 20),
    ('Gólem de Escoria',       70, 110,  6, 16),
    ('Cuatrero No-Muerto',     50,  80,  8, 18),
    ('Ñandú Salvaje',          40,  64,  5, 12),
    ('Perro Cimarrón',         44,  70,  6, 13)
) AS v(name, min_hp, max_hp, min_damage, max_damage)
WHERE m.name = v.name;

UPDATE monsters AS m SET min_hp = v.min_hp, max_hp = v.max_hp, min_damage = v.min_damage, max_damage = v.max_damage,
                         gold_reward = v.gold_reward, xp_reward = v.xp_reward
FROM (VALUES
    ('Rey Jabalí', 140, 196, 23, 37, 150, 135)
) AS v(name, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward)
WHERE m.name = v.name;

-- Los otros dos jefes: solo la recompensa (su dureza no se toca).
UPDATE monsters AS m SET gold_reward = v.gold_reward, xp_reward = v.xp_reward
FROM (VALUES
    ('Lobisón Alfa',      300, 255),
    ('Capataz de Hierro', 600, 510)
) AS v(name, gold_reward, xp_reward)
WHERE m.name = v.name;

-- Chequeo: 6 monstruos de zona 1 + 3 jefes con los valores nuevos.
-- SELECT name, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward, is_boss
-- FROM monsters WHERE zone_id = (SELECT zone_id FROM zones WHERE min_level = 1) OR is_boss ORDER BY is_boss, name;
