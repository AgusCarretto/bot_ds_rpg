# 🗺️ La Aventura — diseño (borrador para aprobar)

Estado: **aprobado y construido** (rama `feature/aventura`, 2026-11-16). Premio de 3 viajes **por zona**, sin comida, difícil a propósito.

## v0.59.0 — el pulido (el dueño, 2026-11-17)

- **Más difícil, para FN altos** (el dueño: «el camino difícil, un poquito más difícil que el travel normal… que pegue ×1,3, y el hunt mismo
  que pegue poquito más, tipo 1,2… la puede hacer cualquiera, pero si la quiere hacer alguien de zona o nivel bajo, que le cueste bastante»).
  Eligió que vaya **encima** del ×1,3 de antes y **solo en el daño**: el común del seguro pega **×1,56** (1,3 × 1,2) y el élite del arriesgado
  y de la emboscada **×1,69** (1,3 × 1,3); la vida de los dos sigue en ×1,3 (`AdventureRules.SafeDamageMultiplier` / `RiskyDamageMultiplier`,
  `SafeMonster` / `RiskyMonster`). **El Guardián: ×2,0 de vida** (era ×1,6; «agregale poquito de vida… a ×2 también, igual que el ataque») y ×2,0 de daño.
- **Medido** (`tools/balance2 -- O`, sin mascotas, FN, bendiciones ni dragón, así que pesimista; % que llega al final, siempre seguro / siempre arriesgado,
  según la clase): con el nivel mínimo de la zona +10, la Zona 2 pasa de 97–100 % / 87–97 % a **86–93 % / 22–51 %** y la Zona 3 de 72–80 % / 34–59 %
  a **11–39 % / 0–8 %**; con +20 niveles, la Zona 3 vuelve a 97–100 % / 54–77 % y la Zona 5 queda en 7–40 % / 0–6 %. En el nivel mínimo, la Zona 1
  baja de 89–95 % a 66–75 % por el seguro y desde la Zona 2 nadie llega (antes 0–6 %).
- **Por texto**: `aa av seguro`, `aa av arriesgado`, `aa av misterioso` (también `s` / `a` / `m`, `1` / `2` / `3`, `safe` / `risky` / `mystery`,
  `difícil`), y en la escena 5 cualquiera o `aa av guardian` (`AdventureRules.ParsePath`, `AventuraModule.ChooseByTextAsync`). Es la misma jugada
  que el botón; el resultado sale en un mensaje nuevo y los botones del viejo ya no valen. El pie de cada escena lo dice.
- **`/info tema:aventura`** (`adventure`; alias aventura, av, guardián, caminos, botín): caminos con sus multiplicadores, eventos con su chance
  (contada de `RollEvent`), botín y premio, que no se come, qué pasa si perdés o huís, y los comandos de texto. Todo sale de las constantes.
- **22 escenas** (10 nuevas: el molino, el cementerio, la tormenta, la estancia en ruinas, el arroyo seco, los tótems, el túnel de raíces,
  la carreta volcada, el lago quieto, la torre del vigía).
- **La primera escena** dice la zona y el **premio base** (los 3 viajes en su punto medio, con los bonus del jugador y el botín en +0 %;
  `CombatRewardCalculator.ExpectedTravelReward`). Se calcula antes de cobrar las 3 horas.
- **El resumen del final**: peleas ganadas, cada evento del camino misterioso que tocó (con cuántas veces) y el botín total, antes del oro, la EXP y la caja.
- **Misión semanal** `w_adv2` «Completá 2 aventuras» (25 cacerías de oro + 10 %, como «Forjá un ítem») y **logro «Aventurero»** (🗺️, 5 / 25 / 100
  aventuras completadas, premios estándar con cajas como «Racha»: el contador no se infla), en la página Combate, con 3 títulos (Andariego, Aventurero, Vencedor del Guardián). Los dos cuentan
  `adventure_win`. Sumar la misión al pozo cambia qué misiones semanales salen desde el release (como con la pesca en la v0.56.2).

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
4. **La escena 5 es siempre el Guardián del Camino**: el élite de tu zona con ×2,0 de vida y ×2,0 de daño (v0.59.0; empezó en ×1,6 de los dos).

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

- **Base: lo de 3 viajes** de oro y EXP (3 × la fórmula del `/travel`) **calculados con el nivel mínimo de la zona** (el dueño: «ni que hablar
  que el premio es por zona»), multiplicado por el botín (hasta ~×1,9). Quedarse en una zona fácil paga lo de esa zona.
  **Cada viaje incluye el bono de oro y EXP del monstruo de viaje de la zona** (como el `/travel` de verdad). Hasta la v0.57.1 se pasaba 0 y el
  «viaje» quedaba en lo que paga el nivel solo: el dueño notó que la Aventura daba mucho menos (2026-11-16). Medido: era el 34 % de un viaje
  real en la Zona 2, el 13 % en la 5 y el 4 % en la 10; desde la v0.57.2 paga los 3 viajes completos.
- **El Guardián pega un 25 % más desde la v0.57.2** (el dueño: «es muy fácil para la mayoría… tipo 1.25»): vida ×1,6 el élite como antes, daño
  ×2,0 (`AdventureRules.GuardianDamageMultiplier`). Medido con `tools/balance2 -- O` (equipo de la zona, sin encantamientos, mascotas, platos,
  dragón ni bendiciones, que los jugadores sí tienen, así que la realidad es más fácil): con +30 niveles sobre el mínimo, las zonas 1-5 siguen
  en ~75-100 % de llegada, pero la Zona 6 por el camino seguro baja de 65-91 % a 24-56 % según la clase y la 10 de 4-45 % a 0-14 %; con
  +15 niveles, la Zona 5 por el seguro baja de 66-90 % a 22-52 %. Si las zonas altas quedan imposibles, la palanca es ese número (o solo
  subirlo en las zonas bajas).
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

## Dificultad medida (`tools/balance2 -- O`, 2026-11-16)

Sin bonos (mascotas, Fuego Nuevo, bendiciones, dragón), así que arriba es pesimista. Siempre seguro, nivel mínimo de la zona + 5:
Zona 1 100 %; Zona 2 74–89 %; Zona 3 27–70 %; Zona 5 5–43 %; Zonas 6–10 casi nadie. La Zona 1 es trivial desde nivel ~6 aunque se
agranden los monstruos (son fijos y el jugador sube): por eso el premio es por zona. El dueño (2026-11-16) **lo quiere difícil así**:
«los FN bajos bajan de zona a hacer lo más fácil; los FN altos pueden arriesgarse con las más altas». Se ajusta con jugadores reales.
