-- =========================================================
-- La CARA de cada monstruo (v0.8.2): un emoji de la APLICACIÓN (Discord Developer Portal > la aplicación del bot > Emojis, igual que los
-- de los ítems, ver update_item_emojis.sql) que se muestra como miniatura de los mensajes de combate (/hunt, /travel, /boss, /raid, /autohunt).
-- Es una columna APARTE de monsters.emoji a propósito: ese es el emoji unicode que los mensajes escriben en el texto ("¡Un Jabalí 🐗 aparece!"),
-- y esta es la imagen grande de la miniatura (GameData/ItemDisplay.ImageUrl arma la URL del CDN a partir del código "<:nombre:id>").
--
-- Auto-contenido y re-ejecutable: agrega la columna si falta y carga las 20 caras de la escalera POR NOMBRE (monsters.name es UNIQUE), la del Asador Eterno y las
-- de los 10 monstruos de las Zonas 6 a 10 (ver abajo). Falla en voz alta si alguno de los 20 monstruos de la lista no existe (una cara que no se cargó se vería como "no pasó nada"). Va DESPUÉS de los scripts que
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

-- La cara de El Asador Eterno (2026-10-09, el retrato `asadorfinal` del dueño). Va APARTE de la lista de arriba a propósito: ese monstruo lo crea seed_fogon.sql,
-- que en una instalación nueva corre DESPUÉS de este script, así que exigir que exista haría fallar la instalación. run_fresh_install.sql corre este script
-- otra vez al final (cuando ya existe) y en una base viva está desde la v0.11.0.
UPDATE monsters SET portrait_emoji = '<:asadorfinal:1558168786677403709>' WHERE name = 'El Asador Eterno';

-- Las caras de los 10 monstruos de las Zonas 6 a 10 (2026-11-04, arte del dueño: cada uno con el escenario de su zona de fondo). Igual que el Asador, van APARTE de la
-- lista verificada de arriba: add_zones_6_10.sql crea esos monstruos DESPUÉS de la primera corrida de este script en run_fresh_install.sql, y exigir que existan
-- haría fallar la instalación; la corrida del final (cuando ya existen) es la que las carga. En una base viva donde todavía no se corrió add_zones_6_10.sql
-- este UPDATE no encuentra nada y no pasa nada. Los nombres de emoji son los que el dueño subió al portal tal cual (con el "yaguarate" sin tilde y "Sombra_del_Monte"
-- con mayúscula): el portal le pone a cada emoji el nombre de su archivo.
UPDATE monsters m
SET portrait_emoji = f.portrait
FROM (VALUES
    -- Zona 6: Salinas del Olvido
    ('Guanaco de Sal',     '<:guanaco_de_sal:1567537057176354867>'),
    ('Coloso de Sal',      '<:coloso_de_sal:1567537058703081634>'),
    -- Zona 7: Selva de Humo
    ('Yaguareté de Humo',  '<:yaguarate_de_humo:1567537060896702526>'),
    ('Sombra del Monte',   '<:Sombra_del_Monte:1567537055456567397>'),
    -- Zona 8: Pantano de Brea
    ('Sapo de Brea',       '<:sapo_de_brea:1567537053690892418>'),
    ('Bestia del Pantano', '<:bestia_del_pantano:1567537051354534078>'),
    -- Zona 9: Glaciar del Silencio
    ('Lobo de Hielo',      '<:lobo_de_hielo:1567537049798713497>'),
    ('Gigante Escarchado', '<:gigante_de_hielo:1567537047957143712>'),
    -- Zona 10: Cumbre del Rayo
    ('Cóndor del Rayo',    '<:condor_rayo:1567537046380224552>'),
    ('Titán de Tormenta',  '<:titan_de_la_tormenta:1567537044723343390>')
) AS f(monster_name, portrait)
WHERE m.name = f.monster_name;

-- Chequeo rápido: monstruos que todavía no tienen cara (debería salir vacío).
-- SELECT name FROM monsters WHERE portrait_emoji IS NULL ORDER BY monster_id;
