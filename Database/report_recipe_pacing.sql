-- =========================================================
-- REPORTE (solo lectura, NO forma parte de la instalación): cuántos minutos de farmeo piden las recetas.
--
--   psql -U postgres -d asado-y-acero -P pager=off -v ph=0.10 -v pt=0.20 -v pb=0.15 -f report_recipe_pacing.sql
--
-- ph / pt / pb son las chances de drop de /hunt, /travel y del jefe: tienen que ser las de
-- GameData/CombatRewardCalculator (HuntDropChancePercent, TravelDropChancePercent, BossDropChancePercent) en
-- fracción. Hay que correrlo CADA VEZ que se toque una de esas chances, un monstruo, un drop o las cantidades de una
-- receta: las cantidades de seed_recipes.sql / seed_zoneN_gear_and_recipes.sql están calibradas contra estos números.
-- Referencia de la run 1 (hunt 10% / travel 20% / jefe 15%): armas de afinidad, amuletos bajos y general de zonas
-- 2-5 ~80-100 min; amuleto alto (con drop de jefe) ~200; Zona 1: armas iniciales ~30, Hoja +15 ~60, Levantador ~30,
-- Hombreras ~200. Subir a nivel dentro de una zona cuesta ~95-110 min de juego seguido, y lo que se busca es que el
-- equipo de la zona cueste más o menos lo mismo que subir de nivel en ella.
-- =========================================================
-- Ritmo de farmeo de los materiales que DROPEAN (no los de recolección): minutos esperados para juntar los ingredientes
-- de cada receta, suponiendo 1 /hunt por minuto, 1 /travel cada 10 y 1 /boss cada 30. Los ingredientes se juntan EN
-- PARALELO (cada pelea puede soltar cualquiera), así que el tiempo de una receta es el del ingrediente más lento.
-- Parámetros (psql -v): ph = chance de drop de hunt, pt = travel, pb = jefe.
WITH hunt_n AS (
    SELECT zone_id, COUNT(*) AS n FROM monsters WHERE NOT is_boss AND NOT is_travel GROUP BY zone_id
), src AS (
    SELECT m.zone_id, d.item_id,
           CASE WHEN m.is_boss THEN 'boss' WHEN m.is_travel THEN 'travel' ELSE 'hunt' END AS kind,
           (SELECT COUNT(*) FROM monster_drops x WHERE x.monster_id = m.monster_id) AS k
    FROM monsters m JOIN monster_drops d USING (monster_id)
), rate AS (
    SELECT s.item_id,
           SUM(CASE s.kind
                 WHEN 'hunt'   THEN (1.0 / h.n) * :ph / s.k
                 WHEN 'travel' THEN :pt / s.k / 10.0
                 WHEN 'boss'   THEN :pb / s.k / 30.0 END) AS per_min
    FROM src s LEFT JOIN hunt_n h USING (zone_id) GROUP BY s.item_id
), need AS (
    SELECT r.recipe_id, z.min_level AS nv,
           CASE WHEN r.affinity THEN 'AFIN' WHEN i.type = 'Amulet' THEN 'AMUL' ELSE 'GRAL' END AS tipo,
           i.name AS resultado,
           MAX(ri.quantity / ra.per_min) AS minutos,
           string_agg(ri.quantity || ' ' || ing.name, ', ' ORDER BY ri.quantity / ra.per_min DESC) FILTER (WHERE ra.per_min IS NOT NULL) AS drops
    FROM recipes r
    JOIN items i ON i.item_id = r.result_item_id
    JOIN zones z ON z.zone_id = r.zone_id
    JOIN recipe_ingredients ri ON ri.recipe_id = r.recipe_id
    JOIN items ing ON ing.item_id = ri.item_id
    LEFT JOIN rate ra ON ra.item_id = ri.item_id
    GROUP BY r.recipe_id, z.min_level, r.affinity, i.type, i.name
)
SELECT nv, tipo, resultado, ROUND(minutos::numeric) AS minutos, drops FROM need ORDER BY nv, tipo, resultado;
