# Emojis del dragón (v0.30.0)

Cinco emojis de aplicación (Developer Portal → la aplicación → *Emojis*). **El bot los busca por NOMBRE**, así que no hay que copiar ids ni tocar la base: se suben con el nombre exacto de abajo y,
en unos minutos (el bot los relee cada 10), las pantallas del dragón los usan. Mientras uno no esté, esa pantalla sigue con el de siempre (🐲 / 🗡️ / 🛡️ / 💰 / ✨). Se pueden ir subiendo de a uno.
Formato: PNG (o GIF), cuadrado, idealmente 128×128, hasta 256 KiB. Discord lo dibuja a 22 px dentro del texto: formas gruesas, pocos detalles, que se lea sobre fondo oscuro y claro.

| Nombre del emoji | Dónde sale | Reemplaza a |
|---|---|---|
| `ui_dragon` | el mensaje de compra del dragón | 🐲 |
| `dragon_fuerza` | el dragón de tipo Fuerza (`/dragon`, la cría, la compra) | 🗡️ |
| `dragon_defensa` | el dragón de tipo Defensa | 🛡️ |
| `dragon_oro` | el dragón de tipo Oro | 💰 |
| `dragon_especial` | el dragón de tipo Especial | ✨ |

## Prompts

Estilo de todos: *pixel art, 128×128, fondo transparente, contorno oscuro grueso, colores saturados, formas simples que se lean a 22 px, mismo estilo que los ítems del juego. Solo la cabeza o el busto
del dragón, de frente o de tres cuartos, sin texto.*

- `ui_dragon`: «una cría de dragón simpática, cabeza redonda con dos cuernitos y ojos grandes, escamas naranja brasa con la panza clara, una chispa de fuego saliendo de la boca; el dragón «base» del juego».
- `dragon_fuerza`: «un dragón rugiendo, escamas rojo intenso, colmillos y cuernos bien marcados, ojos amarillos furiosos y una espada pequeña cruzada detrás de la cabeza; se siente fuerte y agresivo».
- `dragon_defensa`: «un dragón azul acero, escamas grises azuladas, con un escudo redondo delante del pecho y una expresión firme y protectora; se siente sólido, como una armadura».
- `dragon_oro`: «un dragón dorado con escamas amarillo oro muy brillantes, una moneda de oro entre las garras y destellos; se siente rico y orgulloso».
- `dragon_especial`: «un dragón violeta y celeste como de galaxia, con estrellas y destellos mágicos alrededor, ojos brillantes y un aura suave; se siente raro y mágico».

Que los cuatro tipos compartan estilo y pose (así se reconocen como una familia) y que se distingan por el **color dominante** (rojo, azul, dorado, violeta): en 22 px es lo que más se lee.
