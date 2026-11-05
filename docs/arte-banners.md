# Arte de los banners del perfil

Un banner es una imagen ancha que se ve **al pie del `/profile`** (solo cosmética). Cada bendición que tenés desbloquea **uno**, y cuanto más sube de nivel la bendición (tier I a V), **mejor** es el banner. Se elige en `/config` (menú «Banner del perfil») o con `aa banner`.

Hoy hay **13 banners provisorios de tier I** (degradé + trama + círculo, generados con `node tools/ui-art/make-banners.js`). Los tiers II a V usan el de tier I hasta que exista su archivo.

## Cómo cargar arte nuevo

Los archivos viajan con el bot, en `Assets/banners/` (no se suben al portal como los emojis): se pisa o se agrega el archivo y se publica.

- **Nombre:** `banner_<clave>_<tier>.png`, con el tier de 1 a 5. Ejemplo: `banner_manada_3.png`.
- **Tamaño:** PNG de **720 × 240** (proporción 3:1). Discord lo muestra a unos 400 px de ancho, así que nada de texto chico ni detalles finos en los bordes. Que pese **menos de ~200 KB** (cada perfil lo sube).
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

> Wide banner illustration, 3:1 aspect ratio (720x240), for a Discord RPG profile. Theme: **<idea de la tabla>**. Warm Argentine asado / campfire mood, strong readable shapes, no text, no letters, no watermark, important elements centered (the sides can be cropped). **Tier <I a V>**: <tier I: simple, one main element, limited palette | tier III: richer, several elements and lighting | tier V: epic, glowing, very detailed>.

Un banner por prompt. Si la imagen sale con otra proporción, se recorta a 720 × 240 antes de guardarla.
