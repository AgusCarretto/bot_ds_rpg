-- =========================================================
-- Informe de SALUD de la base viva (solo lectura, no cambia nada): cómo está jugando la gente después de los últimos cambios (EXP de las peleas a la mitad v0.18.1, daily nuevo v0.18.2,
-- el voto v0.18.0, la vida de los jefes v0.17.1). Todo agregado: NO imprime ids ni nombres de nadie, así que se puede pegar tal cual en un chat.
--
--   $env:PGCLIENTENCODING = "UTF8"
--   & "C:\Program Files\PostgreSQL\18\bin\psql.exe" $url -f report_live_health.sql      (desde la carpeta Database; $url entre comillas simples)
--
-- Los días se cuentan con la hora de Uruguay (la de las misiones). Si una sección sale vacía es que todavía no hay datos de eso (por ejemplo, ningún voto).
-- =========================================================
\pset pager off
\set ON_ERROR_STOP off

\echo ''
\echo '== A. Jugadores y actividad (por el último evento registrado) =='
WITH act AS (SELECT discord_id, max(occurred_at) AS ultimo FROM game_events GROUP BY discord_id)
SELECT count(*) AS jugadores,
       count(*) FILTER (WHERE act.ultimo > now() - interval '1 day')  AS activos_24h,
       count(*) FILTER (WHERE act.ultimo > now() - interval '7 days') AS activos_7d,
       count(*) FILTER (WHERE act.ultimo IS NULL OR act.ultimo <= now() - interval '30 days') AS inactivos_30d
FROM users u LEFT JOIN act USING (discord_id);

\echo ''
\echo '== B. Jugadores por nivel (y cuántos ya hicieron Fuego Nuevo) =='
SELECT CASE WHEN level < 5 THEN '01-04' WHEN level < 10 THEN '05-09' WHEN level < 15 THEN '10-14' WHEN level < 20 THEN '15-19'
            WHEN level < 25 THEN '20-24' WHEN level < 40 THEN '25-39' ELSE '40+' END AS nivel,
       count(*) AS jugadores,
       count(*) FILTER (WHERE fuego_nuevo > 0) AS con_fuego_nuevo,
       round(avg(gold)) AS oro_promedio
FROM users GROUP BY 1 ORDER BY 1;

\echo ''
\echo '== C. Hasta qué zona llegaron (jefe más alto vencido en esta vuelta; 0 = ninguno) =='
SELECT highest_zone_cleared AS jefe_vencido_hasta_zona, count(*) AS jugadores, count(*) FILTER (WHERE gate_cleared) AS vencieron_al_asador
FROM users GROUP BY 1 ORDER BY 1;

\echo ''
\echo '== D. Ritmo de los últimos 8 días (hora de Uruguay): peleas ganadas, subidas de nivel y derrotas =='
SELECT (occurred_at AT TIME ZONE 'America/Montevideo')::date AS dia,
       count(*) FILTER (WHERE kind = 'hunt_win')   AS cacerias,
       count(*) FILTER (WHERE kind = 'travel_win') AS viajes,
       count(*) FILTER (WHERE kind = 'boss_win')   AS jefes,
       count(*) FILTER (WHERE kind = 'raid_win')   AS raids,
       count(*) FILTER (WHERE kind = 'fight_lost') AS derrotas,
       coalesce(sum(amount) FILTER (WHERE kind = 'level_up'), 0) AS niveles_subidos,
       count(DISTINCT discord_id) AS jugadores_activos
FROM game_events
WHERE occurred_at > now() - interval '8 days'
GROUP BY 1 ORDER BY 1;

\echo ''
\echo '== E. % de derrotas por tipo de pelea, últimos 7 días =='
SELECT t.tipo,
       coalesce(w.n, 0) AS victorias,
       coalesce(l.n, 0) AS derrotas,
       round(100.0 * coalesce(l.n, 0) / nullif(coalesce(w.n, 0) + coalesce(l.n, 0), 0), 1) AS pct_derrota
FROM (VALUES ('hunt'), ('travel'), ('boss'), ('raid')) AS t(tipo)
LEFT JOIN (SELECT replace(kind, '_win', '') AS tipo, count(*) AS n FROM game_events
           WHERE kind IN ('hunt_win', 'travel_win', 'boss_win', 'raid_win') AND occurred_at > now() - interval '7 days' GROUP BY 1) w USING (tipo)
LEFT JOIN (SELECT detail AS tipo, count(*) AS n FROM game_events
           WHERE kind = 'fight_lost' AND occurred_at > now() - interval '7 days' GROUP BY 1) l USING (tipo)
ORDER BY 1;

\echo ''
\echo '== F. Daily: reclamos por día (últimos 8 días) =='
SELECT (occurred_at AT TIME ZONE 'America/Montevideo')::date AS dia, count(*) AS reclamos, count(DISTINCT discord_id) AS jugadores
FROM game_events WHERE kind = 'daily_claim' AND occurred_at > now() - interval '8 days'
GROUP BY 1 ORDER BY 1;

\echo ''
\echo '== G. Daily: racha ACTUAL de cada jugador (solo cuenta si reclamó en las últimas 48 h) =='
SELECT CASE WHEN daily_streak = 0 THEN '0 (nunca)' WHEN daily_streak <= 2 THEN '1-2' WHEN daily_streak <= 5 THEN '3-5' WHEN daily_streak = 6 THEN '6'
            WHEN daily_streak = 7 THEN '7' WHEN daily_streak <= 13 THEN '8-13' WHEN daily_streak <= 20 THEN '14-20' ELSE '21+' END AS racha,
       count(*) AS jugadores
FROM users
WHERE last_daily_claim > now() - interval '48 hours'
GROUP BY 1 ORDER BY 1;

\echo ''
\echo '== H. Logro Racha (la racha más larga): cuántos pasaron cada tramo =='
SELECT count(*) FILTER (WHERE value >= 7) AS tramo_I_7_dias, count(*) FILTER (WHERE value >= 30) AS tramo_II_30_dias, count(*) FILTER (WHERE value >= 100) AS tramo_III_100_dias,
       coalesce(max(value), 0) AS mejor_racha
FROM player_stats WHERE stat_key = 'daily_streak_best';

\echo ''
\echo '== I. Votos (/vote) por día, y cuántos reseteaban un jefe en cooldown =='
SELECT (occurred_at AT TIME ZONE 'America/Montevideo')::date AS dia, count(*) AS votos_cobrados, count(*) FILTER (WHERE detail = 'boss_reset') AS con_reseteo_de_jefe,
       count(DISTINCT discord_id) AS jugadores
FROM game_events WHERE kind = 'vote'
GROUP BY 1 ORDER BY 1;

\echo ''
\echo '== J. Cajas abiertas por tipo, últimos 7 días =='
SELECT detail AS caja, count(*) AS aperturas, sum(amount) AS cajas
FROM game_events WHERE kind = 'box_opened' AND occurred_at > now() - interval '7 days'
GROUP BY 1 ORDER BY 3 DESC;

\echo ''
\echo '== K. Oro y Polvo en circulación =='
SELECT sum(gold) AS oro_en_billeteras, sum(bank_gold) AS oro_en_bancos, sum(dust) AS polvo_total,
       round(percentile_cont(0.5) WITHIN GROUP (ORDER BY gold)) AS oro_mediana,
       round(percentile_cont(0.9) WITHIN GROUP (ORDER BY gold)) AS oro_p90,
       max(gold) AS oro_maximo
FROM users;

\echo ''
\echo '== L. Fuego Nuevo: cuántos llevan cada vuelta =='
SELECT fuego_nuevo AS vueltas_hechas, count(*) AS jugadores, count(*) FILTER (WHERE gate_cleared) AS pueden_renovar_ya
FROM users GROUP BY 1 ORDER BY 1;

\echo ''
\echo 'Listo. Pegame todo lo que salió (no lleva ids ni nombres).'
