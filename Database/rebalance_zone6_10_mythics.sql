-- =========================================================
-- v0.53.0: las armas de clase Y los amuletos de las zonas 6 a 10 piden materiales Míticos (el dueño, 2026-11-12: «poné en las recetas de amuleto y arma de zona 6 a 10 materiales míticos; el amuleto no tiene»).
-- Hasta la v0.52.2 solo las armas pedían UNO (el Meteorito las de metal, la Corteza las de madera) y el amuleto ninguno. Ahora:
--   · cada ARMA de clase pide 1 Fragmento de Meteorito y 1 Corteza del Árbol de Vida (los dos Míticos de recolección, siempre 1 de cada uno);
--   · cada AMULETO pide Meteorito y Corteza en cantidad 1 + k (k = 0 en la Zona 6 … 4 en la 10: 1, 2, 3, 4, 5 de cada uno).
-- Así el equipo de una zona (arma + amuleto) pide 2 + k de cada Mítico: 2 en la Zona 6 y 6 en la 10. El Mítico sale al 0,5 % por tirada de /chop (Corteza) y de /mine (Meteorito) y la cantidad de cada acierto la
-- multiplica el Fuego Nuevo (×2 con FN 5, ×4,4 con FN 17), así que con juego seguido son unas 13 horas de cada uno en la Zona 6 y unas 18 en la 10 (con las dos tareas a la vez, y más o menos según la suerte). También se
-- compran en el Tabernero del Inframundo (3.000 Polvo cada uno). Las recetas pasan a tener 6 tipos de ingrediente (las de las zonas 1 a 5 siguen en 3-4: la regla de «máximo 4» es de esas zonas).
--
-- Valores ABSOLUTOS (pone cada cantidad, no suma), así que correrlo dos veces deja lo mismo, y se puede correr antes o después del binario v0.53.0. Una instalación NUEVA no lo necesita: add_zones_6_10.sql ya trae
-- este estado final. Solo toca los ingredientes Míticos de esas 10 recetas. Se verifica solo: si algo no queda como debe, frena con un error.
-- =========================================================
INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT r.recipe_id, m.item_id, CASE WHEN res.type = 'Amulet' THEN 1 + (r.zone_id - 6) ELSE 1 END
FROM recipes r
JOIN items res ON res.item_id = r.result_item_id
JOIN zones z ON z.zone_id = r.zone_id AND z.kind = 'normal'
CROSS JOIN items m
WHERE r.zone_id BETWEEN 6 AND 10 AND res.type IN ('Weapon', 'Amulet') AND m.name IN ('Fragmento de Meteorito', 'Corteza del Árbol de Vida')
ON CONFLICT (recipe_id, item_id) DO UPDATE SET quantity = EXCLUDED.quantity;

DO $$
DECLARE
    v_bad TEXT;
BEGIN
    SELECT string_agg(res.name, ', ') INTO v_bad
    FROM recipes r
    JOIN items res ON res.item_id = r.result_item_id
    WHERE r.zone_id BETWEEN 6 AND 10 AND res.type IN ('Weapon', 'Amulet')
      AND ((SELECT count(*) FROM recipe_ingredients ri WHERE ri.recipe_id = r.recipe_id) <> 6
           OR (SELECT count(*) FROM recipe_ingredients ri JOIN items m ON m.item_id = ri.item_id
               WHERE ri.recipe_id = r.recipe_id AND m.name IN ('Fragmento de Meteorito', 'Corteza del Árbol de Vida')
                 AND ri.quantity = CASE WHEN res.type = 'Amulet' THEN 1 + (r.zone_id - 6) ELSE 1 END) <> 2);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rebalance_zone6_10_mythics: estas recetas no quedaron con 6 ingredientes y los dos Míticos: [%]', v_bad;
    END IF;
    IF (SELECT count(*) FROM recipes r JOIN items res ON res.item_id = r.result_item_id WHERE r.zone_id BETWEEN 6 AND 10 AND res.type IN ('Weapon', 'Amulet')) <> 25 THEN
        RAISE EXCEPTION 'rebalance_zone6_10_mythics: se esperaban 25 recetas de equipo en las zonas 6 a 10';
    END IF;
END $$;

-- Chequeo rápido: SELECT res.name, m.name, ri.quantity FROM recipes r JOIN items res ON res.item_id = r.result_item_id JOIN recipe_ingredients ri USING (recipe_id) JOIN items m ON m.item_id = ri.item_id
--                 WHERE r.zone_id BETWEEN 6 AND 10 AND m.rarity = 'Mítico' AND res.type IN ('Weapon','Amulet') ORDER BY r.zone_id, res.name, m.name;
