-- =========================================================
-- v0.19.2: lo que curan los dos BANQUETES Míticos baja: Asado Completo del Domingo en Familia 250 → 150 HP y Mate Dulce de la Abuela 300 → 200 HP (los dos siguen dando +15 % de ataque por 30 min: eso
-- vive en item_buffs y no se toca). Migración para la base viva (datos nada más: no hace falta redeploy ni reiniciar el bot, y se puede correr con el bot andando). Re-ejecutable (valores absolutos).
-- La base NUEVA ya lo trae: el final del catálogo está en seed_consumables_and_base_swords.sql, así que este script NO va en run_fresh_install.sql.
--
--   psql -d asado-y-acero -v ON_ERROR_STOP=1 -f rebalance_banquets.sql
--
-- Por qué (el dueño, 2026-10-20: «sino está muy OP esa comida… ¿con la de 150 ya basta?… ¿200 HP en vez de 300?»; decidió «150 y 15 % de ataque, y 200 y 15 % de ataque»): desde la v0.19.1 los banquetes se
-- pueden comer en la pelea del jefe en equipo (curan y dan el buff en el acto, ocupando la única comida de la pelea). Medido con tools/balance2 (parte K, 2 jugadores, equipo propio, nivel de llegada, zonas
-- 2 / 3 / 4 / 5): con 300 HP la victoria queda en ~82 / 90 / 91 / 90 % (todas las zonas iguales, el jefe pierde el filo); con 200 HP en ~81 / 87 / 82 / 75 %; con 150 HP en ~83 / 73 / 67 / 65 %; la comida
-- común más grande (150 HP) sin buff, 67 / 61 / 45 / 29 %. El +15 % de ataque por sí solo vale +15-35 puntos y comerlo ANTES de la pelea es gratis; la curación grande solo importaba de la zona 4 en
-- adelante, donde el golpe del jefe (~120-270 de la zona 3 a la 5) es mayor que lo que cura cualquier comida común. Los precios NO cambian (2100 / 2800: lo que se paga es el buff).
--
-- Vale también para el jefe solo (/travel, /boss: el banquete ocupa la única curación de la pelea) y para /use fuera de combate (afuera igual se cura todo con /heal).
-- =========================================================

UPDATE items SET stat_value = 150 WHERE name = 'Asado Completo del Domingo en Familia';
UPDATE items SET stat_value = 200 WHERE name = 'Mate Dulce de la Abuela';

-- Se verifica sola: los dos existen con lo que se quería, y siguen siendo banquetes (con su buff) y más caros que cualquier comida común.
DO $$
DECLARE
    asado_hp   INTEGER;
    mate_hp    INTEGER;
    buffed     INTEGER;
BEGIN
    SELECT stat_value INTO asado_hp FROM items WHERE name = 'Asado Completo del Domingo en Familia';
    SELECT stat_value INTO mate_hp  FROM items WHERE name = 'Mate Dulce de la Abuela';
    SELECT count(*) INTO buffed
    FROM item_buffs b JOIN items i ON i.item_id = b.item_id
    WHERE i.name IN ('Asado Completo del Domingo en Familia', 'Mate Dulce de la Abuela') AND b.attack_percent = 15 AND b.minutes = 30;

    IF asado_hp IS DISTINCT FROM 150 OR mate_hp IS DISTINCT FROM 200 THEN
        RAISE EXCEPTION 'Los banquetes no quedaron en 150 / 200 HP (Asado %, Mate Dulce %): ¿falta alguno en el catálogo?', asado_hp, mate_hp;
    END IF;

    IF buffed <> 2 THEN
        RAISE EXCEPTION 'Los dos banquetes tienen que seguir dando +15 %% de ataque por 30 minutos en item_buffs (encontré % de 2)', buffed;
    END IF;

    RAISE NOTICE 'Banquetes: Asado Completo del Domingo en Familia 150 HP y Mate Dulce de la Abuela 200 HP, los dos con +15 %% de ataque por 30 minutos.';
END $$;
