-- =========================================================
-- v0.15.1: el CRAFTEO baja a su ritmo de diseño. Migración para una base que YA existe (una base nueva ya trae estas cantidades en rework_drops_and_recipes.sql: NO hace falta
-- correr esto contra una instalación limpia). Re-ejecutable (deja siempre las mismas cantidades). No cambia el código: alcanza con correrlo, aunque el bot esté andando.
--
--   psql -d asado-y-acero -v ON_ERROR_STOP=1 -f rebalance_crafting.sql
--
-- Por qué (revisión de balance del 2026-10-08, pedida por el dueño: «siento que el crafteo está bravo»; simulación de recorridos completos con el combate real, los drops, la
-- recolección y las recetas): el cuello de botella de TODAS las recetas de las zonas 2 a 5 es el drop de /travel (40 % cada 30 minutos = 75 minutos por unidad), y cada arma de clase
-- pedía 3 y cada amuleto 4. Con eso el set de las cinco zonas llevaba ~40 horas de juego perfecto (el arma de clase de la Zona 5, ~17 h por un material MÍTICO de 0,5 % por acción,
-- con una cola de hasta ~40 h). Cambios:
--   1) arma de clase de las zonas 2 a 5: el drop de viaje de la zona, de 3 a 2 unidades (~150 min, el diseño original);
--   2) amuleto de las zonas 2 a 5: el drop de viaje de la zona, de 4 a 3 unidades (~225 min);
--   3) las cuatro armas de clase de la Zona 5 dejan de pedir el Mítico de recolección: las de mineral (Espada del Abismo, Colmillo del Cráter) piden Gema de Zafiro ×1 en vez de
--      Fragmento de Meteorito, y las de madera (Arco del Alma Errante, Báculo del Árbol de Vida) piden Madera de Nogal ×3 en vez de Corteza del Árbol de Vida (siguen con su Ébano).
--   4) en cambio, el equipo del Fogón Eterno (Zona 0) pide UN Mítico de recolección en cada pieza (el dueño: «la corteza y el meteorito tienen que usarse para algo»): el Trinche del Asador Eterno
--      pide Corteza del Árbol de Vida ×1 y la Brasa del Fogón Eterno pide Fragmento de Meteorito ×1. Salen con 0,5 % por acción (~1000 min cada uno, en paralelo porque /chop y /mine tienen cooldown
--      aparte): conviene juntarlos mientras se hace lo demás y no desmantelarlos.
-- Resultado medido: set de las cinco zonas ~31 h (era ~40 h) y el caso malo (percentil 90) baja de ~48 h a ~35 h. La Zona 1 y las armas generales no se tocan.
-- Corteza del Árbol de Vida y Fragmento de Meteorito siguen cayendo con 0,5 % y están en el Arca del Soberano; valen 500 de Polvo c/u si se desmantelan.
--
-- Hacé un backup antes (DEPLOY.md, sección 6). Después de correrlo, `report_recipe_pacing.sql` tiene que dar ~150 min para las armas de clase de las zonas 2 a 5 y ~225 min para los amuletos.
-- =========================================================

-- 1) y 2) El drop de viaje de cada zona en las armas de clase y en el amuleto.
WITH nuevo(receta, ingrediente, cantidad) AS (VALUES
    ('Hacha de Hierro MK3',        'Ceniza Bendita',          2),
    ('Colmillo Nocturno',          'Ceniza Bendita',          2),
    ('Arco Élfico Ancestral',      'Ceniza Bendita',          2),
    ('Grimorio de las Tormentas',  'Ceniza Bendita',          2),
    ('Talismán de Ceniza Bendita', 'Ceniza Bendita',          3),
    ('Mazo de Escoria',            'Escoria Metálica Densa',  2),
    ('Dagas de Garra Maldita',     'Escoria Metálica Densa',  2),
    ('Boleadoras de Escoria',      'Escoria Metálica Densa',  2),
    ('Báculo de Tizón',            'Escoria Metálica Densa',  2),
    ('Peto de Escoria Templada',   'Escoria Metálica Densa',  3),
    ('Facón de Hueso Añejo',       'Aliento de Fuego Eterno', 2),
    ('Cuchillos de Ceniza',        'Aliento de Fuego Eterno', 2),
    ('Arco de Caza Mayor',         'Aliento de Fuego Eterno', 2),
    ('Códice de las Brasas',       'Aliento de Fuego Eterno', 2),
    ('Talismán del Volcán',        'Aliento de Fuego Eterno', 3),
    ('Espada del Abismo',          'Ceniza del Abismo',       2),
    ('Colmillo del Cráter',        'Ceniza del Abismo',       2),
    ('Arco del Alma Errante',      'Ceniza del Abismo',       2),
    ('Báculo del Árbol de Vida',   'Ceniza del Abismo',       2),
    ('Corazón de Titán Engarzado', 'Ceniza del Abismo',       3)
)
UPDATE recipe_ingredients ri
SET quantity = n.cantidad
FROM nuevo n
JOIN items res ON res.name = n.receta
JOIN items ing ON ing.name = n.ingrediente
JOIN recipes r ON r.result_item_id = res.item_id
WHERE ri.recipe_id = r.recipe_id AND ri.item_id = ing.item_id;

-- 3) Las armas de clase de la Zona 5 sin el Mítico de recolección (y, en el paso 4, el equipo del Fogón con uno en cada pieza).
DELETE FROM recipe_ingredients ri
USING recipes r, items res, items ing
WHERE ri.recipe_id = r.recipe_id AND res.item_id = r.result_item_id AND ing.item_id = ri.item_id
  AND res.name IN ('Espada del Abismo', 'Colmillo del Cráter', 'Arco del Alma Errante', 'Báculo del Árbol de Vida')
  AND ing.name IN ('Fragmento de Meteorito', 'Corteza del Árbol de Vida');

WITH nuevo(receta, ingrediente, cantidad) AS (VALUES
    ('Espada del Abismo',        'Gema de Zafiro',   1),
    ('Colmillo del Cráter',      'Gema de Zafiro',   1),
    ('Arco del Alma Errante',    'Madera de Nogal',  3),
    ('Báculo del Árbol de Vida', 'Madera de Nogal',  3)
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT r.recipe_id, ing.item_id, n.cantidad
FROM nuevo n
JOIN items res ON res.name = n.receta
JOIN items ing ON ing.name = n.ingrediente
JOIN recipes r ON r.result_item_id = res.item_id
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- 4) El equipo del Fogón Eterno: un Mítico de recolección en cada pieza.
WITH nuevo(receta, ingrediente, cantidad) AS (VALUES
    ('Trinche del Asador Eterno', 'Corteza del Árbol de Vida',  1),
    ('Brasa del Fogón Eterno',    'Fragmento de Meteorito',     1)
)
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT r.recipe_id, ing.item_id, n.cantidad
FROM nuevo n
JOIN items res ON res.name = n.receta
JOIN items ing ON ing.name = n.ingrediente
JOIN recipes r ON r.result_item_id = res.item_id
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

-- Se verifica sola: si falta una receta o un ítem, algún UPDATE/INSERT no hizo nada y esto lo grita en lugar de dejarlo a medias.
DO $$
DECLARE
    v_bad TEXT;
BEGIN
    -- (a) las cantidades de viaje quedaron como arriba (20 filas) ...
    SELECT string_agg(res.name || ' / ' || ing.name || ' = ' || ri.quantity, '; ') INTO v_bad
    FROM recipe_ingredients ri
    JOIN recipes r ON r.recipe_id = ri.recipe_id
    JOIN items res ON res.item_id = r.result_item_id
    JOIN items ing ON ing.item_id = ri.item_id
    WHERE ing.name IN ('Ceniza Bendita', 'Escoria Metálica Densa', 'Aliento de Fuego Eterno', 'Ceniza del Abismo')
      AND ri.quantity <> CASE WHEN res.type = 'Amulet' THEN 3 ELSE 2 END
      AND res.name NOT IN ('Cuchilla de Cenizas', 'Pico de Minero Reforzado', 'Lanza de Magma', 'Martillo del Titán');
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rebalance_crafting: cantidades de viaje distintas de las esperadas: [%]', v_bad;
    END IF;

    IF (SELECT count(*) FROM recipe_ingredients ri
        JOIN recipes r ON r.recipe_id = ri.recipe_id JOIN items res ON res.item_id = r.result_item_id JOIN items ing ON ing.item_id = ri.item_id
        WHERE ing.name IN ('Ceniza Bendita', 'Escoria Metálica Densa', 'Aliento de Fuego Eterno', 'Ceniza del Abismo')
          AND res.name NOT IN ('Cuchilla de Cenizas', 'Pico de Minero Reforzado', 'Lanza de Magma', 'Martillo del Titán')) <> 20 THEN
        RAISE EXCEPTION 'rebalance_crafting: tenían que ser 20 filas de drop de viaje (16 armas de clase y 4 amuletos) y no lo son.';
    END IF;

    -- (b) ... el único Mítico de recolección (Madera o Mineral Mítico) que piden las recetas es el uno-por-pieza del Fogón ...
    SELECT string_agg(res.name || ' pide ' || ing.name, '; ') INTO v_bad
    FROM recipe_ingredients ri
    JOIN recipes r ON r.recipe_id = ri.recipe_id JOIN items res ON res.item_id = r.result_item_id JOIN items ing ON ing.item_id = ri.item_id
    WHERE ing.rarity = 'Mítico' AND ing.type IN ('Madera', 'Mineral')
      AND NOT (res.name = 'Trinche del Asador Eterno' AND ing.name = 'Corteza del Árbol de Vida' AND ri.quantity = 1)
      AND NOT (res.name = 'Brasa del Fogón Eterno' AND ing.name = 'Fragmento de Meteorito' AND ri.quantity = 1);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rebalance_crafting: quedan recetas con material Mítico de recolección: [%]', v_bad;
    END IF;

    IF (SELECT count(*) FROM recipe_ingredients ri JOIN items ing ON ing.item_id = ri.item_id WHERE ing.name IN ('Corteza del Árbol de Vida', 'Fragmento de Meteorito')) <> 2 THEN
        RAISE EXCEPTION 'rebalance_crafting: la Corteza y el Meteorito tienen que estar en exactamente 2 recetas (el Trinche y la Brasa del Fogón).';
    END IF;

    -- (c) ... y las cuatro armas de la Zona 5 tienen los reemplazos.
    IF (SELECT count(*) FROM recipe_ingredients ri
        JOIN recipes r ON r.recipe_id = ri.recipe_id JOIN items res ON res.item_id = r.result_item_id JOIN items ing ON ing.item_id = ri.item_id
        WHERE (res.name IN ('Espada del Abismo', 'Colmillo del Cráter') AND ing.name = 'Gema de Zafiro' AND ri.quantity = 1)
           OR (res.name IN ('Arco del Alma Errante', 'Báculo del Árbol de Vida') AND ing.name = 'Madera de Nogal' AND ri.quantity = 3)) <> 4 THEN
        RAISE EXCEPTION 'rebalance_crafting: faltan los reemplazos del Mítico en las armas de la Zona 5.';
    END IF;

END $$;
