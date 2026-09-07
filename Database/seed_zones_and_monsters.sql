-- =========================================================
-- Asado y Acero RPG — Sistema de Zonas: 5 zonas escalonadas + 14 monstruos de /hunt (4 migrados
-- desde GameData/MonsterCatalog.cs, que ahora quedan homeados en Zona 1, + 10 nuevos, 2 por zona)
-- + los 20 materiales nuevos que dropean los 10 monstruos nuevos.
--
-- Ejecutar DESPUÉS de:
--   - Database/add_zones_and_monsters.sql (o schema.sql en una instalación nueva) — crea las tablas.
--   - Database/seed_class_gear_and_monster_drops.sql — los 4 monstruos migrados dropean ítems
--     creados ahí (Cuero Grueso, Colmillo de Jabalí, Garra Maldita, Pelaje Oscuro, Núcleo Ígneo,
--     Piedra Caliente, Hueso Añejo, Tela Rasgada). Si todavía no corriste ese script, este falla.
--
-- Totalmente re-ejecutable (ídem seed_recipes.sql): zones.name y monsters.name son UNIQUE, así
-- que cada bloque hace upsert (ON CONFLICT ... DO UPDATE), y los items nuevos son
-- ON CONFLICT (name) DO NOTHING. Los 4 monstruos migrados NO se borran de MonsterCatalog.cs acá
-- (eso es un cambio de código aparte, ver GameData/MonsterCatalog.cs) — este script solo toca la base.
-- =========================================================

-- ---------------------------------------------------------
-- 🗺️ Las 5 zonas
-- ---------------------------------------------------------
INSERT INTO zones (name, description, min_level, emoji) VALUES
('Praderas del Mate',
 'Las llanuras donde todo empieza: pasto alto, viento fresco y las primeras alimañas que todo aventurero enfrenta antes de aprender a manejar el acero.',
 1, '🌾'),
('Bosque de Cenizas',
 'Un bosque quemado que nunca terminó de apagarse. Entre troncos negros y espíritus errantes, la caza empieza a cobrarse algo más que sudor.',
 5, '🌲'),
('Minas del Yunque',
 'Túneles excavados por generaciones de herreros, ahora tomados por gólems de escoria y criaturas que confunden el metal con carne.',
 10, '⛏️'),
('Cordillera del Fuego',
 'Picos volcánicos donde el aire quema los pulmones. Solo quienes ya forjaron su propio acero se animan a subir.',
 15, '🌋'),
('Cráter de la Escoria',
 'El corazón del mundo, donde la tierra misma se derritió. Lo que habita acá no perdona errores de un guerrero sin experiencia.',
 20, '☠️')
ON CONFLICT (name) DO UPDATE SET
    description = EXCLUDED.description,
    min_level   = EXCLUDED.min_level,
    emoji       = EXCLUDED.emoji;

-- ---------------------------------------------------------
-- 🩸 Materiales nuevos: 2 por cada uno de los 10 monstruos nuevos (20 en total). Rareza escalada
-- por zona siguiendo la misma escala de precios que Madera/Mineral (seed.sql): Común 2/3,
-- Raro 10/13, Épico 30/39, Legendario 100/130, Mítico 500/650.
-- ---------------------------------------------------------
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price) VALUES
-- Zona 1: Praderas del Mate (Común) — Ñandú Salvaje, Perro Cimarrón
('Pluma de Ñandú',           'Material', 'Común', 0, 2, 3),
('Cuero Curtido de Pradera', 'Material', 'Común', 0, 2, 3),
('Colmillo de Cimarrón',     'Material', 'Común', 0, 2, 3),
('Collar de Cuero Viejo',    'Material', 'Común', 0, 2, 3),
-- Zona 2: Bosque de Cenizas (Raro) — Puma de las Cenizas, Espíritu del Monte
('Garra de Puma Cenizo',     'Material', 'Raro', 0, 10, 13),
('Ceniza Bendita',           'Material', 'Raro', 0, 10, 13),
('Esencia Espectral',        'Material', 'Raro', 0, 10, 13),
('Rama Carbonizada',         'Material', 'Raro', 0, 10, 13),
-- Zona 3: Minas del Yunque (Épico) — Gólem del Yunque, Excavador Profundo
('Yunque Fragmentado',       'Material', 'Épico', 0, 30, 39),
('Escoria Metálica Densa',   'Material', 'Épico', 0, 30, 39),
('Gema en Bruto',            'Material', 'Épico', 0, 30, 39),
('Polvo de Mina Sagrada',    'Material', 'Épico', 0, 30, 39),
-- Zona 4: Cordillera del Fuego (Legendario) — Salamandra Infernal, Coloso de Magma
('Escama Ígnea',             'Material', 'Legendario', 0, 100, 130),
('Aliento de Fuego Eterno',  'Material', 'Legendario', 0, 100, 130),
('Núcleo de Magma',          'Material', 'Legendario', 0, 100, 130),
('Roca Volcánica Pura',      'Material', 'Legendario', 0, 100, 130),
-- Zona 5: Cráter de la Escoria (Mítico) — Devorador de Almas, Titán de Escoria
('Fragmento de Alma',        'Material', 'Mítico', 0, 500, 650),
('Ceniza del Abismo',        'Material', 'Mítico', 0, 500, 650),
('Corazón de Titán',         'Material', 'Mítico', 0, 500, 650),
('Escoria Pura del Cráter',  'Material', 'Mítico', 0, 500, 650)
ON CONFLICT (name) DO NOTHING;

-- ---------------------------------------------------------
-- 🐗 Zona 1: Praderas del Mate — los 4 monstruos que ya vivían en GameData/MonsterCatalog.cs,
-- ahora homeados acá (mismos stats/drops de siempre, gold_reward/xp_reward en 0 para que la
-- recompensa de /hunt no cambie ni un poco respecto a lo que ya había), + 2 nuevos.
-- ---------------------------------------------------------

WITH monster AS (
    INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward)
    SELECT zone_id, 'Jabalí Rabioso', '🐗', 28, 42, 6, 13, 0, 0 FROM zones WHERE name = 'Praderas del Mate'
    ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
        min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward
    RETURNING monster_id
)
INSERT INTO monster_drops (monster_id, item_id)
SELECT monster.monster_id, items.item_id FROM monster
CROSS JOIN (VALUES ('Cuero Grueso'), ('Colmillo de Jabalí')) AS drop(name)
JOIN items ON items.name = drop.name
ON CONFLICT DO NOTHING;

WITH monster AS (
    INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward)
    SELECT zone_id, 'Lobisón de las Cenizas', '🐺', 30, 48, 7, 15, 0, 0 FROM zones WHERE name = 'Praderas del Mate'
    ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
        min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward
    RETURNING monster_id
)
INSERT INTO monster_drops (monster_id, item_id)
SELECT monster.monster_id, items.item_id FROM monster
CROSS JOIN (VALUES ('Garra Maldita'), ('Pelaje Oscuro')) AS drop(name)
JOIN items ON items.name = drop.name
ON CONFLICT DO NOTHING;

WITH monster AS (
    INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward)
    SELECT zone_id, 'Gólem de Escoria', '🗿', 35, 55, 5, 12, 0, 0 FROM zones WHERE name = 'Praderas del Mate'
    ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
        min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward
    RETURNING monster_id
)
INSERT INTO monster_drops (monster_id, item_id)
SELECT monster.monster_id, items.item_id FROM monster
CROSS JOIN (VALUES ('Núcleo Ígneo'), ('Piedra Caliente')) AS drop(name)
JOIN items ON items.name = drop.name
ON CONFLICT DO NOTHING;

WITH monster AS (
    INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward)
    SELECT zone_id, 'Cuatrero No-Muerto', '💀', 25, 40, 6, 14, 0, 0 FROM zones WHERE name = 'Praderas del Mate'
    ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
        min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward
    RETURNING monster_id
)
INSERT INTO monster_drops (monster_id, item_id)
SELECT monster.monster_id, items.item_id FROM monster
CROSS JOIN (VALUES ('Hueso Añejo'), ('Tela Rasgada')) AS drop(name)
JOIN items ON items.name = drop.name
ON CONFLICT DO NOTHING;

WITH monster AS (
    INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward)
    SELECT zone_id, 'Ñandú Salvaje', '🦤', 20, 32, 4, 9, 0, 0 FROM zones WHERE name = 'Praderas del Mate'
    ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
        min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward
    RETURNING monster_id
)
INSERT INTO monster_drops (monster_id, item_id)
SELECT monster.monster_id, items.item_id FROM monster
CROSS JOIN (VALUES ('Pluma de Ñandú'), ('Cuero Curtido de Pradera')) AS drop(name)
JOIN items ON items.name = drop.name
ON CONFLICT DO NOTHING;

WITH monster AS (
    INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward)
    SELECT zone_id, 'Perro Cimarrón', '🐕', 22, 35, 5, 10, 0, 0 FROM zones WHERE name = 'Praderas del Mate'
    ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
        min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward
    RETURNING monster_id
)
INSERT INTO monster_drops (monster_id, item_id)
SELECT monster.monster_id, items.item_id FROM monster
CROSS JOIN (VALUES ('Colmillo de Cimarrón'), ('Collar de Cuero Viejo')) AS drop(name)
JOIN items ON items.name = drop.name
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------
-- 🐆 Zona 2: Bosque de Cenizas
-- ---------------------------------------------------------

WITH monster AS (
    INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward)
    SELECT zone_id, 'Puma de las Cenizas', '🐆', 55, 85, 14, 24, 15, 12 FROM zones WHERE name = 'Bosque de Cenizas'
    ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
        min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward
    RETURNING monster_id
)
INSERT INTO monster_drops (monster_id, item_id)
SELECT monster.monster_id, items.item_id FROM monster
CROSS JOIN (VALUES ('Garra de Puma Cenizo'), ('Ceniza Bendita')) AS drop(name)
JOIN items ON items.name = drop.name
ON CONFLICT DO NOTHING;

WITH monster AS (
    INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward)
    SELECT zone_id, 'Espíritu del Monte', '👻', 60, 95, 13, 22, 15, 12 FROM zones WHERE name = 'Bosque de Cenizas'
    ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
        min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward
    RETURNING monster_id
)
INSERT INTO monster_drops (monster_id, item_id)
SELECT monster.monster_id, items.item_id FROM monster
CROSS JOIN (VALUES ('Esencia Espectral'), ('Rama Carbonizada')) AS drop(name)
JOIN items ON items.name = drop.name
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------
-- 🔨 Zona 3: Minas del Yunque
-- ---------------------------------------------------------

WITH monster AS (
    INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward)
    SELECT zone_id, 'Gólem del Yunque', '🔨', 130, 190, 28, 45, 35, 28 FROM zones WHERE name = 'Minas del Yunque'
    ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
        min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward
    RETURNING monster_id
)
INSERT INTO monster_drops (monster_id, item_id)
SELECT monster.monster_id, items.item_id FROM monster
CROSS JOIN (VALUES ('Yunque Fragmentado'), ('Escoria Metálica Densa')) AS drop(name)
JOIN items ON items.name = drop.name
ON CONFLICT DO NOTHING;

WITH monster AS (
    INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward)
    SELECT zone_id, 'Excavador Profundo', '⛏️', 120, 175, 26, 40, 35, 28 FROM zones WHERE name = 'Minas del Yunque'
    ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
        min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward
    RETURNING monster_id
)
INSERT INTO monster_drops (monster_id, item_id)
SELECT monster.monster_id, items.item_id FROM monster
CROSS JOIN (VALUES ('Gema en Bruto'), ('Polvo de Mina Sagrada')) AS drop(name)
JOIN items ON items.name = drop.name
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------
-- 🔥 Zona 4: Cordillera del Fuego
-- ---------------------------------------------------------

WITH monster AS (
    INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward)
    SELECT zone_id, 'Salamandra Infernal', '🔥', 240, 340, 60, 90, 70, 55 FROM zones WHERE name = 'Cordillera del Fuego'
    ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
        min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward
    RETURNING monster_id
)
INSERT INTO monster_drops (monster_id, item_id)
SELECT monster.monster_id, items.item_id FROM monster
CROSS JOIN (VALUES ('Escama Ígnea'), ('Aliento de Fuego Eterno')) AS drop(name)
JOIN items ON items.name = drop.name
ON CONFLICT DO NOTHING;

WITH monster AS (
    INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward)
    SELECT zone_id, 'Coloso de Magma', '🌋', 260, 360, 58, 88, 70, 55 FROM zones WHERE name = 'Cordillera del Fuego'
    ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
        min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward
    RETURNING monster_id
)
INSERT INTO monster_drops (monster_id, item_id)
SELECT monster.monster_id, items.item_id FROM monster
CROSS JOIN (VALUES ('Núcleo de Magma'), ('Roca Volcánica Pura')) AS drop(name)
JOIN items ON items.name = drop.name
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------
-- 🌑 Zona 5: Cráter de la Escoria — el daño de estos dos (140-220 de base) supera el HP máximo
-- de un jugador de nivel bajo (100 HP a nivel 1), a propósito: es la zona de riesgo real del
-- juego, pensada para personajes ya equipados con lo mejor de /forge.
-- ---------------------------------------------------------

WITH monster AS (
    INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward)
    SELECT zone_id, 'Devorador de Almas', '🌑', 400, 550, 140, 210, 130, 100 FROM zones WHERE name = 'Cráter de la Escoria'
    ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
        min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward
    RETURNING monster_id
)
INSERT INTO monster_drops (monster_id, item_id)
SELECT monster.monster_id, items.item_id FROM monster
CROSS JOIN (VALUES ('Fragmento de Alma'), ('Ceniza del Abismo')) AS drop(name)
JOIN items ON items.name = drop.name
ON CONFLICT DO NOTHING;

WITH monster AS (
    INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward)
    SELECT zone_id, 'Titán de Escoria', '👑', 450, 600, 150, 220, 130, 100 FROM zones WHERE name = 'Cráter de la Escoria'
    ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
        min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward
    RETURNING monster_id
)
INSERT INTO monster_drops (monster_id, item_id)
SELECT monster.monster_id, items.item_id FROM monster
CROSS JOIN (VALUES ('Corazón de Titán'), ('Escoria Pura del Cráter')) AS drop(name)
JOIN items ON items.name = drop.name
ON CONFLICT DO NOTHING;

-- Chequeo rápido: 14 monstruos, 6 en zona 1 (4 migrados + 2 nuevos) y 2 en cada una de las otras 4.
-- SELECT z.name, COUNT(*) FROM monsters m JOIN zones z ON z.zone_id = m.zone_id GROUP BY z.name ORDER BY z.min_level;
