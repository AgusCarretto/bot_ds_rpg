# Arte del banner del perfil

Hay **un solo banner en todo el juego**: el de la bendición **Brasa de Color**. Es una imagen ancha que se ve **al pie del `/profile`** (solo cosmética) y **mejora con cada nivel de la bendición** (tier I a V). Solo lo puede activar quien tiene la bendición, y cada jugador elige qué tier mostrar entre los que ya desbloqueó (o «el mejor», que sube solo). Se activa en `/config` (menú «Banner del perfil») o con `aa banner`.

Son **5 imágenes en total**, una por tier. **El tier I ya es arte propio** (la placa de madera quemada con la brasa al centro, 300 × 169); los tiers II a V son todavía provisorios (generados con `node tools/ui-art/make-banners.js`: cada uno lleva el color de ese nivel de la bendición y más adornos que el anterior). Cuando haya arte propio se reemplaza el archivo con el mismo nombre. Mientras los tiers II a V sean provisorios, al cambiar de tier el banner cambia también de alto (los provisorios miden 300 × 100).

## Cómo cargar arte nuevo

Los archivos viajan con el bot, en `Assets/banners/` (no se suben al portal como los emojis): se pisa el archivo y se publica.

- **Nombres:** `banner_brasa_de_color_1.png`, `banner_brasa_de_color_2.png`, … `banner_brasa_de_color_5.png` (el número es el tier).
- **Tamaño:** PNG de **300 px de ancho**: **no más ancho** (con 720 px salía a todo el ancho del mensaje y quedaba enorme). El alto sale del arte y puede ser mayor: el tier I mide 300 × 169, y si hay que elegir, mejor que crezca hacia arriba que hacia los costados; conviene que los cinco tengan más o menos el mismo alto para que el perfil no «salte» al cambiar de tier. El ancho decide cuánto ocupa en Discord: en la compu una imagen chica se ve en su tamaño y una grande se achica a unos 400 px (con 720 × 240 salía a todo el ancho del mensaje y quedaba enorme). Si el arte lo hacés más grande, achicalo a 300 de ancho antes de guardarlo (o pasámelo y lo recorto yo al contenido, conservando la transparencia); nada de texto chico ni detalles finos. Que pese **menos de ~120 KB** (cada perfil lo sube; el tier I pesa 72 KB). Para probar otro tamaño de los provisorios se cambian `W` y `H` en `tools/ui-art/make-banners.js`.
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

## Los 5 prompts

Cada uno está completo (se pega entero). Los cinco comparten la misma base para que se reconozcan como **el mismo banner que va creciendo**: una placa ancha con marco de madera quemada y un círculo de brasa al centro; lo que cambia es el color, la cantidad de llamas, chispas y estrellitas que **sobresalen** por arriba, las esquinas y los costados, y lo elaborado del marco. Fondo blanco liso para recortarlo (así lo que sobresale queda suelto, sin rectángulo detrás).

**Ojo con las proporciones:** NO pedir una placa más ancha ni más baja. El tier I que armaste (placa de ≈ 2,8:1 con las llamas arriba; recortada queda de ≈ 1,8:1, 300 × 169) es la referencia: los otros cuatro tienen que tener **la misma placa y una proporción parecida**. Por eso el lienzo de los prompts es de 2:1 (más aire arriba para las llamas) y no de 3:1. Y como el marco es oscuro y el fondo del embed también, conviene una luz de borde anaranjada fina.

**Tip:** dale el tier I (la imagen que ya está) como referencia de estilo y de proporción a los otros cuatro, así no cambia el dibujo de la placa entre tiers. Con la referencia puesta, que mantenga la misma forma y tamaño de la placa.

**Después de generarlos:** recortá el fondo blanco (PNG con transparencia), achicá a **300 de ancho** (o pasámelos y los recorto yo) y guardalos como `banner_brasa_de_color_1.png` … `banner_brasa_de_color_5.png` en `Assets/banners/`. Como se ve tan chico, las llamas y chispas tienen que ser grandes y simples.

### Tier I — Brasa Viva (naranja rojizo)

```
Wide horizontal profile banner illustration for an RPG game, 2:1 aspect ratio (for example 1500x750 px), on a flat solid pure white background that I will cut out. Clean painterly digital illustration with bold readable shapes, NOT pixel art. A rounded rectangular banner plaque with a dark charred-wood frame and a glowing circular ember emblem in the center, placed in the lower 60% of the canvas, with the upper part left free for the flames. Tier 1 of 5, the simplest of the series: palette of warm orange-red and dark brown, a plain frame, a couple of small flame tongues rising from the top edge and about six tiny glowing embers drifting up and out past the frame, so the silhouette is not a plain rectangle. A thin warm rim light runs along the whole frame edge so the plaque stays visible on a dark background. Argentine asado / campfire mood. No text, no letters, no watermark, no characters. It will be shrunk to 300 px wide, so keep the shapes big, simple and high-contrast, with no fine detail.
```

### Tier II — Llama Dorada (dorado)

```
Wide horizontal profile banner illustration for an RPG game, 2:1 aspect ratio (for example 1500x750 px), on a flat solid pure white background that I will cut out. Clean painterly digital illustration with bold readable shapes, NOT pixel art. A rounded rectangular banner plaque with a dark charred-wood frame and a glowing circular ember emblem in the center, placed in the lower 60% of the canvas, with the upper part left free for the flames; the emblem now has an extra thin ring around it. Tier 2 of 5, a little richer than tier 1: palette of golden yellow and amber with dark brown, a row of small golden flame tongues rising along the top edge and over the corners, plus a handful of sparks and three small four-point stars floating up and out past the frame, so the silhouette is not a plain rectangle. A thin warm rim light runs along the whole frame edge so the plaque stays visible on a dark background. Argentine asado / campfire mood. No text, no letters, no watermark, no characters. It will be shrunk to 300 px wide, so keep the shapes big, simple and high-contrast, with no fine detail.
```

### Tier III — Fuego Azul (azul)

```
Wide horizontal profile banner illustration for an RPG game, 2:1 aspect ratio (for example 1500x750 px), on a flat solid pure white background that I will cut out. Clean painterly digital illustration with bold readable shapes, NOT pixel art. A rounded rectangular banner plaque with a dark charred-wood frame and a glowing circular ember emblem in the center, placed in the lower 60% of the canvas, with the upper part left free for the flames, with a thin ring around the emblem and two small diamond ornaments on each side of it. Tier 3 of 5, clearly richer than tier 2: palette of vivid blue with ice-white highlights and dark navy, blue flames of different heights bursting up from the top edge and the corners, many glowing sparks and several small four-point stars floating up and out past the frame, a soft blue glow around the plaque, so the silhouette is not a plain rectangle. A thin warm rim light runs along the whole frame edge so the plaque stays visible on a dark background. Argentine asado / campfire mood. No text, no letters, no watermark, no characters. It will be shrunk to 300 px wide, so keep the shapes big, simple and high-contrast, with no fine detail.
```

### Tier IV — Ascua Violeta (violeta)

```
Wide horizontal profile banner illustration for an RPG game, 2:1 aspect ratio (for example 1500x750 px), on a flat solid pure white background that I will cut out. Clean painterly digital illustration with bold readable shapes, NOT pixel art. A rounded rectangular banner plaque with a dark charred-wood frame and a glowing circular ember emblem in the center, placed in the lower 60% of the canvas, with the upper part left free for the flames, with a thin ring around the emblem, diamond ornaments on both sides, and ornate carved corner pieces on the frame. Tier 4 of 5, very rich: palette of deep violet and magenta with bright lilac highlights, swirling violet flames curling out beyond the top edge and both sides of the plaque, lots of drifting embers and star shapes escaping the frame, a strong glowing aura behind the emblem, so the silhouette is clearly not a rectangle. A thin warm rim light runs along the whole frame edge so the plaque stays visible on a dark background. Argentine asado / campfire mood. No text, no letters, no watermark, no characters. It will be shrunk to 300 px wide, so keep the shapes big, simple and high-contrast, with no fine detail.
```

### Tier V — Sol del Fogón (rojo y oro)

```
Wide horizontal profile banner illustration for an RPG game, 2:1 aspect ratio (for example 1500x750 px), on a flat solid pure white background that I will cut out. Clean painterly digital illustration with bold readable shapes, NOT pixel art. A rounded rectangular banner plaque, the most elaborate of the series: an ornate GOLDEN frame with golden corner flourishes, a blazing golden sun emblem with short rays in the center instead of a plain ember circle, a ring around it and diamond ornaments on both sides, placed in the lower 60% of the canvas, with the upper part left free for the flames. Tier 5 of 5, epic: palette of crimson red and bright gold with dark brown, big tall flames and many golden four-point stars and sparks bursting out of the top edge, the sides and the corners, an intense golden glow, so the silhouette is dramatic and clearly not a rectangle. A thin warm rim light runs along the whole frame edge so the plaque stays visible on a dark background. Argentine asado / campfire mood. No text, no letters, no watermark, no characters. It will be shrunk to 300 px wide, so keep the shapes big, simple and high-contrast, with no fine detail.
```
