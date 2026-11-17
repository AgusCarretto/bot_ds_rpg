# Arte de Navidad (prompts para Bing Image Creator)

Todo el arte del evento «Navidad en el Fogón» (diseño en `docs/navidad.md`, rama `feature/navidad`). Son **12 imágenes**: 8 íconos, 2 caras con escenario, el ícono del evento y el banner de nieve. El Reno NO lleva arte: las mascotas se muestran con un emoji común (🐦 Ñandusito…), así que va con 🦌 como las demás.

**Cómo se carga:**
- Los **íconos y las caras** se suben al Developer Portal (la aplicación del bot → Emojis) con el **nombre de emoji** de la tabla. Cuando estén, avisame y los conecto en el código y en `Database/update_item_emojis.sql` (después se corre `Database/apply_emojis.sql` en Railway).
- El **banner** NO va al portal: mandame el PNG y yo lo recorto, lo achico a 300 px de ancho y lo dejo en `Assets/banners/` (como los de la Brasa de Color, `docs/arte-banners.md`).

**Consejos** (lo que funcionó con el arte anterior):
- **Lo más GRANDE posible** (el dueño, 2026-11-17: «que sean lo más grande posible para que se distingan bien»): el dibujo tiene que llenar casi todo el cuadro, sin margen. Al recortar, cortá **pegado al dibujo** (dejá 1 o 2 píxeles, no más): cualquier borde vacío lo achica en Discord. Si la IA lo deja chiquito en el medio, repetí el prompt agregando `zoomed in, the object touches the edges of the image`.
- Pedí 4 variantes y elegí la que se lea mejor **en chiquito**: Discord muestra los emojis a 22 px. Mejor una silueta simple y gruesa que muchos detalles finos.
- **Sin pixel art.** Los íconos y el banner van con **fondo blanco liso** para que lo recortes; las dos caras llevan su escenario y **no** se les saca el fondo.
- Recortá los íconos y las caras **cuadrados**. Que todos los íconos parezcan del mismo juego: mismo trazo, colores saturados y un poco de brillo.

---

## Íconos (8)

Base común (va al principio de cada prompt):
`A single game item icon for a fantasy RPG, the object as big as possible filling almost the entire square edge to edge with minimal margin, one simple bold readable silhouette, thick clean outlines, high contrast, vibrant saturated colors, soft glow, cozy classic snowy Christmas style, plain pure white background, square, no text, not pixel art,`

| Emoji | Qué es | Prompt (después de la base) |
|---|---|---|
| `regalo` | 🎁 **Regalo**, la moneda del evento (se ve en casi todas las pantallas de Navidad) | a small wrapped Christmas gift box, bright red paper with a shiny gold ribbon and a big bow, a little snow on the lid, sparkles around it |
| `campana_navidad` | 🔔 **Campana** (reinicia `/hunt`) | a golden jingle bell with a red velvet bow and two holly leaves with red berries, shiny metal highlights |
| `calcetin` | 🧦 **Calcetín**, la Caja Navideña | a red and white knitted Christmas stocking with a fluffy white cuff, overflowing with small gifts, gold coins and a candy cane |
| `vela_navidad` | 🕯️ **Vela** (+50 % de Regalos por 1 hora) | a thick red Christmas candle with a warm glowing flame, dripping wax, a ring of holly and pine around its base, warm golden glow |
| `bola_nieve` | ❄️ **Bola de Nieve** (proyectil, 1 Regalo) | a round packed snowball with soft blue shadows, a few snow crystals flying off it, frosty sparkle |
| `baston_caramelo` | 🍬 **Bastón de Caramelo** (proyectil, 10) | a classic red and white striped candy cane, glossy, with a small green ribbon tied around it |
| `petardo` | 🧨 **Petardo** (proyectil, 50) | a red cartoon firecracker stick with gold bands and a lit fuse throwing bright yellow and orange sparks |
| `trineo` | 🛷 **Trineo** (proyectil, 200; el más caro) | a wooden sled with curled red runners, loaded with a big pile of snowballs, motion lines as if it is being launched |

## Caras con escenario (2)

Van como las caras de los monstruos de las zonas: imagen apaisada (3:2) con el personaje al centro para recortarla cuadrada, **con fondo pintado**. Si la IA lo devuelve sin fondo, repetí el escenario y agregá `fully painted background, no white or empty areas`.

Base común:
`A single fantasy RPG character portrait, wide 3:2 illustration with the character centered so it can be cropped square, extreme close-up of the head and shoulders filling almost the whole height of the image, facing the viewer, bold clean outlines, vibrant saturated colors, soft rim light, a detailed scenic background filling the whole image (never a plain or empty background), no text, no frame, not pixel art,`

| Emoji | Qué es | Prompt (después de la base) |
|---|---|---|
| `krampus` | 👹 **Krampus**, el jefe del evento (`/krampus`, cada 4 h) | Krampus, a menacing winter demon from alpine folklore, long curved goat horns, shaggy dark brown fur, glowing red eyes, a long red tongue, rusty chains over his shoulders and a bundle of birch branches in his claw, background: a snowy alpine village at night with dark wooden houses, warm lit windows, falling snow and a cold blue moonlight |
| `muneco_gigante` | ☃️ **Muñeco de Nieve Gigante**, el jefe de todos (`/munieco`) | a giant grumpy snowman, three huge snowballs, angry coal eyes and frowning coal mouth, long carrot nose, a battered black top hat and a red scarf, thick tree-branch arms raised as if ready to fight, a few snowballs stuck to his body, background: a snowy town square at night with string lights, a decorated pine tree and snow falling, tiny figures throwing snowballs at him from below to show how huge he is |

## Ícono del evento (1)

Para la pantalla de `/navidad` (va de miniatura). Usa la base de los íconos.

| Emoji | Qué es | Prompt (después de la base de íconos) |
|---|---|---|
| `navidad_fogon` | 🎄 **Navidad en el Fogón** | a cozy campfire with warm orange flames on a patch of snow, a small decorated pine tree next to it with red and gold ornaments and a star on top, a wrapped red gift leaning on the tree, falling snowflakes |

## El banner de nieve (1)

El cosmético de la tienda (1.000 Regalos). Se ve al pie del `/profile`. Tiene que parecerse en forma a los de la Brasa de Color (una tira ancha con marco y un círculo al centro), pero de nieve.

`A wide decorative banner plate for a game profile, aspect ratio 4:1, a frosty frame made of ice and snow with icicles hanging from the bottom edge, a round medallion in the center with a big glowing snowflake, pine branches with red berries and small red and gold Christmas ornaments on both sides, snowflakes and little sparkles poking out over the top edge and the corners so the silhouette is not a plain rectangle, cold blue and white with touches of red and gold, bold clean outlines, vibrant colors, plain pure white background, no text, not pixel art`

Nombre del archivo: `banner_nieve.png`. Mandámelo en el tamaño que salga: yo lo recorto al contenido y lo dejo en 300 px de ancho, como los otros.

---

## Subidos al portal (falta conectarlos)

| Emoji | Código para el bot | Subido |
|---|---|---|
| `regalo` | `<:regalo:1572268392424153138>` | 2026-11-17 |
| `campana_navidad` | `<:campana_navidad:1572268390691643562>` | 2026-11-17 |
| `vela_navidad` | `<:vela_navidad:1572268388972105909>` | 2026-11-17 |
| `calcetin` | `<:calcetin:1572268387357163621>` | 2026-11-17 |
| `baston_caramelo` | `<:baston_caramelo:1572274254941266065>` | 2026-11-17 |
| `bola_nieve` | `<:bola_nieve:1572274253213073577>` | 2026-11-17 |
| `petardo` | `<:petardo:1572274251648471174>` | 2026-11-17 |
| `trineo` | `<:trineo:1572274250012823676>` | 2026-11-17 |
| `krampus` | `<:krampus:1572275877201575987>` | 2026-11-17 |
| `muneco_gigante` | `<:muneco_gigante:1572275875565535232>` | 2026-11-17 |
| `navidad_fogon` | `<:navidad_fogon:1572275873791344790>` | 2026-11-17 |

Los 11 emojis están subidos. Falta solo el banner `banner_nieve.png` (va a `Assets/banners/`, no al portal).

## Para cuando estén subidos

Pasame los nombres tal como quedaron en el portal (si cambiaste alguno, no pasa nada) y yo:
1. cambio los emojis unicode de las pantallas de Navidad (🎁, 🔔, 🧦, 🕯️, ❄️, 🍬, 🧨, 🛷, 👹, ☃️; el 🦌 del Reno queda) por los propios donde se dibujan (en descripciones y valores de campo, no en títulos ni en botones);
2. pongo el Krampus y el Muñeco de miniatura en sus pantallas, y el ícono del evento en `/navidad`;
3. conecto el banner como opción del perfil.
