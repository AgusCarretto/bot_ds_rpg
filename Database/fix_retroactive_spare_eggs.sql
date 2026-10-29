-- =========================================================
-- v0.41.1: CORRIGE el regalo retroactivo de huevos sobrantes de la v0.40.0 a quien ya lo recibió. El primer regalo dio UN HUEVO POR FUEGO NUEVO de cada mascota sin mirar nada más (el dueño, 2026-10-29: «regalamos
-- la cantidad de FN en huevos, no quiero eso: tenés el primero que se abrió ya… y depende la zona: si estoy en la zona 2 me quedan de más»). Lo que le tocaba de verdad (ver grant_retroactive_spare_eggs.sql):
--   Fuegos Nuevos − 1 por especie (el primer huevo es la mascota ya nacida) + 1 por cada jefe que ya había vencido en la vuelta EN CURSO cuando se dio el regalo (el Asador con gate_cleared).
-- Este script le SACA a cada jugador la diferencia entre lo regalado y lo que le tocaba, SIN pasarse de lo que tiene (si ya gastó huevos evolucionando, no queda en negativo ni se le toca lo demás).
-- Lo que el jugador ganó DESPUÉS del regalo venciendo jefes con el sistema nuevo (los huevos reales) NO se toca: lo que se descuenta es solo el regalo que sobró.
--
--   psql -d asado-y-acero -v ON_ERROR_STOP=1 -f fix_retroactive_spare_eggs.sql
--
-- SE CORRE UNA SOLA VEZ (deja la marca «fix_retroactive_spare_eggs» en data_migrations) y solo si el regalo original SÍ se corrió (marca «grant_retroactive_spare_eggs»); si el regalo todavía no se corrió, hay
-- que correr grant_retroactive_spare_eggs.sql, que ya trae la cuenta correcta, y este NO hace falta. Una base nueva no lo necesita.
--
-- Cómo reconstruye lo que se regaló y lo que tocaba, sin guardar nada extra (todo sale de lo que el juego ya registra):
--   · «Fuegos Nuevos que había al regalar» = filas de fuego_nuevo_history terminadas ANTES de la hora en que se corrió el regalo (la marca de data_migrations);
--   · «jefes vencidos en esa vuelta» = eventos boss_win / raid_win en la zona de la mascota (gate_win para la del Fogón) entre el último Fuego Nuevo de entonces y la hora del regalo;
--   · solo cuentan las mascotas que el jugador YA tenía al regalar (player_pets.hatched_at anterior): el regalo no llegó a las que nacieron después.
-- =========================================================

BEGIN;

CREATE TABLE IF NOT EXISTS data_migrations (
    name        TEXT        PRIMARY KEY,
    applied_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

DO $$
DECLARE
    v_gift_at TIMESTAMPTZ;
    v_marked  BIGINT;
    v_rows    BIGINT;
    v_removed BIGINT;
BEGIN
    SELECT applied_at INTO v_gift_at FROM data_migrations WHERE name = 'grant_retroactive_spare_eggs';
    IF v_gift_at IS NULL THEN
        RAISE NOTICE 'fix_retroactive_spare_eggs: el regalo retroactivo nunca se corrió; no hay nada que corregir (corré grant_retroactive_spare_eggs.sql, que ya trae la cuenta correcta).';
        RETURN;
    END IF;

    INSERT INTO data_migrations (name) VALUES ('fix_retroactive_spare_eggs') ON CONFLICT (name) DO NOTHING;
    GET DIAGNOSTICS v_marked = ROW_COUNT;
    IF v_marked = 0 THEN
        RAISE NOTICE 'fix_retroactive_spare_eggs: ya se había corrido; no se descuenta nada otra vez.';
        RETURN;
    END IF;

    DROP TABLE IF EXISTS egg_fix;
    CREATE TEMP TABLE egg_fix ON COMMIT DROP AS
    SELECT s.discord_id, s.egg_item_id, s.granted, s.granted - s.owed AS excess
    FROM (
        SELECT u.discord_id, ps.egg_item_id,
               k.then_count AS granted,
               (k.then_count - 1)
               + CASE WHEN EXISTS (
                     SELECT 1 FROM game_events e
                     WHERE e.discord_id = u.discord_id
                       AND e.occurred_at >= k.lap_start AND e.occurred_at < v_gift_at
                       AND ((z.kind = 'gate' AND e.kind = 'gate_win') OR (z.kind <> 'gate' AND e.kind IN ('boss_win', 'raid_win') AND e.zone_id = z.zone_id))
                 ) THEN 1 ELSE 0 END AS owed
        FROM users u
        JOIN player_pets pp ON pp.discord_id = u.discord_id AND pp.hatched_at < v_gift_at
        JOIN pet_species ps ON ps.species_id = pp.species_id
        JOIN zones z ON z.zone_id = ps.zone_id
        CROSS JOIN LATERAL (
            SELECT COUNT(*)::int AS then_count,
                   COALESCE(MAX(h.finished_at), '-infinity'::timestamptz) AS lap_start
            FROM fuego_nuevo_history h
            WHERE h.discord_id = u.discord_id AND h.finished_at < v_gift_at
        ) k
        WHERE k.then_count >= 1
    ) s
    WHERE s.granted > s.owed;

    -- Se saca la diferencia, sin pasarse de lo que hay en la mochila.
    UPDATE inventory inv
    SET quantity = inv.quantity - LEAST(f.excess, inv.quantity)
    FROM egg_fix f
    WHERE inv.discord_id = f.discord_id AND inv.item_id = f.egg_item_id;
    GET DIAGNOSTICS v_rows = ROW_COUNT;

    SELECT COALESCE(SUM(excess), 0) INTO v_removed FROM egg_fix;
    DELETE FROM inventory WHERE quantity <= 0 AND item_id IN (SELECT egg_item_id FROM pet_species);

    RAISE NOTICE 'fix_retroactive_spare_eggs: % pilas corregidas (hasta % huevos descontados en total).', v_rows, v_removed;
END $$;

COMMIT;
