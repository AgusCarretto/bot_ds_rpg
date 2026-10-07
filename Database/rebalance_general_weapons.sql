-- =========================================================
-- v0.14.2: el arma GENERAL de las zonas 2 a 5 se abarata. Migración para una base que YA existe (una base nueva ya trae estas cantidades en rework_drops_and_recipes.sql: NO hace
-- falta correr esto contra una instalación limpia). Re-ejecutable (deja siempre las mismas cantidades). No cambia el código: alcanza con correrlo, aunque el bot esté andando.
--
--   psql -d asado-y-acero -v ON_ERROR_STOP=1 -f rebalance_general_weapons.sql
--
-- Por qué: el dueño (2026-10-07) vio que «nunca nadie haría la general». El arma de la clase rinde ×1,5 y desde que el drop de /travel bajó al 40 % (v0.10.2) cuesta ~225 minutos de
-- juego perfecto contra ~100 de la general (medido con report_recipe_pacing.sql). Elegí ABARATAR la general y no encarecer la de clase: la de clase ya era 1,5 veces más lenta que su
-- objetivo de diseño (150 min) y subirla alargaría las ~52 h del camino de las 5 zonas; abaratar la general no toca ese camino y no perjudica a nadie que ya esté juntando materiales.
-- Queda como el puente barato para entrar a cada zona (~67 min en lugar de ~100: 2 de cada drop de cacería y 2 de lo recolectado, en vez de 3 y 4), con la de clase como la meta.
-- La zona 1 no se toca (su general ya era la más barata, ~48 min).
--
-- Hacé un backup antes (DEPLOY.md, sección 6). Después de correrlo, `report_recipe_pacing.sql` tiene que dar ~67 min para las cuatro generales de las zonas 2 a 5.
-- =========================================================

WITH nuevo(receta, ingrediente, cantidad) AS (VALUES
    ('Cuchilla de Cenizas',      'Carbón',               2),
    ('Cuchilla de Cenizas',      'Garra de Puma Cenizo', 2),
    ('Cuchilla de Cenizas',      'Esencia Espectral',    2),
    ('Pico de Minero Reforzado', 'Hierro',               2),
    ('Pico de Minero Reforzado', 'Yunque Fragmentado',   2),
    ('Pico de Minero Reforzado', 'Gema en Bruto',        2),
    ('Lanza de Magma',           'Hierro',               2),
    ('Lanza de Magma',           'Escama Ígnea',         2),
    ('Lanza de Magma',           'Núcleo de Magma',      2),
    ('Martillo del Titán',       'Hierro',               2),
    ('Martillo del Titán',       'Corazón de Titán',     2),
    ('Martillo del Titán',       'Fragmento de Alma',    2)
)
UPDATE recipe_ingredients ri
SET quantity = n.cantidad
FROM nuevo n
JOIN items res ON res.name = n.receta
JOIN items ing ON ing.name = n.ingrediente
JOIN recipes r ON r.result_item_id = res.item_id
WHERE ri.recipe_id = r.recipe_id AND ri.item_id = ing.item_id;

-- Se verifica sola: tienen que ser exactamente las cuatro recetas con sus tres ingredientes en 2 (si falta una fila, la receta o el ítem no existe y no se cambió nada en silencio).
DO $$
DECLARE
    v_bad TEXT;
BEGIN
    SELECT string_agg(res.name, ', ') INTO v_bad
    FROM items res
    JOIN recipes r ON r.result_item_id = res.item_id
    WHERE res.name IN ('Cuchilla de Cenizas', 'Pico de Minero Reforzado', 'Lanza de Magma', 'Martillo del Titán')
      AND (
            (SELECT count(*) FROM recipe_ingredients ri WHERE ri.recipe_id = r.recipe_id) <> 3
            OR EXISTS (SELECT 1 FROM recipe_ingredients ri WHERE ri.recipe_id = r.recipe_id AND ri.quantity <> 2)
          );

    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rebalance_general_weapons: estas armas generales no quedaron con 3 ingredientes en 2: [%]', v_bad;
    END IF;

    IF (SELECT count(*) FROM items WHERE name IN ('Cuchilla de Cenizas', 'Pico de Minero Reforzado', 'Lanza de Magma', 'Martillo del Titán')) <> 4 THEN
        RAISE EXCEPTION 'rebalance_general_weapons: faltan armas generales en el catálogo (se esperaban 4)';
    END IF;

    RAISE NOTICE 'rebalance_general_weapons: listo (las 4 armas generales de las zonas 2 a 5 piden 2 de cada ingrediente).';
END $$;
