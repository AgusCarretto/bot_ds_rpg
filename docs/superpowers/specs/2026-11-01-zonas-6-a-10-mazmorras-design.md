# Zonas 6 a 10, Reliquias y mazmorras — diseño

_2026-11-01. Conversación con el dueño (brainstorming). Lo que dijo él va como suyo; lo que decidí yo está marcado **[mi decisión]**._

## Para qué

Hoy, con el equipo del Fogón y la comida, al Asador se lo baja rápido, y con Fuegos Nuevos altos la vuelta se hace muy corta. El dueño quiere que **a partir del FN 5 cada vuelta sea más larga y más interesante**: zonas nuevas que se abren con los Fuegos Nuevos, una **Reliquia** que hay que forjar para salir de cada una y, en vez de un jefe, una **mazmorra interactiva tipo acertijo**, distinta en cada zona, con patrones y sorteada en cada corrida. Al final, el **Asador más fuerte** cuanto más lejos está.

Éxito, para él: que pasar de zona en el FN alto pida jugar bien (resolver el acertijo y llegar con vida), no solo tener el número de ataque; y que haya un camino largo (FN 5 → FN 17) con algo nuevo cada pocos Fuegos Nuevos.

## Metas

- Cinco zonas nuevas, **6 a 10**, que se abren con el Fuego Nuevo: **la 6 en el FN 5, la 7 en el 8, la 8 en el 11, la 9 en el 14 y la 10 en el 17** (una cada 3 FN). El FN 20 queda para «algo después de la 10» (fuera de este diseño).
- Del FN 0 al 4 **no cambia nada**: el jefe de la Zona 5 abre la Zona 0 y se pelea al Asador como hoy.
- Cada zona nueva es **una zona normal pero simple**: **un solo monstruo para cazar y un solo drop** (que sale bastante), su monstruo de travel y su Reliquia. Nada de mecánicas raras dentro de la zona (eso «para mucho más adelante»).
- **El drop de cada zona es sencillo y se distingue a simple vista** (nombre corto y claro, emoji propio). 5 drops nuevos en total, uno por zona.
- Recetas que piden **muchos materiales** (a esa altura se recolecta mucho más) y **bastantes drops** de la zona.
- **Mazmorras que se hacen solo**, con botones de Discord, sin comida adentro.

## No-metas (por ahora)

- Mazmorras en grupo.
- Mecánicas especiales dentro de las zonas (clima, monstruos con reglas propias).
- Lo que pasa en el FN 20 («zona 11» u otra cosa).
- **[mi decisión]** Mascotas nuevas para las zonas 6 a 10, capítulos de historia nuevos y logros/títulos propios: se pueden sumar después sin tocar este diseño.

## Anti-metas (sería un fracaso aunque funcione)

- Un acertijo que se resuelve copiando la respuesta de otro por chat: **cada corrida se sortea distinta**.
- Un acertijo que se pasa tocando al azar (las brasas «fáciles»): tiene que pedir pensar.
- Que el Asador vuelva a ser un trámite con el equipo de la zona.

## Cómo es la vuelta (decidido por el dueño)

Llamo **zona tope** a la zona más alta que habilitan tus Fuegos Nuevos (Zona 5 con FN 0–4, Zona 6 con FN 5–7, Zona 7 con FN 8–10, … Zona 10 desde el FN 17).

- **Zonas 1 a 5:** como hoy. Si la Zona 5 NO es tu zona tope, su jefe te abre la **Zona 6** (no la Zona 0).
- **Zonas 6 a 10, si NO son tu zona tope:** farmeás, forjás su **Reliquia** y con ella `aa boss` es la **mazmorra** de esa zona. Ganarla **te abre la zona siguiente** (pasás de zona como siempre; no hay puerta al Fogón ahí).
- **Tu zona tope:** lo mismo, pero ganar la mazmorra **te saca de la zona y te mete directo en la Zona 0**: ahí solo existe la pelea contra el **Asador** (`aa boss` / `aa fight`), **sin poder curarte entre la mazmorra y la pelea**. Ganarle habilita el Fuego Nuevo, como hoy.
- **Perder la mazmorra:** volvés a tu zona y **perdés la Reliquia** (hay que forjarla de nuevo).
- Ejemplo: con FN 5, 6 o 7 la Zona 5 abre la 6 y la mazmorra de la 6 lleva al Asador. Con FN 8 la mazmorra de la 6 abre la 7, y la de la 7 lleva al Asador.

Como cada vuelta (Fuego Nuevo) sube todas las zonas otra vez, el Fogón «se corre» solo: siempre está pasando tu zona tope.

## La Reliquia

- Se **forja**, como un arma, en la herrería de su zona.
- Pide los drops de la zona, muchos materiales de recolección y **un ingrediente raro que sale del travel de esa zona** (el dueño sugirió una caja difícil de sacar que hoy no da el travel, tipo Cofre de Escoria; **se define después**).
- Se gasta al entrar a la mazmorra. **[mi decisión]** Se gasta también si ganás (en la vuelta siguiente hay que forjarla otra vez, como todo el equipo). **[mi decisión]** El descuento se hace cuando la mazmorra TERMINA (ganada, perdida o abandonada por tiempo): si el bot se reinicia en el medio, la Reliquia no se pierde.
- **Abierto (el dueño: «luego vemos»):** si es una llave, una Reliquia o un arma obligatoria. El diseño la trata como un objeto del inventario que se forja y se gasta; cambiarle el nombre o la forma no cambia el flujo.

## Las mazmorras

Reglas comunes:

- **Solo**, con botones (y su versión escrita `aa …` para cuando un botón se traba, como en el combate).
- **Sin comida adentro.**
- **Cada error saca vida**; con 0 de vida perdés la mazmorra. **La vida con la que salís es la que llevás al Asador** (si es tu zona tope). Resolver bien deja más vida para la pelea: ahí está la gracia de mejorar.
- **Sorteada en cada corrida** (secuencia, mapa, tablero o clave distinta), pero con **patrones** que se aprenden.
- Un tiempo por jugada como el combate (si no jugás, se abandona y cuenta como perdida).

Una por zona:

| Zona | Mazmorra | Idea |
|---|---|---|
| 6 | **El patrón del fuego** | Se muestra una secuencia de golpes con íconos (🔥💧🪨); tocás el contraataque correcto, en orden. Cada piso la secuencia es más larga. |
| 7 | **Laberinto de humo** | Un mapa de casillas que se descubre al moverte con flechas; pasos limitados y trampas. |
| 8 | **Las brasas** | Tablero de **4×4**: tocar una brasa prende o apaga sus vecinas; hay que prenderlas todas con movimientos contados, y **cada 3 toques el viento apaga una al azar**. |
| 9 | **El Candado del Fogón** | Una clave de 4 símbolos al azar; intentos limitados; cada intento dice cuántos están bien puestos y cuántos están pero en otro lugar. (Reemplaza la idea de preguntas, que el dueño descartó.) |
| 10 | **El ritmo del Asador** | Mezcla: los golpes siguen un ciclo que hay que aprender y anticipar. |

Si las brasas no convencen al probarlas, la de repuesto es otro acertijo de lógica nuevo (el dueño pidió tener «alguna otra opción por las dudas»; el candado ya ocupa la Zona 9).

## El Asador

- Es **el único jefe** de este tramo (no hay guardianes en las mazmorras).
- **Más fuerte según la zona tope desde la que llegás** (vida y daño por zona; números a calibrar).
- **Desde la Zona 6 tiene una habilidad que tira al azar el 20 % de las veces, sin cooldown.** Cuál es la habilidad queda para el constructor.
- Se lo pelea solo, recién salido de la mazmorra, con la vida que te quedó.

## Restricciones del código actual (para el constructor)

- **El juego asume 5 zonas en varios lugares**: `MissionRewards.GoldPerHunt`, `Enchantments.DustPerAttempt` y `WorldBoss.WeaponLadder` son tablas de 5 valores (una zona 6 usaría en silencio los de la 5); `BoxCatalog.RequiredZoneRank` (la caja Mítica es «Zona 5»); `PvpTuning.AmuletDefenseInPvp` necesita fila para cada amuleto nuevo; `zone_boxes` necesita sus filas por zona (repeat, daily, weekly, prize, prize_top, travel) o el pago tira error.
- **La puerta**: `FogonRules.IsGateOpen` hoy dice «venció al jefe de la ÚLTIMA zona de la escalera»; pasa a ser «salió de la mazmorra de su zona tope» (y desde el FN 5 la Zona 5 ya no abre la puerta). `ZoneRanking.MaxUnlockedRank` y `/zona`, `/zonas` tienen que respetar el tope por Fuego Nuevo (una zona por encima del tope no existe para ese jugador).
- **Hoy entrar a la Zona 0 pide el Trinche y la Brasa puestos** (`FogonRules.CheckEntry`). En el camino nuevo se entra desde la mazmorra (ver «Preguntas abiertas»).
- `highest_zone_cleared` y `gate_cleared` vuelven a 0 con cada Fuego Nuevo: la escalera 6–10 se rehace cada vuelta, como la 1–5.
- El combate y el jefe en equipo viven en memoria (`ICombatSessionService`, `RaidSession`); la mazmorra también puede (un reinicio la corta: por eso la Reliquia se descuenta al terminar).
- Todo texto para el jugador en español con `Loc` y su inglés (`Locales/en.json`); `/info` necesita tema (o campo) para las mazmorras; anuncio de top.gg cuando salga.
- Las recetas viven en la base (`recipes`, `recipe_ingredients`), y todo ítem nuevo va en un script de `Database/` el mismo día (el problema recurrente de catálogo a mano).

## Lo que queda para el constructor

- **Números**, calibrados con el simulador (`tools/balance2`, el `CombatTurnResolver` real): stats de los monstruos y del Asador por zona tope, la escalera de equipo (×1,6 por zona como la 1–5), cantidades de las recetas (el «muchos materiales» del dueño, medido con `report_recipe_pacing.sql`), chance del drop y del ingrediente raro, vida que saca cada error de cada mazmorra, tamaño y pasos de cada acertijo.
- Nombres de zonas, monstruos, drops (sencillos y que se distingan), la Reliquia y los textos.
- **[mi decisión]** Las zonas 6–10 tienen arma de clase y amuleto como las 1–5 (la escalera de equipo sigue) además de la Reliquia; el dueño dijo «luego vemos si es llave, reliquia o arma sí o sí», así que esto se puede simplificar.
- Construirlo por partes: primero una zona (la 6) con su Reliquia, su mazmorra y el Asador escalado, probarla con el dueño, y después sumar 7 a 10.

## Preguntas abiertas (del dueño)

1. Desde el FN 5, ¿se sigue pidiendo el **Trinche y la Brasa del Fogón** para pelear al Asador, o lo reemplazan la mazmorra y el equipo de la zona tope?
2. En la pelea contra el Asador (después de la mazmorra), ¿se puede **comer una vez** como en los otros jefes, o nada?
