# Íconos propios de la interfaz: qué subir y cómo se llaman

Desde la v0.19.4 el bot lee los **emojis de su aplicación por nombre** (Developer Portal → la aplicación → *Emojis*), al arrancar y cada 10 minutos. No hay que copiar ningún id ni tocar la base:
se sube el emoji con el nombre exacto de abajo y, en unos minutos, las pantallas lo usan. Mientras un emoji no esté subido, esa pantalla sigue como siempre (🔥 FN, «Nivel», barra de texto, 💰).
El código está en `GameData/UiEmojis.cs`, `Services/UiEmojiRefresher.cs` y `GameData/ProgressBar.cs`.

**Formato de cada archivo**: PNG o GIF (animado), cuadrado, idealmente 128×128, **hasta 256 KiB**. Dentro de un texto Discord lo dibuja a 22 px, así que el dibujo tiene que leerse chico:
formas gruesas, pocos detalles, buen contraste sobre fondo oscuro y claro.

## Los 25 nombres

**Ya hay un juego completo provisorio** en [`docs/ui-emojis/`](ui-emojis/) (25 PNG de 128×128 en pixel art, el nombre de cada archivo es el nombre del emoji). En el portal se arrastran todos juntos y
el portal los nombra por el archivo. Se regeneran con `node tools/ui-art/make-ui-emojis.js` (sin dependencias). Son provisorios a propósito: cuando haya arte propio se borra el emoji del portal y
se sube otro **con el mismo nombre** (el bot los busca por nombre, no por id, así que no hay que tocar nada más).

### Siete íconos sueltos

| Nombre | Dónde sale | Idea |
|---|---|---|
| `ui_fn` | perfil (primera línea), ranking | El Fuego Nuevo. **Puede ser animado** (una brasa que late): es el único lugar donde conviene un GIF. Ya dice «FN» en el dibujo, el texto no lo repite |
| `ui_lv` | perfil, ranking, subida de nivel | «LV» azul. Mientras no esté, el texto dice «Nivel» |
| `ui_xp` | el título del campo de XP del perfil | La experiencia (reemplaza el 📊) |
| `ui_oro` | la línea «Oro» del perfil | Moneda de oro (reemplaza el 💰) |
| `ui_banco` | la línea «Banco» del perfil | Caja fuerte o alcancía (reemplaza el 🏦) |
| `ui_polvo` | la línea «Polvo» del perfil | Polvo mágico, chispas (reemplaza el ✨) |
| `ui_racha` | la línea «Racha» del perfil | Una llamita (reemplaza el 🎁) |

El perfil dice **XP** (ya no «Experiencia») y las líneas de números quedan así (ícono propio al final de cada línea; el arma y el amuleto con su emoji de ítem, sin el nombre):
```
ATQ 123 <arma> ⚡
DEF 45 <amuleto>
Oro 1.234 <moneda>
Banco 99 <banco>
```

### Las barras: 6 piezas por tipo (18 emojis)

Cada tipo de barra son **seis emojis**: un extremo izquierdo, un medio y un extremo derecho, cada uno **lleno** y **vacío**. El bot arma la barra de 10 tramos pegándolos (`l` + 8 × `m` + `r`).
Para que se vea continua, los tres dibujos de un mismo estado tienen que **tocar los bordes izquierdo y derecho del lienzo** (el medio, de lado a lado; los extremos, redondeados solo por fuera)
y tener la misma altura. Las piezas no deben ser animadas. Si se dibujan a mano, lo más fácil es hacer primero el medio y derivar los extremos.

Nombre: `bar_{tipo}_{parte}_{estado}`, con tipo `hp` | `xp` | `pesar`, parte `l` | `m` | `r` y estado `f` (lleno) | `e` (vacío).

| Tipo | Dónde sale | Colores (provisorios) |
|---|---|---|
| `hp` | la vida del perfil y la del jefe en equipo | rojo, vacío gris oscuro |
| `xp` | la barra de XP del perfil | azul, vacío gris oscuro |
| `pesar` | cuánto pesar le queda al Asador (`/fuegonuevo`, `/story`) | violeta, vacío gris oscuro |

Los 18 nombres: `bar_hp_l_f`, `bar_hp_l_e`, `bar_hp_m_f`, `bar_hp_m_e`, `bar_hp_r_f`, `bar_hp_r_e`, y lo mismo con `bar_xp_…` y `bar_pesar_…`.
**Si falta una sola pieza de un tipo, ese tipo sigue con la barra de texto** (una barra a medias se ve peor).

## Prompts para el arte propio (cuando se quiera reemplazar los provisorios)

Estilo de todos: *pixel art, 128×128, fondo transparente, contorno oscuro grueso, colores saturados, formas simples que se lean a 22 px, mismo estilo que los ítems del juego.*

- `ui_lv`: «las letras LV en bloque grueso, azul eléctrico con brillo, dentro de una insignia cuadrada redondeada; sin otros elementos».
- `ui_fn`: «una llama de asador naranja y dorada con una brasa roja al centro y las letras FN integradas, bien legibles; opcional: GIF de 4 cuadros donde la llama late».
- `ui_xp`: «una estrella de experiencia dorada con destello azul, o las letras XP en verde, dentro de una insignia como la del LV».
- `ui_oro`: «una moneda de oro con borde marcado y un reflejo arriba a la izquierda». `ui_banco`: «una pequeña caja fuerte de banco, gris azulada, con la rueda dorada en el centro».
- `ui_polvo`: «tres o cuatro chispas mágicas de cuatro puntas, violeta, celeste y dorado». `ui_racha`: «una llamita naranja con el centro amarillo».
- Barras: no conviene pedirlas a un generador de imágenes porque las piezas tienen que encajar al píxel; mejor dibujar el medio (`m`) en un editor de pixel art y derivar los extremos, o usar el generador de `tools/ui-art/`.

## Dónde NO se usan las barras de emojis (a propósito)

Una barra de 10 emojis mide unos 240 px: en una **columna de un tercio del ancho** (campos `inline`) se corta en dos líneas. Por eso el combate (jugador y monstruo lado a lado), las
misiones, los logros, los oficios y el plantel de jugadores del jefe en equipo siguen con la barra de texto. Para llevarlas también ahí hay que apilar los campos (uno debajo del otro).

## Lo que no se puede (límites de Discord)

- Un emoji dentro de un texto mide siempre 22 px; solo un mensaje hecho únicamente de emojis se ve más grande. Un «LV» realmente grande requiere una imagen generada.
- Los **pies** de los mensajes no dibujan emojis propios (se ven como código); por eso el pie no los lleva.
- Los menús desplegables y el autocompletado tampoco los dibujan.
