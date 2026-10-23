-- =========================================================
-- Informe de SOLO LECTURA (puros SELECT, no cambia nada): de dónde salen los Cofres de Oro. En el informe de salud del 2026-10-20 se abrieron 29 en 7 días con 6 jugadores,
-- más de lo previsto, y no se sabía de dónde. Los cofres no dejan rastro de su origen (el inventario solo guarda cantidades), así que esto los reconstruye por las fuentes que SÍ
-- dejan eventos y compara el total con los que se abrieron + los que quedan guardados.
--
--   [Console]::OutputEncoding = [Text.Encoding]::UTF8     # en PowerShell, antes (si no, las tildes salen rotas)
--   psql "<la URL pública de la base>" -f report_gold_chest_origin.sql
--
-- Qué fuentes mira (las reglas de qué zona paga qué caja salen de la tabla zone_boxes y del botín del jefe, NO están escritas acá: si se retoca una zona, el informe sigue bien):
--   · tienda (shop_gold_spent)                           exacto
--   · el cofre del jefe, primera victoria de cada vuelta  casi exacto (boss_win / raid_win en la zona cuyo jefe suelta Cofre de Oro, una vez por jugador y vuelta del Fuego Nuevo)
--   · el /daily del hito de cada 7 días (rol "weekly")    estimado (1 de cada 7 reclamos en una zona donde ese rol paga Cofre de Oro; no se guarda el día de la racha)
--   · el bonus de las misiones (rol "daily" o "weekly")   exacto (mission_claimed con detalle _bonus:<período>)
--   · logros tramo II (rol "prize") y III (rol "prize_top") TOPE (los logros "Plain" no dan caja y acá cuentan igual)
--   · el campeón de la Arena (rol "prize")                exacto
--   · dentro de otras cajas que se abrieron               ESPERADO (el peso del Cofre de Oro en su botín × lo que sale por apertura)
-- Los eventos se registran desde la v0.6: lo anterior no está. La zona de cada evento es la que tenía el jugador en ese momento.
-- =========================================================
\pset null '-'

\echo ''
\echo '== 1. Cofres de Oro vistos: abiertos + guardados hoy =='
SELECT
    (SELECT COALESCE(SUM(amount), 0) FROM game_events WHERE kind = 'box_opened' AND detail = 'Cofre de Oro')                                   AS abiertos,
    (SELECT COALESCE(SUM(inv.quantity), 0) FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE i.name = 'Cofre de Oro')           AS guardados_hoy,
    (SELECT COALESCE(SUM(amount), 0) FROM game_events WHERE kind = 'box_opened' AND detail = 'Cofre de Oro')
      + (SELECT COALESCE(SUM(inv.quantity), 0) FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE i.name = 'Cofre de Oro')       AS total_visto,
    (SELECT MIN(occurred_at)::date FROM game_events)                                                                                           AS eventos_desde;

\echo ''
\echo '== 2. Dónde pagan Cofre de Oro hoy (zone_boxes y botín del jefe) =='
SELECT 'zone_boxes' AS origen, z.zone_id, z.name AS zona, zb.role AS rol, zb.chance_percent AS chance_pct
FROM zone_boxes zb
JOIN items i ON i.item_id = zb.box_item_id AND i.name = 'Cofre de Oro'
JOIN zones z ON z.zone_id = zb.zone_id
UNION ALL
SELECT 'cofre del jefe', z.zone_id, z.name, 'boss_chest', 100
FROM monsters m
JOIN monster_drops md ON md.monster_id = m.monster_id
JOIN items i ON i.item_id = md.item_id AND i.name = 'Cofre de Oro'
JOIN zones z ON z.zone_id = m.zone_id
WHERE m.is_boss
ORDER BY 1, 2, 4;

\echo ''
\echo '== 3. Cuántos salieron de cada fuente =='
WITH oro AS (SELECT item_id FROM items WHERE name = 'Cofre de Oro'),
roles AS (SELECT zb.zone_id, zb.role FROM zone_boxes zb JOIN oro ON oro.item_id = zb.box_item_id),
jefe_oro AS (SELECT DISTINCT m.zone_id FROM monsters m JOIN monster_drops md ON md.monster_id = m.monster_id JOIN oro ON oro.item_id = md.item_id WHERE m.is_boss),
fuentes AS (
    SELECT 1 AS orden, 'Comprados en la tienda' AS fuente, 'exacto' AS como, COUNT(*)::numeric AS cantidad
    FROM game_events WHERE kind = 'shop_gold_spent' AND detail = 'Cofre de Oro'
    UNION ALL
    SELECT 2, 'Cofre del jefe de la zona (primera victoria de cada vuelta)', 'casi exacto', COUNT(*)::numeric
    FROM (
        SELECT DISTINCT e.discord_id,
               (SELECT COUNT(*) FROM game_events f WHERE f.discord_id = e.discord_id AND f.kind = 'fuego_nuevo' AND f.occurred_at <= e.occurred_at) AS vuelta
        FROM game_events e
        WHERE e.kind IN ('boss_win', 'raid_win') AND e.zone_id IN (SELECT zone_id FROM jefe_oro)
    ) v
    UNION ALL
    SELECT 3, 'Hito de cada 7 días del /daily', 'estimado', ROUND(COUNT(*) / 7.0, 1)
    FROM game_events e JOIN roles r ON r.zone_id = e.zone_id AND r.role = 'weekly'
    WHERE e.kind = 'daily_claim'
    UNION ALL
    SELECT 4, 'Bonus de misiones (todas las del día o de la semana)', 'exacto', COUNT(*)::numeric
    FROM game_events e
    JOIN roles r ON r.zone_id = e.zone_id AND r.role = CASE WHEN split_part(e.detail, ':', 2) = 'weekly' THEN 'weekly' ELSE 'daily' END
    WHERE e.kind = 'mission_claimed' AND split_part(e.detail, ':', 1) = '_bonus'
    UNION ALL
    SELECT 5, 'Logros tramo II y III', 'tope', COUNT(*)::numeric
    FROM game_events e
    JOIN roles r ON r.zone_id = e.zone_id
    WHERE e.kind = 'achievement_unlocked' AND ((r.role = 'prize' AND e.detail LIKE '%:2') OR (r.role = 'prize_top' AND e.detail LIKE '%:3'))
    UNION ALL
    SELECT 6, 'Campeón de la Arena', 'exacto', COUNT(*)::numeric
    FROM game_events e JOIN roles r ON r.zone_id = e.zone_id AND r.role = 'prize'
    WHERE e.kind = 'arena_win'
    UNION ALL
    -- Otras cajas cuyo botín incluye un Cofre de Oro: cada apertura hace entre min_items y max_items tiradas y cada tirada elige UNA entrada por peso (los pesos de los ítems suman 1000).
    SELECT 7, 'Dentro de ' || ib.name || ' (' || opened.n || ' abiertos, peso ' || bl.weight || '/1000)', 'esperado',
           ROUND(opened.n * ((b.min_items + b.max_items) / 2.0) * bl.weight / 1000.0 * ((bl.min_qty + bl.max_qty) / 2.0), 1)
    FROM box_loot bl
    JOIN oro ON oro.item_id = bl.item_id
    JOIN boxes b ON b.box_item_id = bl.box_item_id
    JOIN items ib ON ib.item_id = b.box_item_id
    CROSS JOIN LATERAL (SELECT COALESCE(SUM(amount), 0) AS n FROM game_events WHERE kind = 'box_opened' AND detail = ib.name) opened
    WHERE bl.kind = 'item'
)
SELECT fuente, como, cantidad FROM fuentes ORDER BY orden, fuente;

\echo ''
\echo '== 4. Cuadre: lo que se vio contra lo que explican las fuentes =='
WITH oro AS (SELECT item_id FROM items WHERE name = 'Cofre de Oro'),
roles AS (SELECT zb.zone_id, zb.role FROM zone_boxes zb JOIN oro ON oro.item_id = zb.box_item_id),
jefe_oro AS (SELECT DISTINCT m.zone_id FROM monsters m JOIN monster_drops md ON md.monster_id = m.monster_id JOIN oro ON oro.item_id = md.item_id WHERE m.is_boss),
explicado AS (
    SELECT COUNT(*)::numeric AS n FROM game_events WHERE kind = 'shop_gold_spent' AND detail = 'Cofre de Oro'
    UNION ALL
    SELECT COUNT(*)::numeric FROM (
        SELECT DISTINCT e.discord_id,
               (SELECT COUNT(*) FROM game_events f WHERE f.discord_id = e.discord_id AND f.kind = 'fuego_nuevo' AND f.occurred_at <= e.occurred_at) AS vuelta
        FROM game_events e WHERE e.kind IN ('boss_win', 'raid_win') AND e.zone_id IN (SELECT zone_id FROM jefe_oro)) v
    UNION ALL
    SELECT ROUND(COUNT(*) / 7.0, 1) FROM game_events e JOIN roles r ON r.zone_id = e.zone_id AND r.role = 'weekly' WHERE e.kind = 'daily_claim'
    UNION ALL
    SELECT COUNT(*)::numeric FROM game_events e
    JOIN roles r ON r.zone_id = e.zone_id AND r.role = CASE WHEN split_part(e.detail, ':', 2) = 'weekly' THEN 'weekly' ELSE 'daily' END
    WHERE e.kind = 'mission_claimed' AND split_part(e.detail, ':', 1) = '_bonus'
    UNION ALL
    SELECT COUNT(*)::numeric FROM game_events e JOIN roles r ON r.zone_id = e.zone_id
    WHERE e.kind = 'achievement_unlocked' AND ((r.role = 'prize' AND e.detail LIKE '%:2') OR (r.role = 'prize_top' AND e.detail LIKE '%:3'))
    UNION ALL
    SELECT COUNT(*)::numeric FROM game_events e JOIN roles r ON r.zone_id = e.zone_id AND r.role = 'prize' WHERE e.kind = 'arena_win'
)
SELECT
    (SELECT COALESCE(SUM(amount), 0) FROM game_events WHERE kind = 'box_opened' AND detail = 'Cofre de Oro')
      + (SELECT COALESCE(SUM(inv.quantity), 0) FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE i.name = 'Cofre de Oro') AS visto,
    (SELECT SUM(n) FROM explicado)                                                                                                         AS explicado_sin_cajas,
    (SELECT COALESCE(SUM(amount), 0) FROM game_events WHERE kind = 'box_opened' AND detail = 'Cofre de Oro')
      + (SELECT COALESCE(SUM(inv.quantity), 0) FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE i.name = 'Cofre de Oro')
      - (SELECT SUM(n) FROM explicado)                                                                                                     AS sin_explicar;

\echo ''
\echo '== 5. Por jugador: cuántos abrió, cuántos le quedan y cuántos compró =='
SELECT
    u.discord_id,
    COALESCE(SUM(e.amount) FILTER (WHERE e.kind = 'box_opened' AND e.detail = 'Cofre de Oro'), 0)               AS abiertos,
    COALESCE((SELECT SUM(inv.quantity) FROM inventory inv JOIN items i ON i.item_id = inv.item_id WHERE inv.discord_id = u.discord_id AND i.name = 'Cofre de Oro'), 0) AS guardados,
    COUNT(*) FILTER (WHERE e.kind = 'shop_gold_spent' AND e.detail = 'Cofre de Oro')                            AS comprados,
    u.fuego_nuevo
FROM users u
LEFT JOIN game_events e ON e.discord_id = u.discord_id AND e.kind IN ('box_opened', 'shop_gold_spent')
GROUP BY u.discord_id, u.fuego_nuevo
ORDER BY abiertos DESC, u.discord_id
LIMIT 20;
