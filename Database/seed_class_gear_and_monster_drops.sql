-- =========================================================
-- Asado y Acero RPG — Drops específicos de monstruos + materiales de recolección que faltaban +
-- equipamiento forjable por clase (2 armas + 2 amuletos por clase, ver seed_recipes.sql).
-- Ejecutar UNA SOLA VEZ, después de add_class_requirement.sql y add_recipes_tables.sql
-- (no es idempotente: correrlo de nuevo duplica las filas, igual que seed.sql). Los ítems tienen
-- que existir ANTES de correr seed_recipes.sql (las recetas los referencian por nombre).
-- =========================================================

-- 🩸 Drops de los 4 monstruos nuevos de /hunt (ver GameData/MonsterCatalog.cs). type = 'Material'
-- a propósito: es un tipo NUEVO y separado de 'Madera'/'Mineral' (botín de /chop y /mine) para que
-- Modules/AdventureModule.ResolveDroppedItemAsync nunca pueda entregar madera o piedra como
-- recompensa de combate, ni viceversa.
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price) VALUES
-- Jabalí Rabioso 🐗
('Cuero Grueso',           'Material', 'Común', 0, 2,  3),
('Colmillo de Jabalí',     'Material', 'Raro',  0, 10, 13),
-- Lobisón de las Cenizas 🐺
('Garra Maldita',          'Material', 'Raro',  0, 10, 13),
('Pelaje Oscuro',          'Material', 'Común', 0, 2,  3),
-- Gólem de Escoria 🗿
('Núcleo Ígneo',           'Material', 'Épico', 0, 30, 39),
('Piedra Caliente',        'Material', 'Común', 0, 2,  3),
-- Cuatrero No-Muerto 💀
('Hueso Añejo',            'Material', 'Raro',  0, 10, 13),
('Tela Rasgada',           'Material', 'Común', 0, 2,  3)
ON CONFLICT DO NOTHING;

-- ⛏️ Carbón: material de /mine que faltaba en el catálogo original (seed.sql), lo piden varias
-- recetas nuevas (Ninja/Hechicero). Común, NO Raro a propósito: RollGatheringRarity() sortea
-- primero la rareza y DESPUÉS un ítem al azar entre los que la tienen — poner a Carbón en Raro
-- junto a Hierro hubiera partido al medio la probabilidad de sacar Hierro (25% -> 12.5% por
-- /mine), rompiendo el balance de las 6 recetas que ya pedían Hierro. En Común comparte pool con
-- Piedra (que tiene mucho menos uso en recetas), sin tocar la tasa de Hierro.
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price) VALUES
('Carbón', 'Mineral', 'Común', 0, 2, 3)
ON CONFLICT DO NOTHING;

-- 🔨 "Amuleto del Levantador": recuperado del viejo CraftingCatalog.cs (ítem genérico, sin
-- class_requirement) — puede que ya exista en tu base si lo habías cargado a mano; si no, se crea acá.
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price) VALUES
('Amuleto del Levantador', 'Amulet', 'Épico', 15, 60, 78)
ON CONFLICT DO NOTHING;

-- ⚔️ Equipamiento forjable por clase: 2 armas + 2 amuletos por clase (16 en total). Rareza
-- Legendario para las 16 — se diferencian por stat_value/precio según la complejidad de su receta
-- (2 ingredientes vs 3, ver seed_recipes.sql), no por rareza. weapon_family define la sinergia de
-- daño de clase (ver GameData/ClassWeaponSynergy.cs); los amuletos no tienen family. No se pueden
-- comprar en /shop (solo vende Consumibles) — la única forma de conseguirlas es /forge make.
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price, weapon_family, class_requirement) VALUES
-- 🛡️ Guerrero
('Mazo de Escoria',            'Weapon', 'Legendario', 45, 180, 235, 'Espadas',   'Guerrero'),
('Facón de Hueso Añejo',       'Weapon', 'Legendario', 55, 220, 285, 'Espadas',   'Guerrero'),
('Collar de Hueso',            'Amulet', 'Legendario', 20, 80,  105, NULL,        'Guerrero'),
('Hombreras de Cuero Grueso',  'Amulet', 'Legendario', 20, 80,  105, NULL,        'Guerrero'),
-- 🏹 Arquero
('Arco de Caza Mayor',         'Weapon', 'Legendario', 55, 220, 285, 'Arcos',     'Arquero'),
('Boleadoras de Escoria',      'Weapon', 'Legendario', 55, 220, 285, 'Arcos',     'Arquero'),
('Ojo de Jabalí',              'Amulet', 'Legendario', 20, 80,  105, NULL,        'Arquero'),
('Carcaj de Pelaje Oscuro',    'Amulet', 'Legendario', 28, 110, 145, NULL,        'Arquero'),
-- 🥷 Ninja
('Dagas de Garra Maldita',     'Weapon', 'Legendario', 45, 180, 235, 'Dagas',     'Ninja'),
('Cuchillos de Ceniza',        'Weapon', 'Legendario', 55, 220, 285, 'Dagas',     'Ninja'),
('Capa de Pelaje Oscuro',      'Amulet', 'Legendario', 20, 80,  105, NULL,        'Ninja'),
('Botas de Silencio',          'Amulet', 'Legendario', 28, 110, 145, NULL,        'Ninja'),
-- 🔮 Hechicero
('Báculo de Tizón',            'Weapon', 'Legendario', 55, 220, 285, 'Grimorios', 'Hechicero'),
('Códice de las Brasas',       'Weapon', 'Legendario', 55, 220, 285, 'Grimorios', 'Hechicero'),
('Mate Tallado en Cenizas',    'Amulet', 'Legendario', 28, 110, 145, NULL,        'Hechicero'),
('Bombilla de Hierro Maldito', 'Amulet', 'Legendario', 20, 80,  105, NULL,        'Hechicero')
ON CONFLICT DO NOTHING;
