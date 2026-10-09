-- =========================================================
-- v0.15.1: los AMULETOS defienden más, y los jefes pegan más en la misma medida. Migración para una base que YA existe (una base nueva ya trae estos valores en
-- rework_drops_and_recipes.sql, seed_zone_bosses.sql y seed_fogon.sql: NO hace falta correr esto contra una instalación limpia). Re-ejecutable (pone valores ABSOLUTOS, no suma).
-- No cambia el código del combate: alcanza con correrlo, aunque el bot esté andando (el equipo puesto de cada jugador toma el valor nuevo solo).
--
--   psql -d asado-y-acero -v ON_ERROR_STOP=1 -f rebalance_amulets.sql
--
-- Por qué: el dueño (2026-10-09) dijo que el amuleto «no se siente» y que con el puesto se debería poder hacer /autohunt sin perder vida casi. Es cierto: la defensa se RESTA del golpe 1 a 1
-- (daño = golpe del monstruo − defensa, mínimo 1), y el amuleto de la zona (18/30/46/75) dejaba a una cacería común costando entre 11 % y 20 % de la vida. Medido con el combate real: con
-- 35/60/85/120 una común cuesta ~3 % (y una pelea de /travel 4-6 %).
--   · Amuletos de las zonas 2 a 5: 18 → 35, 30 → 60, 46 → 85, 75 → 120 (la Zona 1, 10, ya costaba ~1 % y no cambia).
--   · Brasa del Fogón Eterno (el amuleto de la puerta): 120 → 165 (+45, lo mismo que subió el de la Zona 5).
--   · Los jefes suben su daño en lo MISMO que sube el amuleto de su zona (la resta es 1 a 1), así que el examen no cambia: con el equipo propio se pierde igual que antes (~2-13 %) y con el de
--     la zona anterior se pierde más (~50-84 %, que es lo que decía el diseño: 67-87 %). Lobisón Alfa +17, Capataz de Hierro +30, Señor del Volcán +39, Soberano de la Escoria +45, El Asador Eterno +45.
--     Los monstruos de cacería y los élites de /travel NO se tocan: ese es justo el efecto buscado.
-- El PvP no cambia: el duelo y la Arena siguen usando la escalera de DEF vieja (GameData/PvpTuning.AmuletDefense).
--
-- Hacé un backup antes (DEPLOY.md, sección 6).
-- =========================================================

UPDATE items i
SET stat_value = v.stat
FROM (VALUES
    ('Talismán de Ceniza Bendita',  35),
    ('Peto de Escoria Templada',    60),
    ('Talismán del Volcán',         85),
    ('Corazón de Titán Engarzado', 120),
    ('Brasa del Fogón Eterno',     165)
) AS v(name, stat)
WHERE i.name = v.name;

-- El precio de venta/compra de las piezas de la escalera sale de su stat (la misma cuenta de rework_drops_and_recipes.sql: 6 por punto, con tope en el 60 % del oro de la receta), así que
-- sube con la DEF. La Brasa del Fogón tiene precio fijo (seed_fogon.sql) y no entra acá.
UPDATE items i
SET sell_price = LEAST(6 * i.stat_value, r.gold_cost * 6 / 10),
    buy_price  = ROUND(LEAST(6 * i.stat_value, r.gold_cost * 6 / 10) * 1.3)::INTEGER
FROM recipes r
WHERE r.result_item_id = i.item_id
  AND i.name IN ('Talismán de Ceniza Bendita', 'Peto de Escoria Templada', 'Talismán del Volcán', 'Corazón de Titán Engarzado');

UPDATE monsters m
SET min_damage = v.dmin, max_damage = v.dmax
FROM (VALUES
    ('Lobisón Alfa',            71,  96),
    ('Capataz de Hierro',      108, 142),
    ('Señor del Volcán',       151, 188),
    ('Soberano de la Escoria', 202, 248),
    ('El Asador Eterno',       233, 289)
) AS v(name, dmin, dmax)
WHERE m.name = v.name;

-- Se verifica sola: si falta un ítem o un jefe, algún UPDATE no hizo nada y esto lo grita en lugar de dejarlo a medias.
DO $$
DECLARE
    v_bad TEXT;
BEGIN
    SELECT string_agg(n, ', ') INTO v_bad FROM (
        SELECT v.name AS n FROM (VALUES
            ('Hombreras de Cuero Grueso', 10), ('Talismán de Ceniza Bendita', 35), ('Peto de Escoria Templada', 60),
            ('Talismán del Volcán', 85), ('Corazón de Titán Engarzado', 120), ('Brasa del Fogón Eterno', 165)
        ) AS v(name, stat)
        WHERE NOT EXISTS (SELECT 1 FROM items i WHERE i.name = v.name AND i.type = 'Amulet' AND i.stat_value = v.stat)
    ) x;
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rebalance_amulets: estos amuletos no quedaron con la DEF esperada: [%]', v_bad;
    END IF;

    SELECT string_agg(n, ', ') INTO v_bad FROM (
        SELECT v.name AS n FROM (VALUES
            ('Rey Jabalí', 23, 37), ('Lobisón Alfa', 71, 96), ('Capataz de Hierro', 108, 142), ('Señor del Volcán', 151, 188),
            ('Soberano de la Escoria', 202, 248), ('El Asador Eterno', 233, 289)
        ) AS v(name, dmin, dmax)
        WHERE NOT EXISTS (SELECT 1 FROM monsters m WHERE m.name = v.name AND m.is_boss AND m.min_damage = v.dmin AND m.max_damage = v.dmax)
    ) x;
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rebalance_amulets: estos jefes no quedaron con el daño esperado: [%]', v_bad;
    END IF;
END $$;
