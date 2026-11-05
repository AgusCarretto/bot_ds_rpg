# Arte de los banners del perfil

Un banner es una imagen ancha que se ve **al pie del `/profile`** (solo cosmética). Cada bendición que tenés desbloquea **uno**, y cuanto más sube de nivel la bendición (tier I a V), **mejor** es el banner. Se elige en `/config` (menú «Banner del perfil») o con `aa banner`.

Hoy hay **65 banners provisorios**: los 5 tiers de cada una de las 13 bendiciones, generados con `node tools/ui-art/make-banners.js`. Cada bendición tiene **un solo banner que evoluciona**: el tier I es un degradé con una trama y un círculo, el II suma un aro, el III cuatro rombos, el IV un brillo y el V el marco y el centro dorados. Cuando haya arte propio se reemplaza el archivo con el mismo nombre.

## Cómo cargar arte nuevo

Los archivos viajan con el bot, en `Assets/banners/` (no se suben al portal como los emojis): se pisa o se agrega el archivo y se publica.

- **Nombre:** `banner_<clave>_<tier>.png`, con el tier de 1 a 5. Ejemplo: `banner_manada_3.png`.
- **Tamaño:** PNG de **300 × 100** (proporción 3:1). El tamaño decide cuánto ocupa en Discord: en la compu una imagen chica se ve en su tamaño y una grande se achica a unos 400 px (con 720 × 240 salía a todo el ancho del mensaje y quedaba enorme). Si el arte lo hacés más grande, achicalo a 300 × 100 antes de guardarlo; nada de texto chico ni detalles finos. Que pese **menos de ~60 KB** (cada perfil lo sube). Para probar otro tamaño se cambian `W` y `H` en `tools/ui-art/make-banners.js`.
- **Qué archivo se ve:** el del tier más alto que exista y que no pase del nivel que tiene el jugador. Si hay arte de tier I y III y el jugador tiene el nivel V, ve el III; con el nivel II ve el I.
- **Se puede cargar de a poco:** un tier solo, o una bendición sola. Lo que falta cae al tier más bajo que exista.

## Las 13 bendiciones

| Clave (nombre del archivo) | Bendición | Idea para el banner |
|---|---|---|
| `mano_lenador` | 🪓 Mano de Leñador | bosque, hachas, troncos |
| `pico_fino` | ⛏️ Pico Fino | mina, cristales, picos |
| `brasa_vieja` | 🗺️ Brasa Vieja | mapa viejo, brasas, camino |
| `ojo_de_halcon` | 🏹 Ojo de Halcón | halcón, cielo abierto, mira |
| `aprendiz` | 📊 Aprendiz | libros, velas, estudio |
| `filo_antiguo` | ⚔️ Filo Antiguo | espadas, acero, reliquia |
| `piel_curtida` | 🛡️ Piel Curtida | cuero, escudo, curtiembre |
| `corazon_de_brasa` | ❤️ Corazón de Brasa | corazón de fuego, brasas |
| `manada` | 🐾 Manada | mascotas juntas, huellas |
| `buen_pienso` | 🍖 Buen Pienso | comida de mascotas, cuenco |
| `bolsillo_hondo` | 💰 Bolsillo Hondo | monedas, bolsa llena |
| `alforja_del_fogonero` | 🎒 Alforja del Fogonero | alforja, cajas, fogón |
| `brasa_de_color` | 🌈 Brasa de Color | llamas de varios colores (la cosmética: sus 5 tiers son los colores Brasa Viva, Llama Dorada, Fuego Azul, Ascua Violeta y Sol del Fogón) |

## Los tiers

La idea es que **crezca con el nivel**: el tier I sencillo (un elemento, pocos colores), y a cada tier más riqueza (más elementos, más luz, más detalle) hasta el V, que es el «wow». Los cinco de una misma bendición tienen que reconocerse como la misma familia.

## Prompt base

> Wide banner illustration, 3:1 aspect ratio (it will be shrunk to 300x100), for a Discord RPG profile. Theme: **<idea de la tabla>**. Warm Argentine asado / campfire mood, strong readable shapes, no text, no letters, no watermark, important elements centered (the sides can be cropped). **Tier <I a V>**: <tier I: simple, one main element, limited palette | tier III: richer, several elements and lighting | tier V: epic, glowing, very detailed>.

Un banner por prompt. Si la imagen sale con otra proporción, se recorta y se achica a 300 × 100 antes de guardarla.
