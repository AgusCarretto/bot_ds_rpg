-- =========================================================
-- Asado y Acero RPG — el monstruo DEDICADO de /travel: uno por zona, marcado is_travel = true y EXCLUIDO del
-- pool de /hunt (ver Repositories/MonsterRepository.GetTravelMonsterByZoneAsync). /travel enfrenta siempre a
-- ESE monstruo (el de la zona actual del jugador) y suelta SU drop, igual que un /hunt.
--
-- Ejecutar DESPUÉS de Database/add_monster_is_travel.sql (o schema.sql en instalación nueva) y de los seeds
-- que crean las zonas y los materiales que sueltan (seed_zones_and_monsters.sql,
-- seed_class_gear_and_monster_drops.sql). Re-ejecutable, y se VERIFICA SOLO: si falta una zona o un material
-- FALLA en voz alta (mismo criterio que seed_recipes.sql — un monstruo sin drops se cargaría en silencio).
--
-- CALIBRACIÓN (medida con el CombatTurnResolver real, 4 clases, sin consumibles; misma escalera que los
-- monstruos comunes de seed_zones_and_monsters.sql): el monstruo de /travel es el PROMEDIO de los rangos de los
-- comunes de su zona con HP x1.25 y daño x1.1 — un élite, no un jefe. Con el equipo de la zona anterior cuesta
-- ~62-68% de la vida (un común: ~47%) y se pierde 12-16% de las veces (Zona 1: ~2%); con el equipo propio cuesta
-- ~23-32% (un común: 13-18%) y casi nunca se pierde. Huir lo cierra sin perder nada salvo la recompensa.
-- (Zona 1 se calculó con el promedio de sus 6 monstruos de antes; finalize_monster_roster.sql dejó 3 escalados
-- para que la zona cueste lo mismo, así que el número sigue valiendo.)
--
-- RECOMPENSA: gold_reward / xp_reward son el MISMO bonus que los comunes de la zona (0/0, 38/30, 88/70, 175/138,
-- 325/250); /travel multiplica la fórmula entera de /hunt x10 (GameData/CombatRewardCalculator.RollTravelReward),
-- que es lo que el cooldown de 10 minutos equivale a diez cacerías.
--
-- DROP: UN solo material, de los que YA existen (no nacen ítems sin receta ni sin emoji), y es el "escaso" de la
-- zona: chance de drop 20% y un viaje cada 10 minutos = ~1 unidad cada 50 min (el "a granel" sale de /hunt y el
-- "raro" del jefe). Los nombres de los monstruos calzan con lo que sueltan: un Toro Bravo da Cuero Grueso, un
-- Dragón de Lava el Aliento de Fuego Eterno. Las recetas (seed_recipes.sql, seed_zoneN_gear_and_recipes.sql) piden
-- este material para las armas de afinidad y el amuleto de fondo de cada zona.
-- =========================================================

DO $$
DECLARE
    t            RECORD;
    v_zone_id    INTEGER;
    v_monster_id INTEGER;
    v_item_id    INTEGER;
    v_names      TEXT[] := ARRAY[]::TEXT[];
BEGIN
    FOR t IN
        SELECT * FROM (VALUES
            ('Praderas del Mate',    'Toro Bravo',         '🐂',  67, 105,   8,  18,   0,   0, 'Cuero Grueso'),
            ('Bosque de Cenizas',    'Ciervo Sagrado',     '🦌', 178, 278,  37,  63,  38,  30, 'Ceniza Bendita'),
            ('Minas del Yunque',     'Mole de Escoria',    '🪨', 326, 476,  63,  98,  88,  70, 'Escoria Metálica Densa'),
            ('Cordillera del Fuego', 'Dragón de Lava',     '🐉', 489, 684,  92, 138, 175, 138, 'Aliento de Fuego Eterno'),
            ('Cráter de la Escoria', 'Quimera del Abismo', '🦂', 719, 972, 127, 188, 325, 250, 'Ceniza del Abismo')
        ) AS v(zone_name, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward, drop_name)
    LOOP
        SELECT zone_id INTO v_zone_id FROM zones WHERE name = t.zone_name;
        IF v_zone_id IS NULL THEN
            RAISE EXCEPTION 'seed_travel_monsters: no existe la zona "%" (corré seed_zones_and_monsters.sql antes)', t.zone_name;
        END IF;

        SELECT item_id INTO v_item_id FROM items WHERE name = t.drop_name;
        IF v_item_id IS NULL THEN
            RAISE EXCEPTION 'seed_travel_monsters: no existe el material "%" que suelta %', t.drop_name, t.name;
        END IF;

        INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward, is_boss, is_travel)
        VALUES (v_zone_id, t.name, t.emoji, t.min_hp, t.max_hp, t.min_damage, t.max_damage, t.gold_reward, t.xp_reward, false, true)
        ON CONFLICT (name) DO UPDATE SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji,
            min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
            max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward,
            is_boss = EXCLUDED.is_boss, is_travel = EXCLUDED.is_travel
        RETURNING monster_id INTO v_monster_id;

        -- El drop se recarga desde cero: este archivo es la fuente de verdad, así un cambio de acá arriba no deja
        -- drops viejos colgados al re-ejecutarlo.
        DELETE FROM monster_drops WHERE monster_id = v_monster_id;
        INSERT INTO monster_drops (monster_id, item_id) VALUES (v_monster_id, v_item_id);

        v_names := v_names || t.name;
    END LOOP;

    -- Un monstruo de /travel que ya no está en la lista (renombrado o reemplazado) se borra: si no, la zona quedaría
    -- con dos y /travel elegiría uno cualquiera. Sus drops se van en cascada; los ítems y las mochilas no se tocan.
    DELETE FROM monsters WHERE is_travel AND name <> ALL (v_names);
END $$;

-- Chequeo rápido: uno por zona.
-- SELECT z.name AS zona, m.name, m.min_hp, m.max_hp, m.min_damage, m.max_damage, m.gold_reward, m.xp_reward, i.name AS suelta
-- FROM monsters m JOIN zones z USING (zone_id) JOIN monster_drops d USING (monster_id) JOIN items i USING (item_id)
-- WHERE m.is_travel ORDER BY z.min_level;
