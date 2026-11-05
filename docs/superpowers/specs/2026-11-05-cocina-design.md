# La cocina: `/cook` (diseño, 2026-11-05)

Etapa 2 de la pesca. Pedido del dueño (2026-11-05): «el comando puede ser cook y las recetas con cook recipes… que sean cosas caras, mats legendarios y épicos, con fishes varios… pensá en cualquiera y que sea algo que ayude a futuro»; los permanentes **se apilan** («en FN 5 comés 10 Surubí al Disco y tenés +100 de vida máx; en el FN 6 esos bufos no están»), «el plato se cocina y se come en el momento», «nada de cargar el perfil», y un comando `aa stats` con las cosas actuales.

## Reglas

- **Comandos**: `/cook plato:<…> veces:<1-10>` y `aa cook <plato> [veces]`; sin plato muestra las recetas (`aa cook recipes`, una pantalla con cada plato: efecto, ingredientes con tenés/necesitás y las horas aproximadas NO se muestran). Autocompletado del plato en la barra.
- **Cocinar = comer en el momento.** No existe el ítem «plato»: no hay mochila, ni `/use`, ni pestaña del inventario. Cada ejecución es UNA transacción: bloquea la fila del jugador, descuenta cada ingrediente con el `UPDATE … WHERE quantity >= n` de siempre (si falta algo, se deshace TODO y dice qué falta) y suma el efecto. 12 pedidos a la vez no dejan nada en negativo.
- **Combustible**: todo plato gasta Carbón (un ingrediente más del plato, mostrado aparte como «combustible»). Máximo 4 tipos de ingrediente además del combustible.
- **Solo contra monstruos** (como los encantamientos Vida/Esquive y el dragón): entra por `PlayerBonuses`, que los duelos y la Arena nunca reciben. No toca ningún cooldown.
- **Fuego Nuevo**: borra todo lo cocinado, permanente y de tiempo (junto con la mochila). El aviso previo del `/fuegonuevo` lo cuenta.

## Los platos permanentes (se apilan sin tope, hasta el próximo Fuego Nuevo)

Tabla `player_cooked(discord_id, dish_key, count)`; el bonus es `count × lo que suma cada plato`. Sin tope y sin costo creciente (el dueño: «si lo quieren repetir lo hagan muchas veces y chau»).

| Plato | Cada plato suma | Ingredientes (+ combustible) | ≈ h (FN 7, Zona 3 ×2) |
|---|---|---|---|
| Surubí al Disco | +10 vida máx | 7 Surubí, 2 Zafiro, 6 Oro Puro + 16 Carbón | 4,8 |
| Dorado a la Parrilla | +1 DEF | 40 Dorado, 1 Surubí, 4 Ébano, 8 Nogal + 20 Carbón | 6,2 |
| Mojarras en Escabeche | +2 ATQ | 150 Mojarra, 10 Dorado, 3 Ébano, 3 Zafiro + 20 Carbón | 5,1 |

La DEF es la más cara a propósito: se resta plana de cada golpe (+1 DEF ≈ +1 % de vida efectiva contra un jefe). El ATQ +2 es casi invisible en zonas altas (≈ +0,2 % en la 10): si se ve inútil, subirlo es un número en `CookingCatalog`. Con la DEF sin tope la palanca, si un FN muy alto la dispara, es un tope por vuelta.

## Los platos con tiempo (60 minutos)

Tabla `player_cook_buffs(discord_id, dish_key, expires_at)`. **Se acumulan**: varios platos distintos activos a la vez, y comer el mismo otra vez SUMA 60 minutos (`GREATEST(expires_at, now()) + 60 min`, con el reloj de la base). El efecto no se multiplica.

| Plato | Efecto | Ingredientes (+ combustible) | ≈ h |
|---|---|---|---|
| Empanada de Surubí | +20 % oro en peleas | 3 Surubí, 1 Zafiro, 2 Oro Puro + 6 Carbón | 2,0 |
| Locro del Pescador | +20 % EXP en peleas | 20 Dorado, 1 Ébano, 4 Nogal + 6 Carbón | 1,9 |
| Chupín de Dorado | +15 % chance de drop (`/hunt`, `/travel`) | 15 Dorado, 2 Surubí, 3 Nogal + 6 Carbón | 1,5 |
| Pastel de Mojarra | +25 % cantidad en `/chop`, `/mine`, `/fish` | 60 Mojarra, 1 Zafiro, 1 Ébano + 4 Carbón | 1,5 |
| Brocheta de Dorado | +10 puntos de crítico | 10 Dorado, 2 Oro Puro, 1 Zafiro + 4 Carbón | 1,9 |
| Surubí a la Manteca | +10 puntos de esquive | 3 Surubí, 6 Dorado, 1 Ébano + 4 Carbón | 1,4 |
| Caldo del Último Aliento | sobrevivís un golpe mortal con 1 HP (una vez por pelea) | 6 Surubí, 2 Zafiro, 2 Ébano + 10 Carbón | 3,1 |

El Chupín es el más flojo (+15 % relativo sobre un 6 % son 0,9 puntos); el Caldo es el más fuerte (cambia cómo se juega un jefe). Los dos son los candidatos a retocar.

## Dónde se engancha (sin tocar el combate)

- `PlayerBonusService.GetAsync` lee las dos tablas (una consulta cada una; nunca tira, como el resto) y las pone en `PlayerBonuses`: `CookedMaxHp`, `CookedDefense`, `CookedAttack` (sumas planas) y los multiplicadores de oro / EXP / drop / cantidad, los puntos de crítico y esquive y el «último aliento».
- `PlayerCombatProfileCalculator.Resolve` suma lo plano SOLO cuando `bonuses` no es null (la misma llave que `PveTuning` y los encantamientos), y la vida entra por la cuenta de vida de combate (`HpDisplay`, así lo que se ve es lo que pelea).
- Los ingredientes y los platos son datos en código (`GameData/CookingCatalog.cs`, puro y probable): son 10 platos con efectos atados al código, no una tabla que se edite a mano. Los ítems (peces, minerales, maderas) ya existen en el catálogo.

## `/stats` y `aa stats` (la ficha)

Pantalla nueva, sin tocar el perfil: ATQ, DEF, vida máx, crítico y esquive con **cuánto aporta cada cosa** (nivel, arma con su encantamiento, amuleto con el suyo, dragón, bendiciones, banquete con minutos, platos), los platos activos con el tiempo que les queda, el dragón, las mascotas y los porcentajes del Fuego Nuevo. `aa equipamiento` ya es el atajo del Carpintero (`aa tools`), así que NO se usa. Se permite en plena pelea (solo mira; entra en la lista de `FightLock`) y `cook` queda bloqueado.

## El perfil

El % del dragón sale de la línea de ATQ (el número ya lo incluye); la marca 🔥 queda solo para la bendición de ataque. Los platos tampoco llevan marca: suman en los números. El ✨ del encantamiento y el 🍖 del banquete quedan como estaban.

## Ayuda y traducción

Un campo 🍳 en `/info tema:gather` (alias `cook`, `cocinar`, `plato`): no se crea un tema nuevo (con 28 el autocompletado ya recorta). Todo en español e inglés (`Locales/en.json`, `tools/loc --check` en 0).

## No se hace ahora

El oficio Cocinero, buffs nuevos de otros tipos, sacar la comida de la tienda, platos como ítems de mochila, un tope o un costo creciente (hay palanca si hace falta).

## Pruebas

Un arnés `cooktest`: la tabla de platos, cada receta contra la base local (ingredientes descontados, falta de uno = nada cambia, 12 pedidos a la vez), el apilado (10 platos = +100 de vida), el reloj que suma, el Fuego Nuevo que borra, que los duelos no los reciban, `/stats` y el perfil sin las marcas.
