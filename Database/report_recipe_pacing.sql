-- =========================================================
-- REPORTE (solo lectura, NO forma parte de la instalación): cuántos minutos de farmeo piden las recetas.
--
--   psql -U postgres -d asado-y-acero -P pager=off -v ph=0.10 -v pt=0.20 -v pb=0.15 -f report_recipe_pacing.sql
--
-- ph / pt / pb son las chances de drop de /hunt, /travel y del jefe: tienen que ser las de
-- GameData/CombatRewardCalculator (HuntDropChancePercent, TravelDropChancePercent, BossDropChancePercent) en
-- fracción. Hay que correrlo CADA VEZ que se toque una de esas chances, un monstruo, un drop, el rendimiento de
-- /chop y /mine (GameData/GatheringYield) o las cantidades de una receta: las cantidades de seed_recipes.sql /
-- seed_zoneN_gear_and_recipes.sql están calibradas contra estos números.
--
-- Columnas: min_drops = tiempo de juntar los materiales que DROPEAN (1 cacería por minuto, 1 viaje cada 10, 1 jefe cada
-- 30; se juntan en paralelo, así que cuenta el más lento). min_recol_antes / min_recol_ahora = lo mismo para los de
-- RECOLECCIÓN con 1 unidad por acción vs con el rendimiento de GatheringYield (Común 1-5, Raro/Épico 1-3,
-- Legendario/Mítico 1), con /chop y /mine cada 5 min y en paralelo entre sí. "antes" solo sirve para comprobar que
-- subir las cantidades de las recetas dejó el ritmo parejo (con las cantidades VIEJAS de las recetas, "ahora" sería
-- 2-3 veces más rápido). min_total = el más lento de los tres.
-- Referencia de la run 1 (hunt 10% / travel 20% / jefe 15%): armas de afinidad, amuletos bajos y general de zonas 2-5
-- ~80-100 min de drops; amuleto alto (con drop de jefe) ~200.
-- =========================================================
WITH hunt_n AS (
    SELECT zone_id, COUNT(*) AS n FROM monsters WHERE NOT is_boss AND NOT is_travel GROUP BY zone_id
), src AS (
    SELECT m.zone_id, d.item_id,
           CASE WHEN m.is_boss THEN 'boss' WHEN m.is_travel THEN 'travel' ELSE 'hunt' END AS kind,
           (SELECT COUNT(*) FROM monster_drops x WHERE x.monster_id = m.monster_id) AS k
    FROM monsters m JOIN monster_drops d USING (monster_id)
), drop_rate AS (
    SELECT s.item_id,
           SUM(CASE s.kind
                 WHEN 'hunt'   THEN (1.0 / h.n) * :ph / s.k
                 WHEN 'travel' THEN :pt / s.k / 10.0
                 WHEN 'boss'   THEN :pb / s.k / 30.0 END) AS per_min
    FROM src s LEFT JOIN hunt_n h USING (zone_id) GROUP BY s.item_id
), weights (rarity, weight, avg_yield) AS (
    -- Mantener sincronizado con RarityCatalog.GatheringWeights y GameData/GatheringYield.
    VALUES ('Común', 0.600, 3.0), ('Raro', 0.250, 2.0), ('Épico', 0.100, 2.0), ('Legendario', 0.045, 1.0), ('Mítico', 0.005, 1.0)
), gather_rate AS (
    -- Unidades por minuto de UN ítem de recolección (un /chop o /mine cada 5 min; el ítem sale con la probabilidad de
    -- su rareza repartida entre los ítems de esa rareza y ese tipo).
    SELECT i.item_id,
           (w.weight / COUNT(*) OVER (PARTITION BY i.type, i.rarity)) * w.avg_yield / 5.0 AS per_min_now,
           (w.weight / COUNT(*) OVER (PARTITION BY i.type, i.rarity)) * 1.0 / 5.0 AS per_min_before
    FROM items i JOIN weights w ON w.rarity = i.rarity
    WHERE i.type IN ('Madera', 'Mineral')
), need AS (
    SELECT r.recipe_id, z.min_level AS nv,
           CASE WHEN r.affinity THEN 'AFIN' WHEN i.type = 'Amulet' THEN 'AMUL' ELSE 'GRAL' END AS tipo,
           i.name AS resultado,
           MAX(ri.quantity / dr.per_min) AS min_drops,
           MAX(ri.quantity / gr.per_min_before) AS min_recol_antes_viejas_cantidades,
           MAX(ri.quantity / gr.per_min_now) AS min_recol_ahora
    FROM recipes r
    JOIN items i ON i.item_id = r.result_item_id
    JOIN zones z ON z.zone_id = r.zone_id
    JOIN recipe_ingredients ri ON ri.recipe_id = r.recipe_id
    LEFT JOIN drop_rate dr ON dr.item_id = ri.item_id
    LEFT JOIN gather_rate gr ON gr.item_id = ri.item_id
    GROUP BY r.recipe_id, z.min_level, r.affinity, i.type, i.name
)
SELECT nv, tipo, resultado,
       ROUND(min_drops::numeric) AS min_drops,
       ROUND(min_recol_ahora::numeric) AS min_recol_ahora,
       ROUND(GREATEST(COALESCE(min_drops, 0), COALESCE(min_recol_ahora, 0))::numeric) AS min_total
FROM need ORDER BY nv, tipo, resultado;
