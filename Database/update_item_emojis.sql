-- =========================================================
-- Emojis personalizados de cada ítem — UN SOLO archivo, se va actualizando acá mismo cada vez que
-- hay códigos nuevos (no se crean archivos "batchN" nuevos). Auto-contenido: agrega la columna si
-- todavía no existe en esta base (algunas máquinas la tienen de schema.sql, otras no) y después
-- carga todos los códigos conocidos hasta ahora.
--
-- Ejecutable las veces que haga falta: el ADD COLUMN es IF NOT EXISTS, y cada UPDATE es por
-- nombre (no por item_id, que varía entre instalaciones) así que sobreescribe con el mismo valor
-- si ya estaba cargado, no hay riesgo de duplicar nada.
--
-- Formato del código: "<:nombre_emoji:1234567890>" (o "<a:nombre:id>" si es animado) tal cual lo
-- da Discord al subirlo — NO el emoji unicode. GameData/ItemDisplay.cs cae a mostrar solo el
-- nombre del ítem para cualquiera que siga en NULL.
--
-- EMOJIS DE LA APLICACIÓN (desde 2026-10-02): casi todos viven en el Discord Developer Portal > la aplicación del bot > Emojis, NO en el
-- servidor. No gastan los 50 slots del servidor (la aplicación admite hasta 2000) y se ven en cualquier servidor donde esté el bot (los del
-- servidor solo si el bot comparte servidor con ese emoji). El código es el mismo "<:nombre:id>". Para sumar uno: subirlo en el portal con un
-- nombre claro, pedirle al bot la lista (o copiar el id) y agregar el UPDATE acá. Las únicas excepciones siguen siendo las 4 maderas.
-- =========================================================

ALTER TABLE items ADD COLUMN IF NOT EXISTS emoji VARCHAR(100);

-- 🪵 Madera (5/5) — OJO: las 4 maderas comunes siguen siendo emojis del SERVIDOR (ids viejos, no están en la lista de la aplicación); la Corteza del Árbol de Vida ya es de la aplicación
UPDATE items SET emoji = '<:maderacomun:1545190666122960906>' WHERE name = 'Madera de Pino';
UPDATE items SET emoji = '<:maderarara:1545190728970407998>' WHERE name = 'Madera de Roble';
UPDATE items SET emoji = '<:maderaepica:1545190573957447700>' WHERE name = 'Madera de Nogal';
UPDATE items SET emoji = '<:maderalegendaria:1545190639715745832>' WHERE name = 'Madera de Ébano';
UPDATE items SET emoji = '<:MADERADELAVIDA_MITICO:1555578599556517888>' WHERE name = 'Corteza del Árbol de Vida';

-- ⛏️ Mineral (6/6 completo)
UPDATE items SET emoji = '<:PIEDRA_COMUN2:1555578615780085831>' WHERE name = 'Piedra';
UPDATE items SET emoji = '<:CARBON_COMUN:1555578588588277913>' WHERE name = 'Carbón';
UPDATE items SET emoji = '<:HIERRO_RARO:1555578597887189012>' WHERE name = 'Hierro';
UPDATE items SET emoji = '<:ORO_EPICO:1555578608540712970>' WHERE name = 'Oro Puro';
UPDATE items SET emoji = '<:ZAFIRO_LEGENDARIO:1555578618573492316>' WHERE name = 'Gema de Zafiro';
UPDATE items SET emoji = '<:METEORITO_MITICO:1555578605919014992>' WHERE name = 'Fragmento de Meteorito';

-- 🍖 Consumable (6/6 completo — el catálogo quedó en 6 comidas, ver Database/rework_food_catalog.sql). Sin uso en el catálogo: pan_comun, chori_epicos, fileteasado_epico, empanada_raro y PIEDRA_COMUN (quedaron en el portal)
UPDATE items SET emoji = '<:mate_comun:1555578601892749433>' WHERE name = 'Mate Amargo';
UPDATE items SET emoji = '<:empanada_raro1:1555578594342740130>' WHERE name = 'Empanada de Carne';
UPDATE items SET emoji = '<:asado_epico:1555578573878722600>' WHERE name = 'Asado de Tira';
UPDATE items SET emoji = '<:cordero_epico:1555578592661078156>' WHERE name = 'Cordero Patagónico';
UPDATE items SET emoji = '<:asado_mitico:1555578587053293688>' WHERE name = 'Asado Completo del Domingo en Familia';
UPDATE items SET emoji = '<:mate_mitico:1555578604014796870>' WHERE name = 'Mate Dulce de la Abuela';

-- 📦 Cajas (5/5): un cofre por rareza
UPDATE items SET emoji = '<:cofre_comun:1555578513682075809>' WHERE name = 'Cajón de Pino';
UPDATE items SET emoji = '<:cofre_raro:1555578515754061944>' WHERE name = 'Baúl de Roble';
UPDATE items SET emoji = '<:cofre_epico:1555578511996092466>' WHERE name = 'Arcón de Hierro';
UPDATE items SET emoji = '<:cofre_legendario:1555578509601280113>' WHERE name = 'Cofre de Oro';
UPDATE items SET emoji = '<:cofre_mitico:1555578518115590245>' WHERE name = 'Arca del Soberano';

-- 🩸 Material — drops de monstruo y trofeos de caja (38/38 completo). OJO: "Yunque del Capataz" usa el emoji forja_hierro
UPDATE items SET emoji = '<:drop_comunes_colmillo:1555578374712201347>' WHERE name = 'Colmillo de Cimarrón';
UPDATE items SET emoji = '<:drop_comunes_collarCuero:1555578362704036010>' WHERE name = 'Collar de Cuero Viejo';
UPDATE items SET emoji = '<:drops_comunes_cuerocurtido:1555578412968579184>' WHERE name = 'Cuero Curtido de Pradera';
UPDATE items SET emoji = '<:Drops_comunes_cuero:1555578359881146388>' WHERE name = 'Cuero Grueso';
UPDATE items SET emoji = '<:Drops_comunes_pelajeoscuro:1555578358153347132>' WHERE name = 'Pelaje Oscuro';
UPDATE items SET emoji = '<:Drops_comunes_piedracaliente:1555578355686965420>' WHERE name = 'Piedra Caliente';
UPDATE items SET emoji = '<:drops_comunes_pluma:1555578415502065865>' WHERE name = 'Pluma de Ñandú';
UPDATE items SET emoji = '<:Drops_comunes_telarasgada:1555578353627570186>' WHERE name = 'Tela Rasgada';
UPDATE items SET emoji = '<:Ceniza_bendita:1555578250485309592>' WHERE name = 'Ceniza Bendita';
UPDATE items SET emoji = '<:colmillo_jabali:1555578251605442581>' WHERE name = 'Colmillo de Jabalí';
UPDATE items SET emoji = '<:drops_raros_colmilloreyjabali:1555578397831467058>' WHERE name = 'Colmillo del Rey Jabalí';
UPDATE items SET emoji = '<:drops_raros_coronacerdas:1555578411009835139>' WHERE name = 'Corona de Cerdas';
UPDATE items SET emoji = '<:escencia_espectral:1555578248547672187>' WHERE name = 'Esencia Espectral';
UPDATE items SET emoji = '<:garra_puma:1555578246345789522>' WHERE name = 'Garra de Puma Cenizo';
UPDATE items SET emoji = '<:garra_maldita:1555578243883475054>' WHERE name = 'Garra Maldita';
UPDATE items SET emoji = '<:hueso_aejo:1555579310184857621>' WHERE name = 'Hueso Añejo';
UPDATE items SET emoji = '<:rama_carbonizada:1555578239093710848>' WHERE name = 'Rama Carbonizada';
UPDATE items SET emoji = '<:drops_epicos_piedraderretida:1555578423475175444>' WHERE name = 'Escoria Metálica Densa';
UPDATE items SET emoji = '<:garra_alfa:1555580921351110778>' WHERE name = 'Garra del Alfa';
UPDATE items SET emoji = '<:drops_epicos_gema:1555578418114855003>' WHERE name = 'Gema en Bruto';
UPDATE items SET emoji = '<:nucleo_igneo:1555578241815805992>' WHERE name = 'Núcleo Ígneo';
UPDATE items SET emoji = '<:pelaje_plateado_alfa:1555580919929249812>' WHERE name = 'Pelaje Plateado del Alfa';
UPDATE items SET emoji = '<:drops_epicos_polvo:1555578420954669086>' WHERE name = 'Polvo de Mina Sagrada';
UPDATE items SET emoji = '<:drops_epicos_yunque:1555578425467736235>' WHERE name = 'Yunque Fragmentado';
UPDATE items SET emoji = '<:drops_legendarios_alientofuego:1555578390256549989>' WHERE name = 'Aliento de Fuego Eterno';
UPDATE items SET emoji = '<:brasa_eterna:1555580923247071281>' WHERE name = 'Brasa Eterna';
UPDATE items SET emoji = '<:colmillo_seor_volcan:1555593542905110548>' WHERE name = 'Colmillo del Señor del Volcán';
UPDATE items SET emoji = '<:drops_legendarios_escamas:1555578393498620024>' WHERE name = 'Escama Ígnea';
UPDATE items SET emoji = '<:martillo_del_capataz:1555593537465229393>' WHERE name = 'Martillo del Capataz';
UPDATE items SET emoji = '<:drops_legendarios_nucleo:1555578395364958248>' WHERE name = 'Núcleo de Magma';
UPDATE items SET emoji = '<:drops_legendarios_rocavolcanica:1555578391779082351>' WHERE name = 'Roca Volcánica Pura';
UPDATE items SET emoji = '<:forja_hierro:1555580925033848984>' WHERE name = 'Yunque del Capataz';
UPDATE items SET emoji = '<:drop_miticos_ceniza:1555578376440250459>' WHERE name = 'Ceniza del Abismo';
UPDATE items SET emoji = '<:drop_miticos_corazontitan:1555578382857674836>' WHERE name = 'Corazón de Titán';
UPDATE items SET emoji = '<:corazon_del_soberano:1555593540761821265>' WHERE name = 'Corazón del Soberano';
UPDATE items SET emoji = '<:corona_escoriaviva:1555593539260391445>' WHERE name = 'Corona de Escoria Viva';
UPDATE items SET emoji = '<:drop_miticos_escoriacrater:1555578379271409715>' WHERE name = 'Escoria Pura del Cráter';
UPDATE items SET emoji = '<:drop_miticos_fragmentoalma:1555578387827785818>' WHERE name = 'Fragmento de Alma';

-- ⚔️ Weapon (0/28, pendiente: 25 se consiguen forjando y 3 — Arco Largo del Cazador, Báculo del Aprendiz, Dagas Gemelas de Sombra — hoy no se consiguen)

-- 📿 Amulet (12/16): los 10 que se consiguen forjando + Capa y Carcaj de Pelaje Oscuro (hoy no se consiguen, pero el emoji ya estaba hecho). Sin emoji y sin forma de conseguirlos: Bombilla de Hierro Maldito, Botas de Silencio, Collar de Hueso, Ojo de Jabalí
UPDATE items SET emoji = '<:amuleto_collar_basico:1555578428181188749>' WHERE name = 'Amuleto del Levantador';
UPDATE items SET emoji = '<:amuleto_hombreras:1555578432006652005>' WHERE name = 'Hombreras de Cuero Grueso';
UPDATE items SET emoji = '<:mate_ceniza:1555615167629496380>' WHERE name = 'Mate Tallado en Cenizas';
UPDATE items SET emoji = '<:talizman_ceniza:1555615173484748860>' WHERE name = 'Talismán de Ceniza Bendita';
UPDATE items SET emoji = '<:casco_capataz:1555615169189781667>' WHERE name = 'Casco de Capataz';
UPDATE items SET emoji = '<:peto_escoria_templaria:1555615171689455676>' WHERE name = 'Peto de Escoria Templada';
UPDATE items SET emoji = '<:coraza_escamas_ignea:1555616983188508733>' WHERE name = 'Coraza de Escamas Ígneas';
UPDATE items SET emoji = '<:talizman_volcan:1555616984899784774>' WHERE name = 'Talismán del Volcán';
UPDATE items SET emoji = '<:corazon_titan_acorazado:1555616981447614514>' WHERE name = 'Corazón de Titán Engarzado';
UPDATE items SET emoji = '<:egidia_deborador:1555616979845390407>' WHERE name = 'Égida del Devorador';
UPDATE items SET emoji = '<:amuleto_capa:1555578429959577601>' WHERE name = 'Capa de Pelaje Oscuro';
UPDATE items SET emoji = '<:amuleto_flechas:1555578434606997564>' WHERE name = 'Carcaj de Pelaje Oscuro';

-- Chequeo rápido: cuántos ítems totales todavía están en NULL, por tipo.
-- SELECT type, COUNT(*) FROM items WHERE emoji IS NULL GROUP BY type ORDER BY type;
