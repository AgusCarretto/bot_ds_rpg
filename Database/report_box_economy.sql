-- =========================================================
-- REPORTE (solo lectura, NO forma parte de la instalación): cuánto "vale" lo que da cada caja contra lo que cuesta (cajas v2, v0.8.0).
--
--   psql -U postgres -d asado-y-acero -P pager=off -f report_box_economy.sql
--
-- El valor de una caja se mide en MINUTOS DE FARMEO: cuánto tardarías en conseguir, jugando, lo mismo que te da (cada ítem tiene su ritmo):
--   gather     un material de /chop o /mine: rareza sorteada con las chances de la caja (RarityCatalog + Mítico 0,1% por ítem),
--              y su ritmo real = chance de la rareza x unidades por acción (GatheringYield) / ítems de esa rareza / 5 min de cooldown.
--   zone_drop  un drop de monstruo de las zonas <= la de la caja: /hunt 6% por pelea (1 pelea por minuto, repartida entre los monstruos
--              de la zona) y /travel 40% por viaje (60% hasta la v0.10.1; 1 viaje cada 30 min); en la caja pesan 3 (cada hunt) y 2 (el travel).
--   item       comida y cajas de menor escalón valen lo que cuestan en la taberna; los trofeos (nadie los dropea) no se valoran (0).
-- Los minutos se pasan a oro con lo que paga una hora de /hunt en la zona de la caja (MissionRewards.GoldPerHunt: 14/58/118/215/375).
-- "precio_pct": el precio sobre ese valor total (farmeo + comida/cajas + bono de oro). El diseño apunta a ~70% (la eficiencia de un jugador
-- real al farmear): comprar nunca es más rápido que jugar. Medido en v0.8.0: 76 / 79 / 68 / 60% (las de arriba bajan porque su botín
-- incluye la caja del escalón de abajo, que se valora por lo que cuesta). La caja Mítica (Arca del Soberano) es solo premio: no tiene precio.
-- Si cambiás una chance de drop, el cooldown de /travel, GatheringYield o GoldPerHunt, cambiá los números de abajo (lo avisan los
-- comentarios) y volvé a mirar los precios.
-- =========================================================
WITH
gold_por_zona(rango, oro) AS (VALUES (1, 14), (2, 58), (3, 118), (4, 215), (5, 375)),
-- w_farm: chance por mil en /chop y /mine; w_caja: la de las cajas (solo cambia el Mítico); rinde: unidades promedio por acción.
rareza(rarity, w_farm, w_caja, rinde) AS (VALUES
    ('Común', 680, 680, 3.0), ('Raro', 210, 210, 2.0), ('Épico', 70, 70, 1.5), ('Legendario', 35, 35, 1.0), ('Mítico', 5, 2, 1.0)),
mats AS (
    SELECT r.w_farm, r.w_caja, r.rinde,
           (SELECT COUNT(DISTINCT x.type) FROM items x WHERE x.rarity = i.rarity AND x.type IN ('Madera', 'Mineral')) AS tipos
    FROM items i JOIN rareza r ON r.rarity = i.rarity
    WHERE i.type IN ('Madera', 'Mineral')
),
-- minutos de farmeo que vale UNA tirada de recolección (es la misma para todas las cajas).
gather AS (
    SELECT SUM((m.w_caja::numeric / (SELECT SUM(w_caja) FROM rareza WHERE rarity IN (SELECT rarity FROM items WHERE type IN ('Madera', 'Mineral'))) / m.tipos)
               * 5000.0 / (m.w_farm * m.rinde)) AS minutos
    FROM mats m
),
zonas AS (SELECT zone_id, ROW_NUMBER() OVER (ORDER BY min_level) AS rango FROM zones WHERE zone_id > 0),
-- cada drop de monstruo (sin jefes) con su peso en la caja y los minutos de farmeo de ESE ítem.
drops AS (
    SELECT z.rango,
           CASE WHEN m.is_travel THEN 2 ELSE 3 END AS peso,
           CASE WHEN m.is_travel THEN 1.0 / (0.40 / 30.0)
                ELSE (SELECT COUNT(*) FROM monsters h WHERE h.zone_id = m.zone_id AND NOT h.is_boss AND NOT h.is_travel) / 0.06 END AS minutos
    FROM monster_drops d
    JOIN monsters m ON m.monster_id = d.monster_id AND NOT m.is_boss
    JOIN zonas z ON z.zone_id = m.zone_id
),
caja AS (
    SELECT b.item_id, b.name, b.buy_price, bx.min_items, bx.max_items,
           CASE b.rarity WHEN 'Común' THEN 1 WHEN 'Raro' THEN 2 WHEN 'Épico' THEN 3 WHEN 'Legendario' THEN 4 ELSE 5 END AS rango
    FROM boxes bx JOIN items b ON b.item_id = bx.box_item_id
),
loot AS (
    SELECT c.item_id,
           SUM(l.weight) FILTER (WHERE l.kind <> 'gold') AS peso_total,
           SUM(l.weight) FILTER (WHERE l.kind = 'gather') AS peso_gather,
           SUM(l.weight) FILTER (WHERE l.kind = 'zone_drop') AS peso_zona,
           SUM(l.weight * (l.min_qty + l.max_qty) / 2.0 * CASE WHEN it.type IN ('Consumable', 'Caja') THEN it.buy_price ELSE 0 END)
               FILTER (WHERE l.kind = 'item') AS oro_items,
           SUM(l.weight / 1000.0 * (l.min_qty + l.max_qty) / 2.0) FILTER (WHERE l.kind = 'gold') AS bono_oro
    FROM caja c
    JOIN box_loot l ON l.box_item_id = c.item_id
    LEFT JOIN items it ON it.item_id = l.item_id
    GROUP BY c.item_id
),
valor AS (
    SELECT c.*, (c.min_items + c.max_items) / 2.0 AS tiradas, l.peso_total, COALESCE(l.peso_gather, 0) AS peso_gather,
           COALESCE(l.peso_zona, 0) AS peso_zona, COALESCE(l.oro_items, 0) AS oro_items, COALESCE(l.bono_oro, 0) AS bono_oro,
           (SELECT SUM(d.peso * d.minutos)::numeric / SUM(d.peso) FROM drops d WHERE d.rango <= c.rango) AS min_zona,
           (SELECT oro FROM gold_por_zona g WHERE g.rango = c.rango) AS oro_min
    FROM caja c JOIN loot l ON l.item_id = c.item_id
)
SELECT v.name AS caja,
       'Zona ' || v.rango AS zona,
       v.min_items || '-' || v.max_items AS items,
       v.buy_price AS precio,
       ROUND((v.tiradas * (v.peso_gather * g.minutos + v.peso_zona * COALESCE(v.min_zona, 0)) / v.peso_total)::numeric) AS minutos_de_farmeo,
       ROUND((v.tiradas * (v.peso_gather * g.minutos + v.peso_zona * COALESCE(v.min_zona, 0)) / v.peso_total * v.oro_min)::numeric) AS oro_por_farmeo,
       ROUND((v.tiradas * v.oro_items / v.peso_total)::numeric) AS oro_comida_y_cajas,
       ROUND(v.bono_oro::numeric) AS bono_oro_esperado,
       CASE WHEN v.buy_price > 0 THEN ROUND(100.0 * v.buy_price / NULLIF(
           v.tiradas * (v.peso_gather * g.minutos + v.peso_zona * COALESCE(v.min_zona, 0)) / v.peso_total * v.oro_min
           + v.tiradas * v.oro_items / v.peso_total + v.bono_oro, 0)::numeric, 0) END AS precio_pct
FROM valor v CROSS JOIN gather g
ORDER BY v.rango;
