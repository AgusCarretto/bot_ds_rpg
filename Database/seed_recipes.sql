-- =========================================================
-- Asado y Acero RPG — Recetas del herrero (16: 2 armas + 2 amuletos por clase).
-- Ejecutar UNA SOLA VEZ, DESPUÉS de add_recipes_tables.sql y seed_class_gear_and_monster_drops.sql
-- (los ítems referenciados abajo tienen que existir antes de correr esto).
--
-- A diferencia de seed.sql y seed_class_gear_and_monster_drops.sql, ESTE script SÍ es
-- re-ejecutable: "recipes" tiene UNIQUE(result_item_id), así que cada bloque hace upsert
-- (ON CONFLICT ... DO UPDATE) tanto de la receta como de sus ingredientes, en vez de duplicar
-- filas. Cada bloque: 1) upsert de la receta (oro + ítem resultado, por nombre) devolviendo su
-- recipe_id con RETURNING, 2) upsert de sus ingredientes (nombre + cantidad) usando ese recipe_id.
-- =========================================================

-- ---------------------------------------------------------
-- 🛡️ Guerrero
-- ---------------------------------------------------------

-- Mazo de Escoria: Hierro + Núcleo Ígneo
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 250 FROM items WHERE name = 'Mazo de Escoria'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Hierro', 5), ('Núcleo Ígneo', 3)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- Facón de Hueso Añejo: Hierro + Hueso Añejo + Cuero Grueso
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 350 FROM items WHERE name = 'Facón de Hueso Añejo'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Hierro', 5), ('Hueso Añejo', 3), ('Cuero Grueso', 2)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- Collar de Hueso: Hueso Añejo + Tela Rasgada
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 200 FROM items WHERE name = 'Collar de Hueso'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Hueso Añejo', 5), ('Tela Rasgada', 3)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- Hombreras de Cuero Grueso: Cuero Grueso + Piedra Caliente
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 200 FROM items WHERE name = 'Hombreras de Cuero Grueso'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Cuero Grueso', 5), ('Piedra Caliente', 3)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- ---------------------------------------------------------
-- 🏹 Arquero
-- ---------------------------------------------------------

-- Arco de Caza Mayor: Madera de Pino + Cuero Grueso + Colmillo de Jabalí
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 350 FROM items WHERE name = 'Arco de Caza Mayor'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Madera de Pino', 5), ('Cuero Grueso', 3), ('Colmillo de Jabalí', 2)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- Boleadoras de Escoria: Cuero Grueso + Piedra Caliente + Hierro
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 350 FROM items WHERE name = 'Boleadoras de Escoria'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Cuero Grueso', 5), ('Piedra Caliente', 3), ('Hierro', 2)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- Ojo de Jabalí: Colmillo de Jabalí + Oro Puro
-- ("Oro" del pedido original mapeado a "Oro Puro", el ítem Mineral que ya existe en seed.sql —
-- no se creó un ítem "Oro" nuevo para no duplicar el catálogo.)
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 200 FROM items WHERE name = 'Ojo de Jabalí'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Colmillo de Jabalí', 5), ('Oro Puro', 3)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- Carcaj de Pelaje Oscuro: Pelaje Oscuro + Tela Rasgada + Madera de Pino
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 300 FROM items WHERE name = 'Carcaj de Pelaje Oscuro'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Pelaje Oscuro', 5), ('Tela Rasgada', 3), ('Madera de Pino', 2)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- ---------------------------------------------------------
-- 🥷 Ninja
-- ---------------------------------------------------------

-- Dagas de Garra Maldita: Hierro + Garra Maldita
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 250 FROM items WHERE name = 'Dagas de Garra Maldita'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Hierro', 5), ('Garra Maldita', 3)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- Cuchillos de Ceniza: Hierro + Carbón + Hueso Añejo
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 350 FROM items WHERE name = 'Cuchillos de Ceniza'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Hierro', 5), ('Carbón', 3), ('Hueso Añejo', 2)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- Capa de Pelaje Oscuro: Pelaje Oscuro + Tela Rasgada
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 200 FROM items WHERE name = 'Capa de Pelaje Oscuro'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Pelaje Oscuro', 5), ('Tela Rasgada', 3)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- Botas de Silencio: Cuero Grueso + Tela Rasgada + Garra Maldita
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 300 FROM items WHERE name = 'Botas de Silencio'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Cuero Grueso', 5), ('Tela Rasgada', 3), ('Garra Maldita', 2)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- ---------------------------------------------------------
-- 🔮 Hechicero
-- ---------------------------------------------------------

-- Báculo de Tizón: Madera de Pino + Piedra Caliente + Núcleo Ígneo
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 350 FROM items WHERE name = 'Báculo de Tizón'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Madera de Pino', 5), ('Piedra Caliente', 3), ('Núcleo Ígneo', 2)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- Códice de las Brasas: Cuero Grueso + Carbón + Núcleo Ígneo
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 350 FROM items WHERE name = 'Códice de las Brasas'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Cuero Grueso', 5), ('Carbón', 3), ('Núcleo Ígneo', 2)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- Mate Tallado en Cenizas: Madera de Pino + Carbón + Pelaje Oscuro
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 300 FROM items WHERE name = 'Mate Tallado en Cenizas'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Madera de Pino', 5), ('Carbón', 3), ('Pelaje Oscuro', 2)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- Bombilla de Hierro Maldito: Hierro + Garra Maldita
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 200 FROM items WHERE name = 'Bombilla de Hierro Maldito'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Hierro', 5), ('Garra Maldita', 3)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- ---------------------------------------------------------
-- 📦 Genéricas (sin class_requirement) — recuperadas del viejo CraftingCatalog.cs, que ya no
-- existe. Costo/cantidades más bajos que las 16 de clase a propósito: son la opción de entrada,
-- sin restricción de clase, no el tope de la progresión.
-- ---------------------------------------------------------

-- Hacha de Hierro MK3: Hierro + Cuero Grueso
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

-- Amuleto del Levantador: Piedra + Colmillo de Jabalí
WITH recipe AS (
    INSERT INTO recipes (result_item_id, gold_cost)
    SELECT item_id, 150 FROM items WHERE name = 'Amuleto del Levantador'
    ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost
    RETURNING recipe_id
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT recipe.recipe_id, items.item_id, ingredient.quantity
FROM recipe
CROSS JOIN (VALUES ('Piedra', 5), ('Colmillo de Jabalí', 3)) AS ingredient(name, quantity)
JOIN items ON items.name = ingredient.name
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;
