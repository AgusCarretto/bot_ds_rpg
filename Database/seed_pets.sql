-- =========================================================
-- v0.10.0: el catálogo de las mascotas. Re-ejecutable (ON CONFLICT) y se verifica solo: si falta una zona o queda mal el recuento, FALLA en voz alta.
-- Va al final de run_fresh_install.sql (necesita zonas e ítems) y también lo corre add_pets.sql en una base viva. Las TABLAS están en schema.sql.
--
--   · 5 huevos (items.type = 'Huevo'): uno por zona, se entregan con el cofre la PRIMERA vez que se vence al jefe de esa zona y se abren con /open.
--   · Comida para Mascotas (items.type = 'PetFood'): el consumible que las hace crecer (una vez por hora por mascota, GameData/PetRules.cs). Se compra en la tienda.
--   · 5 especies, una por zona: cada una da UN bonus que crece con su nivel hasta max_bonus_percent (nivel 10). Todas las que tengas valen a la vez.
--       gold     % más de oro en /hunt, /travel, /boss, /raid y /autohunt
--       xp       % más de EXP en esas mismas peleas
--       defense  % más de tu defensa total, en peleas contra monstruos (no en duelos ni en la Arena)
--       drop     % MÁS chances de drop de los monstruos de cacería y de viaje (relativo: 6 % sobre un 6 % de drop da 6,36 %); no los cofres de los jefes
--   Los topes (5 / 5 / 6 / 6 / 10) los eligió el dueño: la Zona 5 da más EXP "para que la segunda vuelta sea más sencilla" (EXP total máxima 15 %).
--   El emoji de cada ítem y especie es Unicode hasta que el dueño suba los retratos; update_item_emojis.sql los pisa con los de la aplicación.
-- =========================================================

INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price, emoji) VALUES
    ('Huevo de la Pradera',          'Huevo',   'Común',      0,  0,   0, '🥚'),
    ('Huevo del Bosque de Cenizas',  'Huevo',   'Raro',       0,  0,   0, '🥚'),
    ('Huevo de las Minas',           'Huevo',   'Épico',      0,  0,   0, '🥚'),
    ('Huevo de la Cordillera',       'Huevo',   'Legendario', 0,  0,   0, '🥚'),
    ('Huevo del Cráter',             'Huevo',   'Mítico',     0,  0,   0, '🥚'),
    ('Comida para Mascotas',         'PetFood', 'Común',      0, 25, 100, '🦴')
ON CONFLICT (name) DO UPDATE SET
    type = EXCLUDED.type, rarity = EXCLUDED.rarity, stat_value = EXCLUDED.stat_value, sell_price = EXCLUDED.sell_price, buy_price = EXCLUDED.buy_price;

INSERT INTO pet_species (zone_id, name, emoji, bonus_kind, max_bonus_percent, egg_item_id)
SELECT z.zone_id, v.name, v.emoji, v.bonus_kind, v.max_bonus_percent, e.item_id
FROM (VALUES
    ('Praderas del Mate',    'Ñandusito',         '🐦', 'gold',    5.0,  'Huevo de la Pradera'),
    ('Bosque de Cenizas',    'Cachorro de Puma',  '🐆', 'xp',      5.0,  'Huevo del Bosque de Cenizas'),
    ('Minas del Yunque',     'Gólem de bolsillo', '🪨', 'defense', 6.0,  'Huevo de las Minas'),
    ('Cordillera del Fuego', 'Salamandrita',      '🦎', 'drop',    6.0,  'Huevo de la Cordillera'),
    ('Cráter de la Escoria', 'Quimerita',         '🐉', 'xp',      10.0, 'Huevo del Cráter')
) AS v (zone_name, name, emoji, bonus_kind, max_bonus_percent, egg_name)
JOIN zones z ON z.name = v.zone_name
JOIN items e ON e.name = v.egg_name
ON CONFLICT (name) DO UPDATE SET
    zone_id = EXCLUDED.zone_id, bonus_kind = EXCLUDED.bonus_kind, max_bonus_percent = EXCLUDED.max_bonus_percent, egg_item_id = EXCLUDED.egg_item_id;

-- Verificación: las 5 especies (una por zona, con su huevo), los 5 huevos y la comida.
DO $$
DECLARE
    v_species INTEGER;
    v_eggs    INTEGER;
    v_food    INTEGER;
BEGIN
    SELECT count(*) INTO v_species FROM pet_species;
    SELECT count(*) INTO v_eggs FROM items WHERE type = 'Huevo';
    SELECT count(*) INTO v_food FROM items WHERE type = 'PetFood' AND name = 'Comida para Mascotas';

    IF v_species <> 5 THEN
        RAISE EXCEPTION 'seed_pets: hay % especies y tienen que ser 5 (¿falta alguna zona? los nombres de zona tienen que coincidir)', v_species;
    END IF;
    IF v_eggs <> 5 OR v_food <> 1 THEN
        RAISE EXCEPTION 'seed_pets: hay % huevos (tienen que ser 5) y % comidas para mascotas (tiene que ser 1)', v_eggs, v_food;
    END IF;
    IF EXISTS (SELECT 1 FROM pet_species ps JOIN items i ON i.item_id = ps.egg_item_id WHERE i.type <> 'Huevo') THEN
        RAISE EXCEPTION 'seed_pets: alguna especie apunta a un huevo que no es de tipo Huevo';
    END IF;
END $$;
