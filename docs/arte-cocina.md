# Arte de la cocina: los 10 platos (prompts para Bing Image Creator)

Cada imagen se sube al Developer Portal (la aplicación del bot → Emojis) con el **nombre de emoji** de la tabla, EXACTO. A diferencia de los ítems, **no hace falta pasar ids ni correr SQL**: los platos no son ítems de la base, el bot lee sus emojis por nombre (como los del dragón y los de la interfaz, `UiEmojis.Icon`) al arrancar y cada 10 minutos. Hasta que estén subidos, la pantalla usa un 🍳 común.

Estilo (preferencia del dueño, 2026-10-28, y 2026-11-05: «que sean distintivos, colores fuertes»): ilustración digital limpia, **NO pixel art**, **colores muy saturados y de mucho contraste, contornos negros gruesos**, **fondo blanco liso (#FFFFFF)**, sin sombra ni degradado, objeto centrado y grande. Pedí 4 variantes y elegí la que se lea mejor chiquita (Discord la muestra a 22 px en el texto): recortá el fondo y bajala a 128×128.

Base (va al principio de cada prompt):
`A single food icon for a fantasy RPG, clean digital illustration (NOT pixel art), very vivid highly saturated colors with strong contrast, thick bold black outlines, simple big readable shapes, centered and large, plain pure white background (#FFFFFF), no shadow, no gradient, no text, no frame, square 1:1,`

Cada plato tiene **su silueta y su color dominante**, para reconocerlo aun a 22 px. Los tres primeros (los permanentes que se apilan) son los que más se van a ver, así que son los más distintos entre sí: disco redondo negro y rojo, pez dorado en diagonal, frasco alto verde.

| Emoji | Plato | Color | Prompt (después de la base) |
|---|---|---|---|
| `plato_surubi_disco` | Surubí al Disco (+10 vida máx) | rojo + negro | a wide round black iron plow disc used as a pan seen from above, blazing bright red and orange embers glowing under it, thick bright pink catfish steaks in the middle with yellow potato chunks, steam, black and fiery red palette, perfectly round silhouette |
| `plato_dorado_parrilla` | Dorado a la Parrilla (+1 DEF) | amarillo dorado | a whole shiny golden-yellow dorado fish with big bright gold scales lying diagonally across a black iron grill rack, orange flames and glowing charcoal below, long diagonal silhouette, intense gold and yellow palette |
| `plato_mojarras_escabeche` | Mojarras en Escabeche (+2 ATQ) | verde lima | a tall glass preserving jar with a cork lid and bright lime green glass, full of small silver fish with orange carrot slices, white onion rings and green bay leaves, tall jar silhouette, vivid lime green and orange palette |
| `plato_empanada_surubi` | Empanada de Surubí (+20 % oro) | marrón + moneda | a big golden-brown half-moon empanada with a thick braided crimped edge and a small fish tail shape on top, a large shiny gold coin leaning beside it, rich brown and gold palette |
| `plato_locro_pescador` | Locro del Pescador (+20 % EXP) | naranja + turquesa | a bright turquoise blue ceramic bowl full of thick vivid orange stew with yellow corn kernels and chunks of white fish, a wooden spoon, steam, orange and turquoise palette |
| `plato_chupin_dorado` | Chupín de Dorado (+15 % drop) | magenta + cobre | a shiny copper two-handled cooking pot full of bright magenta-pink fish soup, a golden dorado fish head and tail sticking out, green parsley on top, steam, copper and hot pink palette |
| `plato_pastel_mojarra` | Pastel de Mojarra (+25 % cantidad) | azul + dorado | a round golden-brown pie with a lattice crust and small fish tails poking out of the slits, one slice cut out, on a bright royal blue enamel plate, golden crust and strong blue palette |
| `plato_brocheta_dorado` | Brocheta de Dorado (+10 crítico) | multicolor | a long wooden skewer set diagonally with chunks of golden fish, bright red bell pepper, green bell pepper and purple onion, charred marks, a few orange sparks, colorful rainbow palette, thin diagonal silhouette |
| `plato_surubi_manteca` | Surubí a la Manteca (+10 esquive) | blanco + amarillo limón | a seared white fish fillet in a small black skillet with melting bright yellow butter, a big lemon wedge and a sprig of parsley, steam, black white and lemon yellow palette |
| `plato_caldo_ultimo_aliento` | Caldo del Último Aliento (último aliento) | cian eléctrico | a small black iron cauldron full of glowing electric cyan broth, a bright cyan and white ghostly flame rising from it shaped like a small phoenix, sparkles, black and electric cyan palette |
