-- =========================================================
-- v0.38.2: la Corteza del Árbol de Vida y el Fragmento de Meteorito (los dos Míticos de recolección) se venden a 5.000 de oro (antes 500, el precio de la escala por rareza 2 / 10 / 30 / 100 / 500).
-- El dueño (2026-10-29), sobre el Brasero del Fogón: «podemos hacer que se venda más caro cada material mítico, entonces le da utilidad a que salga en el brasero». Desde la v0.38.0 ninguna caja los da salvo
-- el Brasero (el cofre del Asador, una vez por vuelta) y el Fuego Nuevo vacía la mochila, así que lo que sale ahí no puede servir para el equipo de ESA vuelta: con este precio el Mítico que cae es, como
-- mínimo, un premio en oro que se vende antes de renacer («vender todo» lo vende).
--
--   psql -d asado-y-acero -v ON_ERROR_STOP=1 -f raise_mythic_gather_price.sql
--
-- Por qué 5.000: es ×10 el precio de antes (500) y ×50 el del Legendario (100): un Mítico que cae es un premio, no un material más. Y deja el aporte de /chop + /mine en ~600 de oro por hora de juego (0,5 % por tirada × 12 tiradas por hora
-- de cada comando × 5.000 × 2 comandos): lo de una hora de cacería en la Zona 1 (840) y menos del 3 % de una hora en la Zona 5 (22.500). El Polvo de desmantelarlos (500) y su precio en el Tabernero del
-- Inframundo (3.000 Polvo) NO se tocan, así que no hay circuito de comprar y vender (3.000 Polvo son ~6.000 minutos de farmeo para recuperar 5.000 de oro). Si resulta mucho o poco, es esta cifra.
-- buy_price sigue 30 % arriba del precio de venta, como en seed.sql (chk_buy_price_not_below_sell lo exige; los materiales no se compran en la tienda).
--
-- Es solo DATOS (valor absoluto, re-ejecutable, una transacción), no necesita el binario nuevo: el precio se lee de items.sell_price. Una instalación nueva lo trae al final de
-- run_fresh_install.sql, después de remove_mythic_from_boxes.sql.
-- =========================================================

BEGIN;

UPDATE items
SET sell_price = 5000,
    buy_price  = 6500
WHERE name IN ('Corteza del Árbol de Vida', 'Fragmento de Meteorito')
  AND type IN ('Madera', 'Mineral')
  AND rarity = 'Mítico';

-- Verificación: los dos existen, valen 5.000 de venta (6.500 de compra) y no se tocó ningún otro Mítico de recolección.
DO $$
DECLARE
    v_ok    INTEGER;
    v_other INTEGER;
BEGIN
    SELECT count(*) INTO v_ok
    FROM items
    WHERE name IN ('Corteza del Árbol de Vida', 'Fragmento de Meteorito') AND sell_price = 5000 AND buy_price = 6500;
    IF v_ok <> 2 THEN
        RAISE EXCEPTION 'raise_mythic_gather_price: esperaba 2 Míticos de recolección a 5000 y hay %', v_ok;
    END IF;

    SELECT count(*) INTO v_other
    FROM items
    WHERE type IN ('Madera', 'Mineral') AND rarity = 'Mítico' AND sell_price <> 5000;
    IF v_other <> 0 THEN
        RAISE EXCEPTION 'raise_mythic_gather_price: hay % Míticos de recolección sin el precio nuevo', v_other;
    END IF;

    RAISE NOTICE 'raise_mythic_gather_price: Corteza y Meteorito a 5000 de oro.';
END $$;

COMMIT;
