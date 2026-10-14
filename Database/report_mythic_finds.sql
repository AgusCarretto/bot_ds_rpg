-- =========================================================
-- Informe de SOLO LECTURA (puros SELECT, no cambia nada): cuántos Míticos de recolección (Corteza del Árbol de Vida con /chop y Fragmento de Meteorito
-- con /mine) salieron de verdad contra los que tenían que salir. Sirve para ver si la chance real es el 0,5 % por uso de la tabla (RarityCatalog.GatheringChances)
-- o si pasa algo raro, y para ver cuánta mala suerte hay en cada jugador.
--
--   psql "<la URL pública de la base>" -f report_mythic_finds.sql
--
-- De dónde sale: cada /chop y /mine guarda un evento (game_events: kind chop|mine, amount = usos, detail = nombres de lo que salió). El conteo empieza el día que se
-- empezaron a registrar los eventos (v0.6). El Mítico no se puede mejorar con el oficio, así que la chance esperada es 0,5 % por uso (la avanzada cuenta 4 usos y
-- puede dar dos hallazgos juntos: se cuenta UN evento con hallazgo, no cada unidad).
-- =========================================================
\pset null '-'

\echo ''
\echo '== 1. Todos los jugadores juntos: usos, hallazgos y chance observada (esperada: 0,50 %) =='
SELECT
    SUM(amount) FILTER (WHERE kind = 'chop')                                                         AS usos_chop,
    COUNT(*)    FILTER (WHERE kind = 'chop' AND detail LIKE '%Corteza del Árbol de Vida%')           AS cortezas,
    ROUND(100.0 * COUNT(*) FILTER (WHERE kind = 'chop' AND detail LIKE '%Corteza del Árbol de Vida%')
          / NULLIF(SUM(amount) FILTER (WHERE kind = 'chop'), 0), 3)                                  AS pct_corteza,
    SUM(amount) FILTER (WHERE kind = 'mine')                                                         AS usos_mine,
    COUNT(*)    FILTER (WHERE kind = 'mine' AND detail LIKE '%Fragmento de Meteorito%')              AS meteoritos,
    ROUND(100.0 * COUNT(*) FILTER (WHERE kind = 'mine' AND detail LIKE '%Fragmento de Meteorito%')
          / NULLIF(SUM(amount) FILTER (WHERE kind = 'mine'), 0), 3)                                  AS pct_meteorito
FROM game_events
WHERE kind IN ('chop', 'mine');

\echo ''
\echo '== 2. Por jugador (los que más usaron /chop o /mine): usos, hallazgos y cuántos "tocaban" con la chance de la tabla =='
SELECT
    discord_id,
    SUM(amount) FILTER (WHERE kind = 'chop')                                              AS usos_chop,
    COUNT(*)    FILTER (WHERE kind = 'chop' AND detail LIKE '%Corteza del Árbol de Vida%') AS cortezas,
    ROUND(0.005 * SUM(amount) FILTER (WHERE kind = 'chop'), 1)                            AS cortezas_esperadas,
    SUM(amount) FILTER (WHERE kind = 'mine')                                              AS usos_mine,
    COUNT(*)    FILTER (WHERE kind = 'mine' AND detail LIKE '%Fragmento de Meteorito%')   AS meteoritos,
    ROUND(0.005 * SUM(amount) FILTER (WHERE kind = 'mine'), 1)                            AS meteoritos_esperados
FROM game_events
WHERE kind IN ('chop', 'mine')
GROUP BY discord_id
ORDER BY COALESCE(SUM(amount) FILTER (WHERE kind = 'chop'), 0) + COALESCE(SUM(amount) FILTER (WHERE kind = 'mine'), 0) DESC
LIMIT 15;

\echo ''
\echo '== 3. Lo que tienen AHORA en la mochila (Fuego Nuevo y la receta del Fogón gastan estos) =='
SELECT i.name, COUNT(*) AS jugadores_que_tienen, SUM(inv.quantity) AS unidades
FROM inventory inv
JOIN items i USING (item_id)
WHERE i.name IN ('Corteza del Árbol de Vida', 'Fragmento de Meteorito') AND inv.quantity > 0
GROUP BY i.name
ORDER BY i.name;

\echo ''
\echo '== 4. Cuántos Fuegos Nuevos hizo cada uno (para ver cuántas veces tuvieron que volver a juntarlos) =='
SELECT fuego_nuevo AS fuegos_nuevos, COUNT(*) AS jugadores FROM users GROUP BY fuego_nuevo ORDER BY fuego_nuevo;
