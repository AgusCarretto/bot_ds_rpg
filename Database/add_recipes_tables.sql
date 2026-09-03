-- =========================================================
-- Migración: recetas del herrero pasan a vivir en la base (antes eran código, ver
-- GameData/CraftingCatalog.cs — ese archivo ya no existe, Modules/ForgeModule.cs lee de acá).
-- Ejecutar UNA SOLA VEZ, DESPUÉS de add_class_requirement.sql (schema.sql ya las tiene si es
-- una instalación nueva). Esta migración en sí SÍ es re-ejecutable (los CREATE TABLE son
-- IF NOT EXISTS); lo que no es re-ejecutable es Database/seed_recipes.sql más abajo en la cadena.
-- =========================================================

-- Un ítem resultado tiene como máximo una receta (UNIQUE); una receta puede tener varios
-- ingredientes (recipe_ingredients, 1 a N).
CREATE TABLE IF NOT EXISTS recipes (
    recipe_id       INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    result_item_id  INTEGER NOT NULL UNIQUE REFERENCES items (item_id) ON DELETE CASCADE,
    gold_cost       INTEGER NOT NULL DEFAULT 0 CHECK (gold_cost >= 0)
);

CREATE TABLE IF NOT EXISTS recipe_ingredients (
    recipe_id  INTEGER NOT NULL REFERENCES recipes (recipe_id) ON DELETE CASCADE,
    item_id    INTEGER NOT NULL REFERENCES items (item_id) ON DELETE CASCADE,
    quantity   INTEGER NOT NULL CHECK (quantity > 0),
    PRIMARY KEY (recipe_id, item_id)
);

CREATE INDEX IF NOT EXISTS idx_recipe_ingredients_recipe_id ON recipe_ingredients (recipe_id);
