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
-- nombre claro, pedirle al bot la lista (o copiar el id) y agregar el UPDATE acá. Desde 2026-10-05 YA NO HAY excepciones: las 4 maderas también
-- pasaron a la aplicación (antes eran del servidor). Las caras de los monstruos van aparte: Database/update_monster_portraits.sql.
-- =========================================================

ALTER TABLE items ADD COLUMN IF NOT EXISTS emoji VARCHAR(100);

-- 🪵 Madera (5/5, todas de la aplicación). Las 4 comunes se rehicieron el 2026-10-05 con el aura de su rareza más marcada (el Roble, azul con estrellitas); los viejos
-- ids del servidor (maderacomun, maderarara, maderaepica, maderalegendaria) ya no se usan.
UPDATE items SET emoji = '<:madera_pino:1556678837482422392>' WHERE name = 'Madera de Pino';
UPDATE items SET emoji = '<:madera_roble:1556678835339006023>' WHERE name = 'Madera de Roble';
UPDATE items SET emoji = '<:madera_nogal:1556678833569275914>' WHERE name = 'Madera de Nogal';
UPDATE items SET emoji = '<:madera_ebano:1556678831841091715>' WHERE name = 'Madera de Ébano';
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

-- 🩸 Material — drops de monstruo (los 15 de las zonas) y trofeos de caja (32/32 completo). El rework de la v0.7.0 retiró 6 (Colmillo de Cimarrón y los 5 drops de jefe): sus emojis quedan libres en el portal (drop_comunes_colmillo, drops_raros_colmilloreyjabali, pelaje_plateado_alfa, forja_hierro, brasa_eterna, corazon_del_soberano)
UPDATE items SET emoji = '<:drop_comunes_collarCuero:1555578362704036010>' WHERE name = 'Collar de Cuero Viejo';
UPDATE items SET emoji = '<:drops_comunes_cuerocurtido:1555578412968579184>' WHERE name = 'Cuero Curtido de Pradera';
UPDATE items SET emoji = '<:Drops_comunes_cuero:1555578359881146388>' WHERE name = 'Cuero Grueso';
UPDATE items SET emoji = '<:Drops_comunes_pelajeoscuro:1555578358153347132>' WHERE name = 'Pelaje Oscuro';
UPDATE items SET emoji = '<:Drops_comunes_piedracaliente:1555578355686965420>' WHERE name = 'Piedra Caliente';
UPDATE items SET emoji = '<:drops_comunes_pluma:1555578415502065865>' WHERE name = 'Pluma de Ñandú';
UPDATE items SET emoji = '<:Drops_comunes_telarasgada:1555578353627570186>' WHERE name = 'Tela Rasgada';
UPDATE items SET emoji = '<:Ceniza_bendita:1555578250485309592>' WHERE name = 'Ceniza Bendita';
UPDATE items SET emoji = '<:colmillo_jabali:1555578251605442581>' WHERE name = 'Colmillo de Jabalí';
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
UPDATE items SET emoji = '<:drops_epicos_polvo:1555578420954669086>' WHERE name = 'Polvo de Mina Sagrada';
UPDATE items SET emoji = '<:drops_epicos_yunque:1555578425467736235>' WHERE name = 'Yunque Fragmentado';
UPDATE items SET emoji = '<:drops_legendarios_alientofuego:1555578390256549989>' WHERE name = 'Aliento de Fuego Eterno';
UPDATE items SET emoji = '<:colmillo_seor_volcan:1555593542905110548>' WHERE name = 'Colmillo del Señor del Volcán';
UPDATE items SET emoji = '<:drops_legendarios_escamas:1555578393498620024>' WHERE name = 'Escama Ígnea';
UPDATE items SET emoji = '<:martillo_del_capataz:1555593537465229393>' WHERE name = 'Martillo del Capataz';
UPDATE items SET emoji = '<:drops_legendarios_nucleo:1555578395364958248>' WHERE name = 'Núcleo de Magma';
UPDATE items SET emoji = '<:drops_legendarios_rocavolcanica:1555578391779082351>' WHERE name = 'Roca Volcánica Pura';
UPDATE items SET emoji = '<:drop_miticos_ceniza:1555578376440250459>' WHERE name = 'Ceniza del Abismo';
UPDATE items SET emoji = '<:drop_miticos_corazontitan:1555578382857674836>' WHERE name = 'Corazón de Titán';
UPDATE items SET emoji = '<:corona_escoriaviva:1555593539260391445>' WHERE name = 'Corona de Escoria Viva';
UPDATE items SET emoji = '<:drop_miticos_escoriacrater:1555578379271409715>' WHERE name = 'Escoria Pura del Cráter';
UPDATE items SET emoji = '<:drop_miticos_fragmentoalma:1555578387827785818>' WHERE name = 'Fragmento de Alma';

-- ⚔️ Weapon (25/28): las 5 zonas completas (las 25 armas que se consiguen forjando). Sin emoji y sin forma de conseguirlas: Arco Largo del Cazador, Báculo del Aprendiz, Dagas Gemelas de Sombra
UPDATE items SET emoji = '<:espada_madera:1555633462432636958>' WHERE name = 'Espada de Madera';
UPDATE items SET emoji = '<:daga_oxidada:1555633460947984394>' WHERE name = 'Daga Oxidada';
UPDATE items SET emoji = '<:arco_corto_sauce:1555633459232510022>' WHERE name = 'Arco Corto de Sauce';
UPDATE items SET emoji = '<:grimorio_desgastado:1555633457428955176>' WHERE name = 'Grimorio Desgastado';
UPDATE items SET emoji = '<:hoja_acero_pura:1555633455889387730>' WHERE name = 'Hoja de Acero Puro';
UPDATE items SET emoji = '<:hacha_mk3:1555636580667105330>' WHERE name = 'Hacha de Hierro MK3';
UPDATE items SET emoji = '<:colmillo_nocturno:1555636579047833610>' WHERE name = 'Colmillo Nocturno';
UPDATE items SET emoji = '<:arco_elfico_ancestral:1555636576665604106>' WHERE name = 'Arco Élfico Ancestral';
UPDATE items SET emoji = '<:grimorio_tormentas:1555636574740422746>' WHERE name = 'Grimorio de las Tormentas';
UPDATE items SET emoji = '<:cuchilla_cenizas:1555636572970291282>' WHERE name = 'Cuchilla de Cenizas';
UPDATE items SET emoji = '<:mazo_escoria:1555644053788299274>' WHERE name = 'Mazo de Escoria';
UPDATE items SET emoji = '<:daga_guerra_maldita:1555644052026691594>' WHERE name = 'Dagas de Garra Maldita';
UPDATE items SET emoji = '<:boleadoras_escoria:1555644050281992242>' WHERE name = 'Boleadoras de Escoria';
UPDATE items SET emoji = '<:baculo_tizon:1555644047610347663>' WHERE name = 'Báculo de Tizón';
UPDATE items SET emoji = '<:pico_minero_reforzado:1555644045903265813>' WHERE name = 'Pico de Minero Reforzado';
-- Zona 4 (Cordillera del Fuego) y Zona 5 (Cráter de la Escoria), cargadas el 2026-10-05
UPDATE items SET emoji = '<:facon_hueso_anejo:1556675307841388586>' WHERE name = 'Facón de Hueso Añejo';
UPDATE items SET emoji = '<:cuchillos_ceniza:1556675305614221332>' WHERE name = 'Cuchillos de Ceniza';
UPDATE items SET emoji = '<:arco_caza_mayor:1556675303965724772>' WHERE name = 'Arco de Caza Mayor';
UPDATE items SET emoji = '<:codice_brasas:1556675302229413939>' WHERE name = 'Códice de las Brasas';
UPDATE items SET emoji = '<:lanza_magma:1556675300497162332>' WHERE name = 'Lanza de Magma';
UPDATE items SET emoji = '<:espada_abismo:1556675298660196484>' WHERE name = 'Espada del Abismo';
UPDATE items SET emoji = '<:colmillo_crater:1556675296768303135>' WHERE name = 'Colmillo del Cráter';
UPDATE items SET emoji = '<:arco_alma_errante:1556675294918746243>' WHERE name = 'Arco del Alma Errante';
UPDATE items SET emoji = '<:baculo_arbol_vida:1556675289960943786>' WHERE name = 'Báculo del Árbol de Vida';
UPDATE items SET emoji = '<:martillo_titan:1556675288442609747>' WHERE name = 'Martillo del Titán';

-- 📿 Amulet (7/11): los 5 que se consiguen forjando (uno por zona) + Capa y Carcaj de Pelaje Oscuro (hoy no se consiguen, pero el emoji ya estaba hecho). Sin emoji y sin forma de conseguirlos: Bombilla de Hierro Maldito, Botas de Silencio, Collar de Hueso, Ojo de Jabalí. Los 5 amuletos "bajos" salieron en la v0.7.0 (emojis libres: amuleto_collar_basico, mate_ceniza, casco_capataz, coraza_escamas_ignea, egidia_deborador)
UPDATE items SET emoji = '<:amuleto_hombreras:1555578432006652005>' WHERE name = 'Hombreras de Cuero Grueso';
UPDATE items SET emoji = '<:talizman_ceniza:1555615173484748860>' WHERE name = 'Talismán de Ceniza Bendita';
UPDATE items SET emoji = '<:peto_escoria_templaria:1555615171689455676>' WHERE name = 'Peto de Escoria Templada';
UPDATE items SET emoji = '<:talizman_volcan:1555616984899784774>' WHERE name = 'Talismán del Volcán';
UPDATE items SET emoji = '<:corazon_titan_acorazado:1555616981447614514>' WHERE name = 'Corazón de Titán Engarzado';
UPDATE items SET emoji = '<:amuleto_capa:1555578429959577601>' WHERE name = 'Capa de Pelaje Oscuro';
UPDATE items SET emoji = '<:amuleto_flechas:1555578434606997564>' WHERE name = 'Carcaj de Pelaje Oscuro';

-- Chequeo rápido: cuántos ítems totales todavía están en NULL, por tipo.
-- SELECT type, COUNT(*) FROM items WHERE emoji IS NULL GROUP BY type ORDER BY type;
