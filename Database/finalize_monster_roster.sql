-- =========================================================
-- Asado y Acero RPG — PLANTEL FINAL de monstruos y de drops: cada monstruo suelta UN solo ítem, y /hunt tiene
-- 3 monstruos en Zona 1 y 2 en cada una de las otras. Esta lista es LA fuente de verdad de los drops de los
-- monstruos de /hunt y de los jefes (los de /travel los fija seed_travel_monsters.sql); las listas de dos drops
-- que todavía traen seed_class_gear_and_monster_drops.sql / seed_zones_and_monsters.sql / seed_zone_bosses.sql
-- son el punto de partida que este archivo pisa — por eso va DESPUÉS de esos tres y del de /travel.
--
-- POR QUÉ UN SOLO DROP: con dos, cada ítem salía la mitad de las veces y nadie sabía qué esperar de cada bicho.
-- Con uno, "qué suelta cada monstruo" cabe en una línea (ver /drops) y las recetas se arman contra fuentes
-- concretas: cada zona tiene 4 materiales de drop — uno por monstruo de /hunt (los dos "a granel"), el del
-- monstruo de /travel (el "escaso", ~1 cada 33 min) y el del jefe (el "raro", ~1 cada 150 min) —, y las
-- cantidades de las recetas (seed_recipes.sql, seed_zoneN_gear_and_recipes.sql) están calibradas contra esas tasas.
--
-- ZONA 1 pasa de 6 a 3 monstruos de /hunt (se van Lobisón de las Cenizas, Gólem de Escoria y Cuatrero No-Muerto).
-- Los 3 que quedan son los que sueltan los materiales de las recetas de la zona, y se escalaron x1.15 de HP y x1.1
-- de daño para que la zona cueste LO MISMO que con los 6 (medido con el CombatTurnResolver real, 4 clases, nivel 1
-- sin equipo: 6.0 turnos / 32% de la vida / 0.5% de derrota, contra 6.0 / 33% / 0.4% con los 6). El monstruo de
-- /travel de Zona 1 se calibró contra el promedio de los 6, así que sigue valiendo.
-- Borrar un monstruo NO toca inventarios (monster_drops se va en cascada, los ítems quedan): los materiales que
-- ya no suelta ningún monstruo (Pelaje Oscuro, Garra Maldita, Núcleo Ígneo, Hueso Añejo, Ceniza... etc.) siguen en
-- el catálogo y en las mochilas de quien los tenga, pero ya no se consiguen.
--
-- Re-ejecutable, y FALLA EN VOZ ALTA si falta una zona, un monstruo o un ítem, si sobra un monstruo que no está
-- en esta lista, o si algún monstruo no termina con exactamente un drop.
-- Ejecutar DESPUÉS de seed_zones_and_monsters.sql, seed_zone_bosses.sql y seed_travel_monsters.sql.
-- =========================================================

DO $$
DECLARE
    t             RECORD;
    v_zone_id     INTEGER;
    v_monster_id  INTEGER;
    v_item_id     INTEGER;
    v_extra       TEXT;
    v_bad         TEXT;
BEGIN
    -- 1) Se van los 3 monstruos de Zona 1 que sobran (sus drops se borran en cascada; los ítems y las mochilas no).
    DELETE FROM monsters
    WHERE name IN ('Lobisón de las Cenizas', 'Gólem de Escoria', 'Cuatrero No-Muerto')
      AND zone_id = (SELECT zone_id FROM zones WHERE name = 'Praderas del Mate');

    -- 2) El plantel: zona, tipo ('hunt' o 'boss'), monstruo, su ÚNICO drop y (solo para los de Zona 1 escalados)
    -- sus stats nuevos. NULL en los stats = no se tocan.
    CREATE TEMP TABLE roster (zone_name TEXT, kind TEXT, name TEXT, drop_name TEXT,
                              min_hp INTEGER, max_hp INTEGER, min_damage INTEGER, max_damage INTEGER) ON COMMIT DROP;
    INSERT INTO roster VALUES
        -- Zona 1: Praderas del Mate
        ('Praderas del Mate',    'hunt', 'Jabalí Rabioso',         'Colmillo de Jabalí',         64, 97,  9, 19),
        ('Praderas del Mate',    'hunt', 'Perro Cimarrón',         'Colmillo de Cimarrón',       51, 80,  7, 14),
        ('Praderas del Mate',    'hunt', 'Ñandú Salvaje',          'Pluma de Ñandú',             46, 74,  6, 13),
        ('Praderas del Mate',    'boss', 'Rey Jabalí',             'Colmillo del Rey Jabalí',  NULL, NULL, NULL, NULL),
        -- Zona 2: Bosque de Cenizas
        ('Bosque de Cenizas',    'hunt', 'Puma de las Cenizas',    'Garra de Puma Cenizo',     NULL, NULL, NULL, NULL),
        ('Bosque de Cenizas',    'hunt', 'Espíritu del Monte',     'Esencia Espectral',        NULL, NULL, NULL, NULL),
        ('Bosque de Cenizas',    'boss', 'Lobisón Alfa',           'Pelaje Plateado del Alfa', NULL, NULL, NULL, NULL),
        -- Zona 3: Minas del Yunque
        ('Minas del Yunque',     'hunt', 'Gólem del Yunque',       'Yunque Fragmentado',       NULL, NULL, NULL, NULL),
        ('Minas del Yunque',     'hunt', 'Excavador Profundo',     'Gema en Bruto',            NULL, NULL, NULL, NULL),
        ('Minas del Yunque',     'boss', 'Capataz de Hierro',      'Yunque del Capataz',       NULL, NULL, NULL, NULL),
        -- Zona 4: Cordillera del Fuego
        ('Cordillera del Fuego', 'hunt', 'Salamandra Infernal',    'Escama Ígnea',             NULL, NULL, NULL, NULL),
        ('Cordillera del Fuego', 'hunt', 'Coloso de Magma',        'Núcleo de Magma',          NULL, NULL, NULL, NULL),
        ('Cordillera del Fuego', 'boss', 'Señor del Volcán',       'Brasa Eterna',             NULL, NULL, NULL, NULL),
        -- Zona 5: Cráter de la Escoria
        ('Cráter de la Escoria', 'hunt', 'Devorador de Almas',     'Fragmento de Alma',        NULL, NULL, NULL, NULL),
        ('Cráter de la Escoria', 'hunt', 'Titán de Escoria',       'Corazón de Titán',         NULL, NULL, NULL, NULL),
        ('Cráter de la Escoria', 'boss', 'Soberano de la Escoria', 'Corazón del Soberano',     NULL, NULL, NULL, NULL);

    -- 3) Aplicarlo: stats (si vienen) y drops reemplazados por exactamente uno.
    FOR t IN SELECT * FROM roster LOOP
        SELECT zone_id INTO v_zone_id FROM zones WHERE name = t.zone_name;
        IF v_zone_id IS NULL THEN
            RAISE EXCEPTION 'finalize_monster_roster: no existe la zona "%"', t.zone_name;
        END IF;

        SELECT monster_id INTO v_monster_id FROM monsters
        WHERE name = t.name AND zone_id = v_zone_id AND is_boss = (t.kind = 'boss') AND NOT is_travel;
        IF v_monster_id IS NULL THEN
            RAISE EXCEPTION 'finalize_monster_roster: no existe el monstruo "%" (%) en "%"', t.name, t.kind, t.zone_name;
        END IF;

        SELECT item_id INTO v_item_id FROM items WHERE name = t.drop_name;
        IF v_item_id IS NULL THEN
            RAISE EXCEPTION 'finalize_monster_roster: no existe el material "%" que suelta %', t.drop_name, t.name;
        END IF;

        IF t.min_hp IS NOT NULL THEN
            UPDATE monsters SET min_hp = t.min_hp, max_hp = t.max_hp, min_damage = t.min_damage, max_damage = t.max_damage
            WHERE monster_id = v_monster_id;
        END IF;

        DELETE FROM monster_drops WHERE monster_id = v_monster_id;
        INSERT INTO monster_drops (monster_id, item_id) VALUES (v_monster_id, v_item_id);
    END LOOP;

    -- 4) Verificación: no sobra ningún monstruo de /hunt ni jefe que no esté en la lista...
    SELECT string_agg(z.name || ': ' || m.name, ', ') INTO v_extra
    FROM monsters m JOIN zones z ON z.zone_id = m.zone_id
    WHERE NOT m.is_travel
      AND NOT EXISTS (SELECT 1 FROM roster r WHERE r.name = m.name AND r.zone_name = z.name);
    IF v_extra IS NOT NULL THEN
        RAISE EXCEPTION 'finalize_monster_roster: hay monstruos de /hunt o jefes que no están en el plantel: [%]. Agregalos acá o borralos.', v_extra;
    END IF;

    -- ... y todos los monstruos (incluidos los de /travel) terminaron con exactamente un drop.
    SELECT string_agg(m.name || ' (' || (SELECT COUNT(*) FROM monster_drops d WHERE d.monster_id = m.monster_id) || ')', ', ') INTO v_bad
    FROM monsters m
    WHERE (SELECT COUNT(*) FROM monster_drops d WHERE d.monster_id = m.monster_id) <> 1;
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'finalize_monster_roster: estos monstruos no tienen exactamente 1 drop: [%]', v_bad;
    END IF;
END $$;

-- Chequeo rápido: qué suelta cada monstruo, por zona.
-- SELECT z.min_level, CASE WHEN m.is_boss THEN 'jefe' WHEN m.is_travel THEN 'travel' ELSE 'hunt' END AS tipo, m.name, i.name AS suelta
-- FROM monsters m JOIN zones z USING (zone_id) JOIN monster_drops d USING (monster_id) JOIN items i USING (item_id)
-- ORDER BY z.min_level, m.is_boss, m.is_travel, m.name;
