-- =========================================================
-- Completa tres huecos del catálogo que nunca quedaron en ningún script versionado
-- (cargados a mano en una máquina y perdidos al reinstalar en otra, ver MEJORAS.md):
--
-- 1. Progresión base Común/Raro/Épico de la familia Espadas. Dagas/Arcos/Grimorios ya la
--    tenían (ver Database/add_weapon_family.sql); Espadas solo tenía sus 2 piezas Legendario
--    de Guerrero. Los tres nombres de abajo ya estaban referenciados por nombre en
--    add_weapon_family.sql/seed_recipes.sql (asumían que existían), pero nunca se habían
--    creado como ítem — "Hacha de Hierro MK3" incluso se quedó sin receta por esto
--    (seed_recipes.sql la insertaba con 0 filas, sin error, tal como el propio archivo avisaba).
-- 2. Catálogo de Consumibles (type = 'Consumable'): no existía NINGUNO, por lo que /shop view,
--    /shop buy y /use no tenían nada para mostrar ni usar. Progresión de 2 ítems por rareza,
--    temática asado/mate acorde al brief original del proyecto.
--
-- Ejecutar UNA SOLA VEZ (no es idempotente en el sentido de "seguro reversible", pero usa
-- ON CONFLICT DO NOTHING como el resto de los seeds: correrlo de nuevo no duplica filas
-- porque no hay una clave natural en conflicto real, así que sí duplicaría si se corre dos
-- veces — mismo comportamiento que seed.sql y seed_class_gear_and_monster_drops.sql).
-- Si tu base ya tiene estos nombres cargados a mano, no pasa nada: ON CONFLICT DO NOTHING no
-- inserta nada porque no hay unicidad real, así que revisá antes con
-- "SELECT name FROM items WHERE name IN (...)" si no estás seguro.
-- =========================================================

INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price, weapon_family) VALUES
('Espada de Madera',   'Weapon', 'Común', 5,  20,  26,  'Espadas'),
('Hoja de Acero Puro', 'Weapon', 'Raro',  15, 150, 195, 'Espadas'),
('Hacha de Hierro MK3','Weapon', 'Épico', 35, 500, 650, 'Espadas')
ON CONFLICT DO NOTHING;

-- Receta de "Hacha de Hierro MK3" (genérica, sin class_requirement): la de seed_recipes.sql no
-- insertaba nada porque el ítem no existía en ese momento. Mismo patrón upsert re-ejecutable.
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 150 FROM items WHERE name = 'Hacha de Hierro MK3'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Hierro', 5), ('Cuero Grueso', 3)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- stat_value = HP que restaura (leído directo por Modules/TavernModule.cs y Modules/UseModule.cs).
-- Precios calculados para eficiencia pareja (~4.4-5.0 HP por oro de buy_price) en toda la escala
-- de rareza — ver Database/rebalance_consumables.sql: la primera versión escalaba el precio más
-- rápido que el heal, así que el ítem Común más barato siempre rendía más por oro que cualquier
-- otro, y nunca convenía comprar nada más caro. Con eficiencia pareja, lo caro sigue costando más
-- y curando más de un saque, pero deja de ser matemáticamente una mala compra.
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price) VALUES
('Mate Amargo',                           'Consumable', 'Común',      15,  2,  3),
('Pan Casero',                            'Consumable', 'Común',      20,  3,  4),
('Empanada de Carne',                     'Consumable', 'Raro',       40,  7,  9),
('Choripán',                              'Consumable', 'Raro',       50,  8,  11),
('Vacío al Disco',                        'Consumable', 'Épico',      80,  14, 18),
('Asado de Tira',                         'Consumable', 'Épico',      100, 17, 22),
('Cordero Patagónico',                    'Consumable', 'Legendario', 150, 25, 33),
('Matambre Arrollado',                    'Consumable', 'Legendario', 180, 31, 40),
('Asado Completo del Domingo en Familia', 'Consumable', 'Mítico',     250, 43, 56),
('Mate Dulce de la Abuela',               'Consumable', 'Mítico',     300, 52, 67)
ON CONFLICT DO NOTHING;
