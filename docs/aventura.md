# 🗺️ La Aventura — diseño (borrador para aprobar)

Estado: **aprobado por el dueño el 2026-11-16** (premio de 3 viajes, sin comida, difícil); los números marcados **(a medir)** son propuesta.

## Lo que decidió el dueño

- **Una sola aventura para todas las zonas** (también las 6 a 10). La dificultad es **por zona, no por nivel**: los monstruos salen
  del plantel de la zona donde estás parado, con su vida y su daño de esa zona.
- **Cada 3 horas.** El cooldown se cobra al arrancar y queda igual ganes o pierdas.
- **Paga más oro y más EXP** que un viaje.
- **Si morís:** la penalidad de siempre (la misma que en cualquier pelea) y **no cobrás el premio**.
- **Las decisiones cambian algo de verdad.**
- **Sin capítulos de historia** por ahora.

## Cómo se juega

`/aventura` o `aa aventura` (`aa adventure`, `aa av`). Se recorre en **un solo mensaje** que se va editando, con botones.

1. **5 escenas seguidas.** Cada escena tiene un texto corto y **2 o 3 caminos** para elegir.
2. **La vida pasa de escena en escena** (la vida real del jugador) y **NO se puede comer** (el dueño, 2026-11-16: en zonas bajas una comida de 200 HP te cura todo). Nada cura entre escenas salvo la fogata.
3. **Las peleas** son las de siempre (Atacar / Habilidad), con el mismo motor del `/travel`. Si **huís**, la aventura termina sin
   premio y sin penalidad.
4. **La escena 5 es siempre el Guardián del Camino**: el élite de tu zona con ×1,6 de vida y daño (**a medir**).

**Tiene que ser difícil** (el dueño: «ir siempre a la segura y llegar al final en zona 1 nivel 1 te daría DEMASIADO»): los monstruos de la
Aventura (comunes y élites) tienen **×1,3 de vida y daño** respecto de los de afuera (**a medir**). Antes de que el dueño la pruebe se mide
cuántas corridas llegan al final con el equipo de cada zona y se endurece si hace falta.
5. Si llegás con vida al final, cobrás.

## Los caminos (por qué las decisiones importan)

Cada escena ofrece caminos de tres tipos, con el riesgo a la vista:

| Camino | Qué pasa | Botín |
|---|---|---|
| 🛡️ **Seguro** | Una pelea contra un monstruo común de tu zona | +0 % |
| ⚔️ **Arriesgado** | Una pelea contra el élite de tu zona | **+20 %** |
| ❓ **Misterioso** | Un evento al azar (bueno o malo) | según el evento |

El **botín** se va sumando y multiplica el premio final: tres caminos arriesgados = +60 % de oro y EXP.

**Eventos del camino misterioso** (al azar):

| Evento | Efecto |
|---|---|
| 🧰 Cofre abandonado | +30 % de botín |
| 🔥 Fogata | Recuperás el 30 % de la vida |
| 🕸️ Trampa | Perdés el 15 % de la vida |
| ⛩️ Santuario | +10 % de daño por el resto de la aventura |
| 👤 Emboscada | Una pelea contra el élite, sin el +20 % |

## El premio (a medir)

- **Base: lo de 3 viajes** de oro y EXP (3 × la fórmula del `/travel`), multiplicado por el botín (hasta ~×1,9).
- **Chance de la caja de tu zona** (la del viaje, `zone_boxes`): 25 %.
- Para comparar: en 3 horas, el `/travel` paga 6 viajes si no te perdés ninguno; la Aventura es **además** del viaje, no en
  lugar de él.

## Textos

Una tabla de ~12 escenas con su texto (el bosque, el puente roto, la cueva, el río helado, las ruinas…), que salen en orden
al azar; el nombre del monstruo de la pelea se mete en el texto. Todo pasa por `Loc.Mark` (también en inglés).

## Pantallas

- **Escena:** título «🗺️ Aventura — escena 2/5», el texto, tu vida (barra), el botín acumulado y los botones de los caminos.
- **Pelea:** la pantalla de pelea de siempre, con «escena 2/5» arriba.
- **Final:** lo que cobraste (oro, EXP, caja) o la derrota.
- `/cd` suma la línea 🗺️ **Aventura** y hay recordatorio, como los demás.

## Lo técnico (para mí)

- Corrida en memoria con su candado (como `DungeonSession`), reloj por jugada de 3 minutos, ids de botón con el dueño.
- Peleas con `CombatTurnResolver`/`CombatState`; bloqueo de comandos con `FightGuard`/`FightLock`.
- `CooldownCatalog.Adventure` (3 h), `GameEventKinds.AdventureWin`, misión/logro más adelante.
