# Oficios (v0.13.0) — diseño

Pedido del dueño (2026-10-06): «Armate todo el `aa professions`. Me gusta arrancar con **Leñador, Minero y Encantador**. Cada comando sobre eso sube la XP de eso». Concepto aprobado antes en el spec de Fuego Nuevo (sección 7): tablitas por actividad hasta el nivel 100, que **se quedan al reiniciar** y **conviven con las bendiciones**, y al 100 una versión avanzada del comando. «Dale nomas» = construir con los números de abajo (todos son constantes en `GameData/ProfessionRules.cs`, se retocan en un solo lugar).

## Qué es

Tres oficios, cada uno con nivel **0 a 100**. El nivel sale de la XP, y la XP sale de **contar lo que ya cuenta el juego** (`player_stats`, el mismo contador de los logros): no hay tabla nueva ni migración, y el progreso nunca se desincroniza.

| Oficio | Comando | XP por uso | Contador |
|---|---|---|---|
| 🪓 Leñador | `/chop` | 10 | `chop` |
| ⛏️ Minero | `/mine` | 10 | `mine` |
| ✨ Encantador | `/enchant` (cada intento) | 50 | `enchant` |

- Como el contador ya existe desde la v0.6, **lo que cada jugador hizo antes cuenta** (retroactivo, igual que los logros).
- No se infla: `/chop` y `/mine` están frenados por su cooldown (5 min) y `/enchant` por el oro y el Polvo.
- Fuego Nuevo no los toca (`player_stats` queda).

## La curva

XP para pasar del nivel L−1 al L: `round(10 × (1 + 0,0176 × L^1,5))` → el nivel 1 son 10 XP (un uso) y el 100 son 186 (≈ 19 usos). Total al 100: **≈ 8.100 XP = ≈ 810 usos de `/chop` o `/mine`** (≈ 5 semanas jugando ~2 horas por día al ritmo del cooldown, el objetivo del spec madre) y **≈ 160 intentos de encantamiento** (un Soberano son ~33, así que es una o dos vueltas de equipo).

## Qué da cada nivel (lineal, siempre igual)

- **Leñador / Minero**: **+0,2 % de cantidad** por nivel (+20 % al 100) y **+0,1 % de chance de mejorar la rareza** por nivel (+10 % al 100): después de sortear la rareza, con esa chance sube un escalón (Común→Raro→Épico→Legendario; **el Mítico nunca se mejora**: su 0,5 % es la lotería de largo plazo y no se toca). Multiplica con Fuego Nuevo y bendiciones.
- **Encantador**: **−0,25 % de Polvo** por intento y por nivel (−25 % al 100). El oro no cambia.

## La versión avanzada (nivel 100)

- **Tala avanzada / Minería avanzada** (`/chop modo:Avanzada`, `aa chop avanzada`): **4 sorteos de una vez** (cada uno con su rareza y su cantidad), enfriamiento propio de **1 hora** (`chop_adv` / `mine_adv`, no pisa al normal). Es +33 % de ritmo de recolección para quien llegó al 100.
- **Encantamiento avanzado** (`/enchant modo:Avanzado`): **tira dos veces y se queda con el mejor tier**, a **1,5× el costo** (oro y Polvo, ya con el descuento). Sale más barato por chance de Soberano (5,9 % por 1,5 contra 3 % por 1).
- Cada uso avanzado cuenta como los usos que hace (4 y 2) para la XP y los contadores.

## Pantallas

- **`/professions`** (`aa professions`, alias `aa oficios`, `aa profesiones`, `aa prof`): un campo por oficio con nivel, barra de progreso, XP para el próximo, lo que da hoy y si la avanzada está lista. Efímero.
- Cada `/chop`, `/mine` y `/enchant` lleva al pie su oficio («🪓 Leñador nivel 12 — 340/412 XP»), y al subir de nivel lo festeja ahí mismo (sin banner público: sube de nivel cada 1 a 19 usos).
- `/profile`: una línea «🛠️ Oficios» solo si ya tiene alguno. `/cd`: las avanzadas aparecen solo al nivel 100. `/info tema:oficios`.

## Presupuesto (medido con `report_recipe_pacing.sql`, vía `pacing_mult.sql`)

Tiempo del camino de recetas de las 5 zonas (minutos de juego perfecto), con el drop de `/travel` en 40 %:

| Escenario | Camino | Contra la vuelta 1 |
|---|---|---|
| Vuelta 1 | 3.123 | 100 % |
| Vuelta 1 + oficios de recolección al 100 (con avanzada) | 2.738 | 88 % |
| Fuego Nuevo 10 | 1.515 | 49 % |
| Fuego Nuevo 10 + oficios al 100 | 1.390 | 45 % |
| Fuego Nuevo 30 | 983 | 31 % |
| Fuego Nuevo 30 + oficios al 100 | 930 | 30 % |

El camino está limitado por los **drops de los monstruos** (2.338 min contra 2.223 de recolección, en paralelo), así que mejorar la recolección mueve poco el total y el piso de ~30 % del spec madre se respeta. Donde sí se nota es en el **Polvo** (desmantelar da por unidad: +60 % al 100) y en lo que cuesta encantar.

## Fuera de esta versión

Más oficios (Cazador, Herrero, Mascotero…): se agregan como una fila en `ProfessionCatalog` si hace falta; logros por nivel de oficio; un banner público al llegar al 100.
