# Arte del banner del perfil

Hay **un solo banner en todo el juego**: el de la bendición **Brasa de Color**. Es una imagen ancha que se ve **al pie del `/profile`** (solo cosmética) y **mejora con cada nivel de la bendición** (tier I a V). Solo lo puede activar quien tiene la bendición, y cada jugador elige qué tier mostrar entre los que ya desbloqueó (o «el mejor», que sube solo). Se activa en `/config` (menú «Banner del perfil») o con `aa banner`.

Son **5 imágenes en total**, una por tier, y **las cinco ya son arte propio** (2026-11-05): una tira ancha con marco, un círculo al centro y llamas, chispas y estrellitas que sobresalen por arriba; cada tier con el color de ese nivel de la bendición y más rico que el anterior.

| Tier | Nombre del color | Color | Cómo es |
|---|---|---|---|
| I | Brasa Viva | naranja rojizo | marco oscuro, brasa al centro y unas pocas llamas y chispas |
| II | Llama Dorada | dorado | marco dorado, una fila de llamitas y estrellitas |
| III | Fuego Azul | azul | llamas azules, estrellitas y un brillo |
| IV | Ascua Violeta | violeta | llamas violetas, esquinas labradas |
| V | Sol del Fogón | rojo y oro | marco dorado, un sol con rayos al centro y llamas altas por los costados |

## Cómo reemplazar o agregar arte

Los archivos viajan con el bot, en `Assets/banners/` (no se suben al portal como los emojis): se pisa el archivo y se publica.

- **Nombres:** `banner_brasa_de_color_1.png`, `banner_brasa_de_color_2.png`, … `banner_brasa_de_color_5.png` (el número es el tier).
- **Qué mandar:** el PNG **con fondo transparente**, en el tamaño que salga (los actuales eran de 4128 × 1024). Yo lo recorto al contenido y lo achico a **300 px de ancho** conservando la transparencia (sin halo sobre fondo oscuro ni claro), y lo dejo con el nombre del tier.
- **Tamaño final:** **300 px de ancho** (los actuales miden ≈ 300 × 78: una tira larga). El ancho decide cuánto ocupa en Discord: en la compu una imagen chica se ve en su tamaño y una grande se achica a unos 400 px (con 720 px salía a todo el ancho del mensaje y quedaba enorme). Conviene que los cinco tengan más o menos el mismo alto para que el perfil no «salte» al cambiar de tier. Que pese **menos de ~120 KB** (cada perfil lo sube; hoy pesan entre 25 y 51 KB).
- **Qué archivo se ve:** el del tier que eligió el jugador (sin pasar del nivel que tiene la bendición); con «el mejor», el de su nivel. Si falta el archivo de un tier, se ve el más alto que exista por debajo.
- **Se puede cargar de a poco:** un tier solo, o los cinco.

`tools/ui-art/make-banners.js` es solo un generador de placeholders (degradé + trama + círculo): escribe en `docs/banners-provisorios/` y **nunca** en `Assets/banners/`, para no pisar el arte de verdad.

## Si se quiere rehacer alguno

Lo que funcionó para generarlos: una placa ancha (≈ 4:1) con un marco, un círculo de brasa al centro, fondo blanco liso para recortarlo, y llamas, chispas y estrellitas que **sobresalen** por arriba, las esquinas y los costados (la silueta no es un rectángulo). Los cinco tienen que reconocerse como **el mismo banner que va creciendo**: darle el tier I como referencia de estilo y de proporción a los otros cuatro. Como se ve tan chico (300 px de ancho), las llamas y chispas tienen que ser grandes y simples, sin texto ni detalles finos, y el marco necesita una luz de borde fina porque el fondo del embed de Discord también es oscuro.
