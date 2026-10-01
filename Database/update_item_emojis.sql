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

-- 🩸 Material (0/38, pendiente): 21 son drops de monstruos que entran en recetas y 17 son "trofeos" que solo salen de cajas.
-- ⚔️ Weapon (0/29, pendiente)
-- 📿 Amulet (0/16, pendiente; 6 de ellos no se pueden conseguir hoy: no tienen receta ni están en cajas)

-- Chequeo rápido: cuántos ítems totales todavía están en NULL (arrancó en 58, iba bajando de a tanda).
-- SELECT type, COUNT(*) FROM items WHERE emoji IS NULL GROUP BY type ORDER BY type;
