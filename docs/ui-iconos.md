# Íconos propios de la interfaz: qué subir y cómo se llaman

Desde la v0.19.4 el bot lee los **emojis de su aplicación por nombre** (Developer Portal → la aplicación → *Emojis*), cada 10 minutos y al arrancar. No hay que copiar ningún id ni tocar la base:
se sube el emoji con el nombre exacto de abajo y, en unos minutos, las pantallas lo usan. Mientras un emoji no esté subido, esa pantalla sigue como siempre (🔥 FN, «Nivel», barra de texto).
El código está en `GameData/UiEmojis.cs`, `Services/UiEmojiRefresher.cs` y `GameData/ProgressBar.cs`.

**Formato de cada archivo**: PNG o GIF (animado), cuadrado, idealmente 128×128, **hasta 256 KiB**. Dentro de un texto Discord lo dibuja a 22 px, así que el dibujo tiene que leerse chico:
formas gruesas, pocos detalles, buen contraste sobre fondo oscuro y claro.

## Los tres íconos sueltos

| Nombre | Dónde sale | Idea |
|---|---|---|
| `ui_fn` | perfil (primera línea), ranking | El Fuego Nuevo. **Puede ser animado** (una brasa que late): es el único lugar donde conviene un GIF. Ya dice «FN» en el dibujo, el texto no lo repite |
| `ui_lv` | perfil, ranking, subida de nivel | «LV» azul. Mientras no esté, el texto dice «Nivel» |
| `ui_xp` | el campo de XP del perfil | Un ícono corto de experiencia (reemplaza el 📊) |

El perfil dice **XP** (ya no «Experiencia»).

## Las barras: 6 piezas por tipo

Cada tipo de barra son **seis emojis**: un extremo izquierdo, un medio y un extremo derecho, cada uno **lleno** y **vacío**. El bot arma la barra de 10 tramos pegándolos (`l` + 8 × `m` + `r`).
Para que se vea continua, los tres dibujos de un mismo estado tienen que **tocar los bordes izquierdo y derecho del lienzo** (el medio, de lado a lado; los extremos, redondeados solo por fuera)
y tener la misma altura. Las piezas no deben ser animadas.

Nombre: `bar_{tipo}_{parte}_{estado}`, con tipo `hp` | `xp` | `pesar`, parte `l` | `m` | `r` y estado `f` (lleno) | `e` (vacío).

| Tipo | Dónde sale | Colores sugeridos |
|---|---|---|
| `hp` | la vida del perfil y la del jefe en equipo | rojo / verde, vacío gris oscuro |
| `xp` | la barra de XP del perfil | azul / dorado, vacío gris oscuro |
| `pesar` | cuánto pesar le queda al Asador (`/fuegonuevo`, `/story`) | violeta o brasa, vacío ceniza |

Los 18 nombres: `bar_hp_l_f`, `bar_hp_l_e`, `bar_hp_m_f`, `bar_hp_m_e`, `bar_hp_r_f`, `bar_hp_r_e`, y lo mismo con `bar_xp_…` y `bar_pesar_…`.
**Si falta una sola pieza de un tipo, ese tipo sigue con la barra de texto** (una barra a medias se ve peor).

## Dónde NO se usan las barras de emojis (a propósito)

Una barra de 10 emojis mide unos 240 px: en una **columna de un tercio del ancho** (campos `inline`) se corta en dos líneas. Por eso el combate (jugador y monstruo lado a lado), las
misiones, los logros, los oficios y el plantel de jugadores del jefe en equipo siguen con la barra de texto. Para llevarlas también ahí hay que apilar los campos (uno debajo del otro).

## Lo que no se puede (límites de Discord)

- Un emoji dentro de un texto mide siempre 22 px; solo un mensaje hecho únicamente de emojis se ve más grande. Un «LV» realmente grande requiere una imagen generada.
- Los **pies** de los mensajes no dibujan emojis propios (se ven como código); por eso el pie no los lleva.
- Los menús desplegables y el autocompletado tampoco los dibujan.
