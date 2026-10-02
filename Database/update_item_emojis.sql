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
-- =========================================================

ALTER TABLE items ADD COLUMN IF NOT EXISTS emoji VARCHAR(100);

-- 🪵 Madera (5/5 completo)
UPDATE items SET emoji = '<:maderacomun:1545190666122960906>'      WHERE name = 'Madera de Pino';
UPDATE items SET emoji = '<:maderarara:1545190728970407998>'       WHERE name = 'Madera de Roble';
UPDATE items SET emoji = '<:maderaepica:1545190573957447700>'      WHERE name = 'Madera de Nogal';
UPDATE items SET emoji = '<:maderalegendaria:1545190639715745832>' WHERE name = 'Madera de Ébano';
UPDATE items SET emoji = '<:MADERADELAVIDA_MITICO:1545413424970727516>' WHERE name = 'Corteza del Árbol de Vida';

-- ⛏️ Mineral (6/6 completo)
UPDATE items SET emoji = '<:PIEDRA_COMUN2:1545413503408541726>'     WHERE name = 'Piedra';
UPDATE items SET emoji = '<:CARBON_COMUN:1545413367852826624>'      WHERE name = 'Carbón';
UPDATE items SET emoji = '<:HIERRO_RARO:1545413396793401465>'       WHERE name = 'Hierro';
UPDATE items SET emoji = '<:ORO_EPICO:1545413475462021160>'         WHERE name = 'Oro Puro';
UPDATE items SET emoji = '<:ZAFIRO_LEGENDARIO:1545413533657866280>' WHERE name = 'Gema de Zafiro';
UPDATE items SET emoji = '<:METEORITO_MITICO:1545413451487117412>'  WHERE name = 'Fragmento de Meteorito';

-- 🍖 Consumable (6/6 completo — el catálogo quedó en 6 comidas, ver Database/rework_food_catalog.sql). El emoji de Pan Casero
-- (pan_comun) y el de Choripán (chori_epicos) quedaron sin uso: esas comidas ya no existen. El de Cordero Patagónico (cordero_epico) se
-- borró de Discord, así que ahora usa el del filete asado (fileteasado_epico), que se liberó cuando se sacó Vacío al Disco.
UPDATE items SET emoji = '<:mate_comun:1545473051347521596>'      WHERE name = 'Mate Amargo';
UPDATE items SET emoji = '<:empanada_raro1:1545473013385011220>'  WHERE name = 'Empanada de Carne';
UPDATE items SET emoji = '<:asado_epico:1545472928009949184>'     WHERE name = 'Asado de Tira';
UPDATE items SET emoji = '<:fileteasado_epico:1545472954400247908>' WHERE name = 'Cordero Patagónico';
UPDATE items SET emoji = '<:asado_mitico:1545472835537866853>'    WHERE name = 'Asado Completo del Domingo en Familia';
UPDATE items SET emoji = '<:mate_mitico:1545472874863665214>'     WHERE name = 'Mate Dulce de la Abuela';

-- 📦 Cajas (5/5): un cofre por rareza
UPDATE items SET emoji = '<:cofre_comun:1555258322842681404>'      WHERE name = 'Cajón de Pino';
UPDATE items SET emoji = '<:cofre_raro:1555258292157161614>'       WHERE name = 'Baúl de Roble';
UPDATE items SET emoji = '<:cofre_epico:1555258210649374831>'      WHERE name = 'Arcón de Hierro';
UPDATE items SET emoji = '<:cofre_legendario:1555258185357590709>' WHERE name = 'Cofre de Oro';
UPDATE items SET emoji = '<:cofre_mitico:1555258258191548526>'     WHERE name = 'Arca del Soberano';

-- 🩸 Material (22/38): 21 son drops de monstruos que entran en recetas y 17 son "trofeos" que solo salen de cajas.
-- Faltan 8 drops de recetas (Colmillo de Jabalí, Ceniza Bendita, Esencia Espectral, Garra de Puma Cenizo, Pelaje Plateado del Alfa,
-- Yunque del Capataz, Brasa Eterna, Corazón del Soberano) y 8 trofeos (Garra Maldita, Hueso Añejo, Rama Carbonizada, Garra del Alfa,
-- Núcleo Ígneo, Colmillo del Señor del Volcán, Martillo del Capataz, Corona de Escoria Viva).
UPDATE items SET emoji = '<:drop_comunes_colmillo:1546927649635700808>' WHERE name = 'Colmillo de Cimarrón';
UPDATE items SET emoji = '<:Drops_comunes_cuero:1546927605221949440>'   WHERE name = 'Cuero Grueso';
UPDATE items SET emoji = '<:drops_comunes_pluma:1546922505216721056>' WHERE name = 'Pluma de Ñandú';
UPDATE items SET emoji = '<:drops_raros_colmilloreyjabali:1546922446412841021>' WHERE name = 'Colmillo del Rey Jabalí';
UPDATE items SET emoji = '<:drops_epicos_yunque:1546922620866396210>' WHERE name = 'Yunque Fragmentado';
UPDATE items SET emoji = '<:drops_epicos_piedraderretida:1546922595545256037>' WHERE name = 'Escoria Metálica Densa';
UPDATE items SET emoji = '<:drops_epicos_gema:1546922552038006834>' WHERE name = 'Gema en Bruto';
UPDATE items SET emoji = '<:drops_legendarios_nucleo:1546922421636964414>' WHERE name = 'Núcleo de Magma';
UPDATE items SET emoji = '<:drops_legendarios_escamas:1546922402489827418>' WHERE name = 'Escama Ígnea';
UPDATE items SET emoji = '<:drops_legendarios_alientofuego:1546922363659092101>' WHERE name = 'Aliento de Fuego Eterno';
UPDATE items SET emoji = '<:drop_miticos_fragmentoalma:1546922343392084008>' WHERE name = 'Fragmento de Alma';
UPDATE items SET emoji = '<:drop_miticos_corazontitan:1546922316733358190>' WHERE name = 'Corazón de Titán';
UPDATE items SET emoji = '<:drop_miticos_ceniza:1546922256071135313>' WHERE name = 'Ceniza del Abismo';
UPDATE items SET emoji = '<:drop_comunes_collarCuero:1546927625883353230>' WHERE name = 'Collar de Cuero Viejo';
UPDATE items SET emoji = '<:drops_comunes_cuerocurtido:1546922484912099408>' WHERE name = 'Cuero Curtido de Pradera';
UPDATE items SET emoji = '<:Drops_comunes_pelajeoscuro:1546927563828498553>' WHERE name = 'Pelaje Oscuro';
UPDATE items SET emoji = '<:Drops_comunes_piedracaliente:1546927545835069480>' WHERE name = 'Piedra Caliente';
UPDATE items SET emoji = '<:Drops_comunes_telarasgada:1546927525957992530>' WHERE name = 'Tela Rasgada';
UPDATE items SET emoji = '<:drops_raros_coronacerdas:1546922464741826640>' WHERE name = 'Corona de Cerdas';
UPDATE items SET emoji = '<:drops_epicos_polvo:1546922574594838528>' WHERE name = 'Polvo de Mina Sagrada';
UPDATE items SET emoji = '<:drops_legendarios_rocavolcanica:1546922383640625304>' WHERE name = 'Roca Volcánica Pura';
UPDATE items SET emoji = '<:drop_miticos_escoriacrater:1546922285636784168>' WHERE name = 'Escoria Pura del Cráter';

-- ⚔️ Weapon (0/28, pendiente)

-- 📿 Amulet (2/16; 6 de los que faltan no se pueden conseguir hoy: no tienen receta ni están en cajas)
UPDATE items SET emoji = '<:amuleto_hombreras:1546922706497445948>' WHERE name = 'Hombreras de Cuero Grueso';
UPDATE items SET emoji = '<:amuleto_collar_basico:1546922662813765672>' WHERE name = 'Amuleto del Levantador';

-- Chequeo rápido: cuántos ítems totales todavía están en NULL (arrancó en 58, iba bajando de a tanda).
-- SELECT type, COUNT(*) FROM items WHERE emoji IS NULL GROUP BY type ORDER BY type;
