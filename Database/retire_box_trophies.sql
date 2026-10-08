-- =========================================================
-- v0.14.4: se retiran los 17 "trofeos" de las cajas. Son materiales que NINGÚN monstruo suelta y que solo salían de las cajas, y que no sirven para nada: no están en ninguna
-- receta ni se usan en otro lado (el dueño, 2026-10-07: «dentro de las cajas hay items que no se usan en nada… quiero que la gente que juegue no tenga eso al pedo»). Solo se
-- podían vender, desmantelar por Polvo o juntar para el logro Coleccionista (que sale del código en la misma versión: sin trofeos no se puede completar).
--
--   psql -d asado-y-acero -v ON_ERROR_STOP=1 -f retire_box_trophies.sql
--
-- Sirve para las DOS cosas: una base que ya existe (migración) y una instalación nueva (va al final de run_fresh_install.sql, después de seed_zone_boxes.sql, que todavía los usa).
-- Es re-ejecutable: si ya no quedan trofeos, no hace nada. Hacé un backup antes (DEPLOY.md, sección 6).
--
-- Qué hace, todo en UNA transacción (si algo falla, no queda nada a medias):
--   1. Se asegura de que ninguno sirva para otra cosa (receta, drop de monstruo, mascota, buff): si alguno se usa, aborta sin tocar nada.
--   2. REEMBOLSA en oro a quien tenga alguno en la mochila, a su precio de venta (borrar un ítem borra su inventario en cascada y sin esto se perdería sin cobrar nada).
--   3. Le suma a la entrada de RECOLECCIÓN de cada caja el peso que ocupaban sus trofeos (cada caja sigue sumando 1000 sin contar el oro) y borra esas entradas del botín.
--   4. Borra los ítems (en cascada: mochila, colección y botín) y verifica el resultado.
--
-- OJO, igual que con rework_drops_and_recipes.sql: una vez retirados, NO vuelvas a correr rework_boxes.sql ni seed_zone_boxes.sql SOLOS contra esa base (vuelven a pedir los trofeos y
-- fallan porque ya no existen); sobre una base nueva van en el orden de run_fresh_install.sql y esto va al final.
-- =========================================================

DO $$
DECLARE
    v_names     TEXT[] := ARRAY[
        'Collar de Cuero Viejo', 'Cuero Curtido de Pradera', 'Pelaje Oscuro', 'Piedra Caliente', 'Tela Rasgada',          -- Cajón de Pino
        'Corona de Cerdas', 'Garra Maldita', 'Hueso Añejo', 'Rama Carbonizada',                                              -- Baúl de Roble
        'Garra del Alfa', 'Núcleo Ígneo', 'Polvo de Mina Sagrada',                                                           -- Arcón de Hierro
        'Colmillo del Señor del Volcán', 'Martillo del Capataz', 'Roca Volcánica Pura',                                      -- Cofre de Oro
        'Corona de Escoria Viva', 'Escoria Pura del Cráter'                                                                  -- Arca del Soberano y Cofre de Escoria
    ];
    v_ids       INTEGER[];
    v_found     INTEGER;
    v_used      TEXT;
    v_holders   INTEGER;
    v_refunded  BIGINT;
    v_bad       TEXT;
BEGIN
    SELECT array_agg(item_id), count(*) INTO v_ids, v_found FROM items WHERE name = ANY (v_names);

    IF v_found = 0 THEN
        RAISE NOTICE 'retire_box_trophies: no quedan trofeos (ya estaban retirados): no se toca nada.';
        RETURN;
    END IF;

    IF v_found <> array_length(v_names, 1) THEN
        RAISE EXCEPTION 'retire_box_trophies: se esperaban % trofeos y hay % (un retiro a medias o un catálogo distinto): revisalo a mano antes de seguir', array_length(v_names, 1), v_found;
    END IF;

    -- 1) Que no sirvan para otra cosa.
    SELECT string_agg(DISTINCT i.name, ', ') INTO v_used
    FROM items i
    WHERE i.item_id = ANY (v_ids)
      AND (EXISTS (SELECT 1 FROM recipe_ingredients r WHERE r.item_id = i.item_id)
        OR EXISTS (SELECT 1 FROM recipes r WHERE r.result_item_id = i.item_id)
        OR EXISTS (SELECT 1 FROM monster_drops d WHERE d.item_id = i.item_id)
        OR EXISTS (SELECT 1 FROM pet_species p WHERE p.egg_item_id = i.item_id)
        OR EXISTS (SELECT 1 FROM item_buffs b WHERE b.item_id = i.item_id)
        OR EXISTS (SELECT 1 FROM boxes x WHERE x.box_item_id = i.item_id)
        OR EXISTS (SELECT 1 FROM users u WHERE u.weapon_id = i.item_id OR u.amulet_id = i.item_id));
    IF v_used IS NOT NULL THEN
        RAISE EXCEPTION 'retire_box_trophies: estos "trofeos" SÍ se usan en algo, no se borran: [%]', v_used;
    END IF;

    -- 2) Reembolso en oro a su precio de venta, ANTES de borrar (el borrado se lleva la mochila).
    SELECT count(DISTINCT inv.discord_id), COALESCE(sum(inv.quantity::bigint * i.sell_price), 0) INTO v_holders, v_refunded
    FROM inventory inv JOIN items i ON i.item_id = inv.item_id
    WHERE inv.item_id = ANY (v_ids) AND inv.quantity > 0;

    UPDATE users u
    SET gold = u.gold + sub.refund
    FROM (
        SELECT inv.discord_id, sum(inv.quantity::bigint * i.sell_price)::integer AS refund
        FROM inventory inv JOIN items i ON i.item_id = inv.item_id
        WHERE inv.item_id = ANY (v_ids) AND inv.quantity > 0
        GROUP BY inv.discord_id
    ) sub
    WHERE u.discord_id = sub.discord_id;

    -- 3) El peso que ocupaban los trofeos pasa a la recolección de la MISMA caja (el material útil y base del botín); sus entradas se borran.
    UPDATE box_loot g
    SET weight = g.weight + t.freed
    FROM (
        SELECT box_item_id, sum(weight) AS freed
        FROM box_loot
        WHERE item_id = ANY (v_ids)
        GROUP BY box_item_id
    ) t
    WHERE g.box_item_id = t.box_item_id AND g.kind = 'gather';

    SELECT string_agg(b.name, ', ') INTO v_bad
    FROM (SELECT DISTINCT box_item_id FROM box_loot WHERE item_id = ANY (v_ids)) x
    JOIN items b ON b.item_id = x.box_item_id
    WHERE (SELECT count(*) FROM box_loot g WHERE g.box_item_id = x.box_item_id AND g.kind = 'gather') <> 1;
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'retire_box_trophies: estas cajas no tienen UNA entrada de recolección donde pasar el peso: [%]', v_bad;
    END IF;

    DELETE FROM box_loot WHERE item_id = ANY (v_ids);

    -- 4) Se borran los ítems (en cascada: mochila, colección y lo que quedara).
    DELETE FROM items WHERE item_id = ANY (v_ids);

    -- Verificación: cada caja sigue sumando 1000 (sin contar el oro) y ya no queda ningún material suelto que ningún monstruo suelte ni ninguna receta use.
    SELECT string_agg(b.name || '=' || s.total, ', ') INTO v_bad
    FROM (SELECT box_item_id, sum(weight) FILTER (WHERE kind <> 'gold') AS total FROM box_loot GROUP BY box_item_id) s
    JOIN items b ON b.item_id = s.box_item_id
    WHERE s.total <> 1000;
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'retire_box_trophies: el botín de estas cajas ya no suma 1000: [%]', v_bad;
    END IF;

    SELECT string_agg(i.name, ', ') INTO v_bad
    FROM items i
    WHERE i.type = 'Material'
      AND EXISTS (SELECT 1 FROM box_loot l WHERE l.item_id = i.item_id)
      AND NOT EXISTS (SELECT 1 FROM monster_drops d WHERE d.item_id = i.item_id)
      AND NOT EXISTS (SELECT 1 FROM recipe_ingredients r WHERE r.item_id = i.item_id);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'retire_box_trophies: todavía quedan materiales inútiles en las cajas: [%]', v_bad;
    END IF;

    RAISE NOTICE 'retire_box_trophies: listo. % trofeos retirados; % jugador(es) tenían alguno y cobraron % de oro en total.', v_found, v_holders, v_refunded;
END $$;
