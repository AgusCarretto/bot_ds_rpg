-- =========================================================
-- La CARA de cada monstruo (v0.8.2): un emoji de la APLICACIÓN (Discord Developer Portal > la aplicación del bot > Emojis, igual que los
-- de los ítems, ver update_item_emojis.sql) que se muestra como miniatura de los mensajes de combate (/hunt, /travel, /boss, /raid, /autohunt).
-- Es una columna APARTE de monsters.emoji a propósito: ese es el emoji unicode que los mensajes escriben en el texto ("¡Un Jabalí 🐗 aparece!"),
-- y esta es la imagen grande de la miniatura (GameData/ItemDisplay.ImageUrl arma la URL del CDN a partir del código "<:nombre:id>").
--
-- Auto-contenido y re-ejecutable: agrega la columna si falta y carga las 20 caras POR NOMBRE (monsters.name es UNIQUE). Falla en voz alta si
-- alguno de los 20 monstruos de la lista no existe (una cara que no se cargó se vería como "no pasó nada"). Va DESPUÉS de los scripts que
-- crean los monstruos (rework_drops_and_recipes.sql) y de update_item_emojis.sql. Un monstruo nuevo sin cara no rompe nada: el mensaje sale
-- sin miniatura. Para sumar uno: subir la imagen al portal, y agregar su fila a la lista de abajo.
-- =========================================================

ALTER TABLE monsters ADD COLUMN IF NOT EXISTS portrait_emoji VARCHAR(100);

CREATE TEMP TABLE mp_faces (monster_name TEXT, portrait TEXT);
INSERT INTO mp_faces VALUES
    -- Zona 1: Praderas del Mate
    ('Jabalí Rabioso',         '<:zona1_jabali:1556680082343460985>'),
    ('Ñandú Salvaje',          '<:zona1_andu:1556680077649911869>'),
    ('Toro Bravo',             '<:zona1_toro:1556680075817259108>'),
    ('Rey Jabalí',             '<:zona1_reyjabali:1556680072222736585>'),
    -- Zona 2: Bosque de Cenizas
    ('Espíritu del Monte',     '<:zona2_espiritu:1556681290277720084>'),
    ('Puma de las Cenizas',    '<:zona2_puma:1556681288210055308>'),
    ('Ciervo Sagrado',         '<:zona2_siervo:1556681286515560499>'),
    ('Lobisón Alfa',           '<:zona2_reyLOBO:1556681284862877737>'),
    -- Zona 3: Minas del Yunque
    ('Excavador Profundo',     '<:zona3_topo:1556682150114365540>'),
    ('Gólem del Yunque',       '<:zona3_golem:1556682148512010350>'),
    ('Mole de Escoria',        '<:zona3_mole:1556682147010584710>'),
    ('Capataz de Hierro',      '<:zona3_reycapataz:1556682145416740964>'),
    -- Zona 4: Cordillera del Fuego
    ('Coloso de Magma',        '<:zona4_coloso_de_magma:1556683367733264484>'),
    ('Salamandra Infernal',    '<:zona4_salamandra_infernal:1556683354512949418>'),
    ('Dragón de Lava',         '<:zona4_ragon_de_lava:1556683352675983380>'),
    ('Señor del Volcán',       '<:zona4_senor_del_volcan:1556683350792601760>'),
    -- Zona 5: Cráter de la Escoria
    ('Devorador de Almas',     '<:zona_5_devorador_de_almas:1556687063972061265>'),
    ('Titán de Escoria',       '<:zona_5_titan_de_escoria:1556687062143336558>'),
    ('Quimera del Abismo',     '<:zona_5_quimera_del_abismo:1556687060591444149>'),
    ('Soberano de la Escoria', '<:zona_5_soberano_de_la_escoria:1556687058809127062>');

DO $$
DECLARE
    v_missing TEXT;
BEGIN
    SELECT string_agg(f.monster_name, ', ') INTO v_missing
    FROM mp_faces f WHERE NOT EXISTS (SELECT 1 FROM monsters m WHERE m.name = f.monster_name);
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'update_monster_portraits: estos monstruos no existen en la base: [%]. ¿Se corrió antes rework_drops_and_recipes.sql?', v_missing;
    END IF;
END $$;

UPDATE monsters m
SET portrait_emoji = f.portrait
FROM mp_faces f
WHERE m.name = f.monster_name;

DROP TABLE mp_faces;

-- Chequeo rápido: monstruos que todavía no tienen cara (debería salir vacío).
-- SELECT name FROM monsters WHERE portrait_emoji IS NULL ORDER BY monster_id;
