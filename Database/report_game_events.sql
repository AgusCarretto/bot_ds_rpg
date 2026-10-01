-- =========================================================
-- REPORTE (solo lectura, NO forma parte de la instalación): cómo se está jugando, según game_events / player_stats.
--
--   psql -U postgres -d asado-y-acero -P pager=off -f report_game_events.sql
--
-- Sirve para decidir con datos (cuántos juegan, qué usan, dónde se traban) en vez de a ojo. Los números de los primeros
-- días son de pocos jugadores: leerlos como pistas, no como verdades.
-- =========================================================

\echo '== Jugadores activos por día (últimos 14 días) =='
SELECT (occurred_at AT TIME ZONE 'America/Montevideo')::date AS dia, COUNT(DISTINCT discord_id) AS jugadores, COUNT(*) AS eventos
FROM game_events WHERE occurred_at > now() - interval '14 days'
GROUP BY 1 ORDER BY 1 DESC;

\echo ''
\echo '== Qué se usa más (últimos 7 días) =='
SELECT kind, COUNT(*) AS veces, COUNT(DISTINCT discord_id) AS jugadores, SUM(amount) AS cantidad_total
FROM game_events WHERE occurred_at > now() - interval '7 days'
GROUP BY kind ORDER BY veces DESC;

\echo ''
\echo '== Cuánto tarda cada jugador en llegar a nivel 5 y a nivel 10 (desde que se registró) =='
SELECT s.discord_id,
       s.occurred_at AT TIME ZONE 'America/Montevideo' AS empezo,
       to_char(MIN(l5.occurred_at) - s.occurred_at, 'DD "d" HH24 "h" MI "min"') AS hasta_nivel_5,
       to_char(MIN(l10.occurred_at) - s.occurred_at, 'DD "d" HH24 "h" MI "min"') AS hasta_nivel_10
FROM game_events s
LEFT JOIN game_events l5  ON l5.discord_id = s.discord_id  AND l5.kind = 'level_up'  AND l5.detail ~ '^[0-9]+$'  AND l5.detail::int >= 5
LEFT JOIN game_events l10 ON l10.discord_id = s.discord_id AND l10.kind = 'level_up' AND l10.detail ~ '^[0-9]+$' AND l10.detail::int >= 10
WHERE s.kind = 'start'
GROUP BY s.discord_id, s.occurred_at ORDER BY s.occurred_at;

\echo ''
\echo '== Peleas: ganadas vs perdidas por jugador =='
SELECT discord_id,
       COALESCE(MAX(value) FILTER (WHERE stat_key = 'hunt_win'), 0) AS cacerias_ganadas,
       COALESCE(MAX(value) FILTER (WHERE stat_key = 'travel_win'), 0) AS viajes_ganados,
       COALESCE(MAX(value) FILTER (WHERE stat_key = 'boss_win'), 0) AS jefes,
       COALESCE(MAX(value) FILTER (WHERE stat_key = 'fight_lost'), 0) AS derrotas
FROM player_stats GROUP BY discord_id ORDER BY cacerias_ganadas DESC;

\echo ''
\echo '== Oro: dado / recibido / gastado en tienda, por jugador =='
SELECT discord_id,
       COALESCE(MAX(value) FILTER (WHERE stat_key = 'gold_given'), 0) AS dado,
       COALESCE(MAX(value) FILTER (WHERE stat_key = 'gold_received'), 0) AS recibido,
       COALESCE(MAX(value) FILTER (WHERE stat_key = 'shop_gold_spent'), 0) AS gastado_en_tienda
FROM player_stats GROUP BY discord_id ORDER BY dado DESC;
