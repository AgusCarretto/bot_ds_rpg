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

-- 🩸 Material — drops de monstruo (0/8, pendiente)
-- 🍖 Consumable (0/10, pendiente)
-- ⚔️ Weapon base + de clase (0/20, pendiente)
-- 📿 Amulet (0/9, pendiente)

-- Chequeo rápido: cuántos ítems totales todavía están en NULL (arrancó en 58, iba bajando de a tanda).
-- SELECT type, COUNT(*) FROM items WHERE emoji IS NULL GROUP BY type ORDER BY type;
