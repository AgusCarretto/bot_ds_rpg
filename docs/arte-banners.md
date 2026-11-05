# Arte del banner del perfil

Hay **un solo banner en todo el juego**: el de la bendición **Brasa de Color**. Es una imagen ancha que se ve **al pie del `/profile`** (solo cosmética) y **mejora con cada nivel de la bendición** (tier I a V). Solo lo puede activar quien tiene la bendición, y cada jugador elige qué tier mostrar entre los que ya desbloqueó (o «el mejor», que sube solo). Se activa en `/config` (menú «Banner del perfil») o con `aa banner`.

Son **5 imágenes en total**, una por tier. Hoy hay 5 provisorias (generadas con `node tools/ui-art/make-banners.js`): cada tier lleva el color de ese nivel de la bendición y más adornos que el anterior (el I sencillo; el II suma un aro; el III, cuatro rombos; el IV, un brillo; el V, el marco y el centro dorados). Cuando haya arte propio se reemplaza el archivo con el mismo nombre.

## Cómo cargar arte nuevo

Los archivos viajan con el bot, en `Assets/banners/` (no se suben al portal como los emojis): se pisa el archivo y se publica.

- **Nombres:** `banner_brasa_de_color_1.png`, `banner_brasa_de_color_2.png`, … `banner_brasa_de_color_5.png` (el número es el tier).
- **Tamaño:** PNG de **300 × 100** (proporción 3:1). El tamaño decide cuánto ocupa en Discord: en la compu una imagen chica se ve en su tamaño y una grande se achica a unos 400 px (con 720 × 240 salía a todo el ancho del mensaje y quedaba enorme). Si el arte lo hacés más grande, achicalo a 300 × 100 antes de guardarlo; nada de texto chico ni detalles finos. Que pese **menos de ~60 KB** (cada perfil lo sube). Para probar otro tamaño se cambian `W` y `H` en `tools/ui-art/make-banners.js`.
- **Qué archivo se ve:** el del tier que eligió el jugador (sin pasar del nivel que tiene la bendición); con «el mejor», el de su nivel. Si falta el archivo de un tier, se ve el más alto que exista por debajo.
- **Se puede cargar de a poco:** un tier solo, o los cinco. Lo que falta cae al tier más bajo que exista.

## Los 5 tiers

La idea es que **crezca con el nivel**: el tier I sencillo (un elemento, pocos colores) y a cada tier más riqueza (más elementos, más luz, más detalle) hasta el V, que es el «wow». Los cinco tienen que reconocerse como el mismo banner. Cada nivel de la bendición ya tiene su color propio:

| Tier | Nombre del color | Color |
|---|---|---|
| I | Brasa Viva | naranja rojizo `#E8590C` |
| II | Llama Dorada | dorado `#FFB000` |
| III | Fuego Azul | azul `#1C7ED6` |
| IV | Ascua Violeta | violeta `#9C36B5` |
| V | Sol del Fogón | rojo `#F03E3E` |

## Prompt base

> Wide banner illustration, 3:1 aspect ratio (it will be shrunk to 300x100), for a Discord RPG profile. Theme: **campfire embers and living flames, Argentine asado mood**. Color: **<el del tier>**. Strong readable shapes, no text, no letters, no watermark, important elements centered (the sides can be cropped). **Tier <I a V>**: <tier I: simple, one main flame, limited palette | tier III: richer, several flames and lighting | tier V: epic, glowing, golden details, very detailed>.

Un banner por prompt. Si la imagen sale con otra proporción, se recorta y se achica a 300 × 100 antes de guardarla.
