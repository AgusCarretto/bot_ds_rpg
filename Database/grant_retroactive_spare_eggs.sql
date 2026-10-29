-- =========================================================
-- v0.40.0 (corregido en la v0.41.1): huevos sobrantes RETROACTIVOS. A partir de la v0.40.0 cada primera victoria de la vuelta sobre un jefe da su huevo AUNQUE el jugador ya tenga esa mascota (el huevo sobrante
-- sirve para evolucionarla). Antes no se daba, así que a quien ya hizo Fuegos Nuevos le tocan los que no recibió, pero SOLO los que de verdad le habrían llegado:
--   · vueltas YA TERMINADAS: una por Fuego Nuevo, menos la primera de todas (ese huevo es el que ya abrió para tener la mascota): Fuegos Nuevos − 1;
--   · la vuelta EN CURSO: uno más, solo de los jefes que ya venció en esta vuelta (highest_zone_cleared; el Asador con gate_cleared). Un jugador en la Zona 2 NO recibe el de la Zona 3 ni el del Fogón.
-- Con 0 Fuegos Nuevos no se regala nada (el primer huevo es la mascota ya nacida). El primer regalo (v0.40.0) daba un huevo por Fuego Nuevo sin mirar nada de esto: sobraba uno por especie
-- (y más a quien estaba en una zona baja); fix_retroactive_spare_eggs.sql corrige a quien ya lo recibió.
--
--   psql -d asado-y-acero -v ON_ERROR_STOP=1 -f grant_retroactive_spare_eggs.sql
--
-- SE CORRE UNA SOLA VEZ: deja una marca en data_migrations y, si la marca ya está, no hace nada (correrlo de nuevo no regala otra tanda). No crea cuentas ni toca nada más que el inventario.
-- NO va en run_fresh_install.sql: una base nueva no tiene jugadores.
-- =========================================================

BEGIN;

CREATE TABLE IF NOT EXISTS data_migrations (
    name        TEXT        PRIMARY KEY,
    applied_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

DO $$
DECLARE
    v_first   BOOLEAN;
    v_granted BIGINT;
BEGIN
    INSERT INTO data_migrations (name) VALUES ('grant_retroactive_spare_eggs') ON CONFLICT (name) DO NOTHING;
    GET DIAGNOSTICS v_granted = ROW_COUNT;
    v_first := v_granted = 1;

    IF NOT v_first THEN
        RAISE NOTICE 'grant_retroactive_spare_eggs: ya se había corrido; no se regala nada otra vez.';
        RETURN;
    END IF;

    INSERT INTO inventory (discord_id, item_id, quantity)
    SELECT owed.discord_id, owed.egg_item_id, owed.amount
    FROM (
        SELECT u.discord_id, ps.egg_item_id,
               (u.fuego_nuevo - 1)
               + CASE WHEN (z.kind = 'gate' AND u.gate_cleared) OR (z.kind <> 'gate' AND u.highest_zone_cleared >= z.zone_id) THEN 1 ELSE 0 END AS amount
        FROM users u
        JOIN player_pets pp ON pp.discord_id = u.discord_id
        JOIN pet_species ps ON ps.species_id = pp.species_id
        JOIN zones z ON z.zone_id = ps.zone_id
        WHERE u.fuego_nuevo >= 1
    ) owed
    WHERE owed.amount > 0
    ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = inventory.quantity + EXCLUDED.quantity;
    GET DIAGNOSTICS v_granted = ROW_COUNT;

    RAISE NOTICE 'grant_retroactive_spare_eggs: % pilas de huevos sobrantes entregadas (Fuegos Nuevos - 1 por especie, más uno por cada jefe ya vencido en la vuelta en curso).', v_granted;
END $$;

COMMIT;
