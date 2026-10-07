-- =========================================================
-- REPORTE (solo lectura, NO forma parte de la instalación): cuántas cajas GRATIS regala cada zona contra lo que rinde jugar (v0.14.0, "cofres parejos").
--
--   psql -U postgres -d asado-y-acero -P pager=off -f report_economy.sql
--
-- La pregunta que contesta: ¿lo que se regala (misiones, logros, jefe repetido, Arena) pesa más o menos que una sesión de juego? Las cajas gratis salen de zone_boxes
-- (seed_zone_boxes.sql); su valor en oro es el de report_box_economy.sql (columnas oro_por_farmeo + oro_comida_y_cajas + bono_oro_esperado), que hay que volver a
-- copiar acá si cambian los pesos, los rangos, las chances de drop o los precios. Medido en la v0.14.0 (2026-10-07): Pino ~0,75k, Roble ~8,2k, Arcón ~43k,
-- Cofre de Oro ~211k. El Arca del Soberano, el Cofre de Escoria y el Brasero no se regalan por esta tabla.
--
-- Una sesión de 2 h de juego perfecto = 120 cacerías + 4 viajes (cada viaje paga TravelRewardMultiplier = 30 cacerías), medida en oro de cacería de la zona
-- (MissionRewards.GoldPerHunt: 14/58/118/215/375): Z1 3,4k, Z2 13,9k, Z3 28,3k, Z4 51,6k, Z5 90k. Es el metro con el que se comparan las cajas.
--
-- Cómo leerlo: "semana_gratis" es lo que suma, en una semana, completar TODAS las misiones del día (x7) y de la semana (x1) de esa zona (sin contar logros ni
-- jefe). Si pasa de unas pocas sesiones de juego, la zona regala demasiado: el dueño quería "más parejo", no que las cajas reemplacen al farmeo.
-- =========================================================
WITH
valor_caja(nombre, oro) AS (VALUES
    ('Cajón de Pino', 750), ('Baúl de Roble', 8200), ('Arcón de Hierro', 43000), ('Cofre de Oro', 211000)),
oro_zona(rango, oro_cacheria) AS (VALUES (1, 14), (2, 58), (3, 118), (4, 215), (5, 375)),
zonas AS (
    SELECT z.zone_id, z.name, ROW_NUMBER() OVER (ORDER BY z.min_level) AS rango
    FROM zones z WHERE z.kind = 'normal'
),
filas AS (
    SELECT z.rango, z.name AS zona, zb.role, i.name AS caja, zb.chance_percent, COALESCE(v.oro, 0) AS oro_caja
    FROM zone_boxes zb
    JOIN zonas z ON z.zone_id = zb.zone_id
    JOIN items i ON i.item_id = zb.box_item_id
    LEFT JOIN valor_caja v ON v.nombre = i.name
)
-- 1) la tabla tal cual está en la base
SELECT f.rango, f.zona,
       MAX(f.caja) FILTER (WHERE f.role = 'repeat')    || ' (' || MAX(f.chance_percent) FILTER (WHERE f.role = 'repeat') || '%)' AS jefe_repetido,
       MAX(f.caja) FILTER (WHERE f.role = 'daily')     AS misiones_dia,
       MAX(f.caja) FILTER (WHERE f.role = 'weekly')    AS misiones_semana,
       MAX(f.caja) FILTER (WHERE f.role = 'prize')     AS logro_II_y_arena,
       MAX(f.caja) FILTER (WHERE f.role = 'prize_top') AS logro_III
FROM filas f
GROUP BY f.rango, f.zona
ORDER BY f.rango;

-- 2) cuánto pesa lo gratis contra una sesión de juego
WITH
valor_caja(nombre, oro) AS (VALUES
    ('Cajón de Pino', 750), ('Baúl de Roble', 8200), ('Arcón de Hierro', 43000), ('Cofre de Oro', 211000)),
oro_zona(rango, oro_cacheria) AS (VALUES (1, 14), (2, 58), (3, 118), (4, 215), (5, 375)),
zonas AS (
    SELECT z.zone_id, z.name, ROW_NUMBER() OVER (ORDER BY z.min_level) AS rango
    FROM zones z WHERE z.kind = 'normal'
),
filas AS (
    SELECT z.rango, z.name AS zona, zb.role, zb.chance_percent, COALESCE(v.oro, 0) AS oro_caja
    FROM zone_boxes zb
    JOIN zonas z ON z.zone_id = zb.zone_id
    JOIN items i ON i.item_id = zb.box_item_id
    LEFT JOIN valor_caja v ON v.nombre = i.name
)
SELECT f.rango, f.zona,
       (g.oro_cacheria * (120 + 4 * 30)) AS sesion_2h_oro,
       SUM(f.oro_caja) FILTER (WHERE f.role = 'daily') AS caja_dia_oro,
       SUM(f.oro_caja) FILTER (WHERE f.role = 'weekly') AS caja_semana_oro,
       (7 * SUM(f.oro_caja) FILTER (WHERE f.role = 'daily') + SUM(f.oro_caja) FILTER (WHERE f.role = 'weekly')) AS semana_gratis_oro,
       ROUND(((7 * SUM(f.oro_caja) FILTER (WHERE f.role = 'daily') + SUM(f.oro_caja) FILTER (WHERE f.role = 'weekly'))
              / (g.oro_cacheria * (120 + 4 * 30))::numeric), 1) AS semana_gratis_en_sesiones,
       -- el jefe repetido: una pelea cada 5 h de cooldown = ~33 por semana si se juega siempre (techo teórico), por su chance.
       ROUND((33 * SUM(f.oro_caja * f.chance_percent / 100.0) FILTER (WHERE f.role = 'repeat'))::numeric) AS jefe_repetido_semana_oro_techo
FROM filas f
JOIN oro_zona g ON g.rango = f.rango
GROUP BY f.rango, f.zona, g.oro_cacheria
ORDER BY f.rango;
