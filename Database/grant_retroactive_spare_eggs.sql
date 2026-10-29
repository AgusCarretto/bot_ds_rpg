-- =========================================================
-- v0.40.0: huevos sobrantes RETROACTIVOS. A partir de la v0.40.0 cada primera victoria de la vuelta sobre un jefe da su huevo AUNQUE el jugador ya tenga esa mascota (el huevo sobrante sirve para evolucionarla).
-- Antes no se daba, así que a quien ya hizo Fuegos Nuevos le tocan los que no recibió: UN huevo sobrante por cada Fuego Nuevo que hizo, de cada especie que tiene (el dueño aprobó el regalo, 2026-10-29).
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
    SELECT u.discord_id, ps.egg_item_id, u.fuego_nuevo
    FROM users u
    JOIN player_pets pp ON pp.discord_id = u.discord_id
    JOIN pet_species ps ON ps.species_id = pp.species_id
    WHERE u.fuego_nuevo > 0
    ON CONFLICT (discord_id, item_id) DO UPDATE SET quantity = inventory.quantity + EXCLUDED.quantity;
    GET DIAGNOSTICS v_granted = ROW_COUNT;

    RAISE NOTICE 'grant_retroactive_spare_eggs: % pilas de huevos sobrantes entregadas (un huevo por Fuego Nuevo y por especie que ya tiene el jugador).', v_granted;
END $$;

COMMIT;
