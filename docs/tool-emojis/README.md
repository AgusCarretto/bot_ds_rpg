# Emojis del hacha y el pico (Carpintero, v0.29.0)

Cuatro ítems nuevos, cuatro emojis de aplicación (Developer Portal → la aplicación → *Emojis*). **El nombre del emoji tiene que ser exactamente el del archivo.**
Ya hay un juego provisorio en esta carpeta (se regenera con `node tools/ui-art/make-tool-emojis.js`).

| Archivo / nombre del emoji | Ítem | Zona |
|---|---|---|
| `hacha_hierro` | Hacha de Hierro Forjado | 3 (×2) |
| `pico_hierro` | Pico de Hierro Forjado | 3 (×2) |
| `hacha_escoria` | Hacha de Escoria Templada | 5 (×3) |
| `pico_escoria` | Pico de Escoria Templada | 5 (×3) |

**Cómo se conectan** (el mismo flujo de siempre): se suben al portal → se copia el código de cada uno (`<:hacha_hierro:123…>`) → se agregan los `UPDATE` por nombre de ítem en
`Database/update_item_emojis.sql` → el dueño corre `Database/apply_emojis.sql` en Railway. **Ya están conectados (2026-10-27, los provisorios de esta carpeta)**; para reemplazarlos por arte propio se borra
el emoji del portal y se sube otro con el MISMO nombre (el código cambia de id: hay que actualizar el `UPDATE` de `update_item_emojis.sql` y volver a correr `apply_emojis.sql`).

## Prompts para el arte propio

Estilo de todos: *pixel art, 128×128, fondo transparente, contorno oscuro grueso, colores saturados, formas simples que se lean a 22 px, mismo estilo que los ítems del juego. La herramienta va en
diagonal, con el mango de abajo a la izquierda hasta arriba a la derecha.*

- `hacha_hierro`: «un hacha de leñador en diagonal, mango de madera marrón con vetas, hoja ancha de acero azulado con el filo brillante y un reflejo blanco arriba; se ve recién forjada y robusta».
- `pico_hierro`: «un pico de minero en diagonal, mango de madera marrón, cabeza de acero curva con dos puntas afiladas y un reflejo blanco en el lomo; mismo acero y misma madera que el hacha».
- `hacha_escoria`: «un hacha de escoria volcánica en diagonal: hoja negra violácea como obsidiana con vetas de lava naranja incandescente en el filo, mango de madera oscura y chamuscada, unas brasas chicas alrededor».
- `pico_escoria`: «un pico de escoria volcánica en diagonal: cabeza curva de obsidiana negra violácea con una grieta de lava naranja en el medio, mango de madera oscura y chamuscada; mismo material que el hacha».

Que cada par (hacha y pico del mismo tier) comparta paleta, y que el de la Zona 5 se vea claramente más poderoso que el de la Zona 3 sin dejar de leerse como la misma herramienta.
