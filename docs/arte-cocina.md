# Arte de la cocina: los 10 platos (prompts para Bing Image Creator)

Cada imagen se sube al Developer Portal (la aplicación del bot → Emojis) con el **nombre de emoji** de la tabla, EXACTO. A diferencia de los ítems, **no hace falta pasar ids ni correr SQL**: los platos no son ítems de la base, el bot lee sus emojis por nombre (como los del dragón y los de la interfaz, `UiEmojis.Icon`) al arrancar y cada 10 minutos. Hasta que estén subidos, la pantalla usa un 🍳 / 🐟 común.

Estilo (preferencia del dueño, 2026-10-28): ilustración digital limpia, **NO pixel art**, bordes suaves, colores saturados, **fondo blanco liso (#FFFFFF)**, sin sombra ni degradado, objeto centrado y grande. Pedí 4 variantes y elegí la que se lea mejor chiquito (Discord la muestra a 22 px en el texto): recortá el fondo y bajala a 128×128.

Base (va al principio de cada prompt):
`A single food icon for a fantasy RPG, clean digital illustration (NOT pixel art), smooth bold outlines, saturated colors, centered and large, plain pure white background (#FFFFFF), no shadow, no gradient, no text, no frame, square 1:1,`

Los tres primeros (los permanentes que se apilan) son los que más se van a ver: cada uno tiene **su silueta y su color**, para reconocerlos aun a 22 px (disco redondo y rojo/hierro; pez largo en diagonal y dorado; frasco alto y verde oliva).

| Emoji | Plato | Prompt (después de la base) |
|---|---|---|
| `plato_surubi_disco` | Surubí al Disco (+10 vida máx) | a wide round concave iron plow disc used as a pan, glowing red embers under it, thick pink-white catfish steaks cooking in the middle with potato chunks and orange bell pepper slices, rising steam, dark iron and warm red palette, round silhouette |
| `plato_dorado_parrilla` | Dorado a la Parrilla (+1 DEF) | a whole golden dorado fish with shining gold-amber scales lying diagonally across a black iron grill rack, charred grill marks, small flames and glowing charcoal below, long diagonal silhouette, gold and amber palette |
| `plato_mojarras_escabeche` | Mojarras en Escabeche (+2 ATQ) | a tall glass preserving jar sealed with a cork, filled with small silver fish layered with onion rings, carrot slices, bay leaves and peppercorns in golden vinegar, olive green glass tint, tall jar silhouette, green yellow and silver palette |
| `plato_empanada_surubi` | Empanada de Surubí (+20 % oro) | a golden-baked half-moon empanada with a neatly braided crimped edge, a small fish tail shape pressed into the crust, a few steam wisps and one tiny sparkling gold coin beside it, warm brown and gold palette |
| `plato_locro_pescador` | Locro del Pescador (+20 % EXP) | a rustic terracotta clay pot full of thick orange-yellow stew with corn kernels, squash cubes and chunks of white fish, a wooden ladle resting in it, steam rising, terracotta and orange palette |
| `plato_chupin_dorado` | Chupín de Dorado (+15 % drop) | a shiny copper two-handled cooking pot full of red tomato fish soup, a golden dorado fish head and a tail sticking out, green parsley on top, steam, copper and tomato red palette |
| `plato_pastel_mojarra` | Pastel de Mojarra (+25 % cantidad) | a round golden-brown pie with a lattice crust and tiny fish tails poking out of the slits, one slice cut out showing creamy filling, on a blue enamel tin plate, golden crust and blue plate |
| `plato_brocheta_dorado` | Brocheta de Dorado (+10 crítico) | a long wooden skewer set diagonally with chunks of golden dorado fish, red bell pepper, green bell pepper and onion, lightly charred and glistening, a few sparks, colorful palette |
| `plato_surubi_manteca` | Surubí a la Manteca (+10 esquive) | a seared white fish fillet in a small black skillet with melting yellow butter, a lemon wedge and fresh green parsley, light steam, black skillet with butter yellow and lemon palette |
| `plato_caldo_ultimo_aliento` | Caldo del Último Aliento (último aliento) | a small black iron cauldron full of dark teal glowing broth, a pale cyan ghostly flame rising from it shaped like a small phoenix, faint sparkles, mystical dark and cyan palette |
