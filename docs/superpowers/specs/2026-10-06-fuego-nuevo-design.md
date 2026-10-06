# Fuego Nuevo, El Fogón Eterno, bendiciones y oficios — diseño

_2026-10-06. Diseño acordado en la conversación con el dueño; este documento es lo que hay que revisar antes de escribir el plan de la v0.11. Todo lo marcado «supuesto» es una decisión mía que él puede cambiar._

## 1. Para qué

El juego tiene 5 zonas y un final. Con 4 jugadores reales (niveles 23, 23, 14 y 2 el 2026-10-06) los dos mejores ya vencieron al jefe de la Zona 3 en 6 días: el primero puede llegar al final de la escalera en más o menos una semana.
Hace falta (a) un objetivo para quien termina la Zona 5, (b) una razón para volver a empezar y (c) que cada vuelta se sienta mejor que la anterior sin romper la dificultad calibrada.

## 2. Principios

1. **La vuelta acelera; el techo lo sube el contenido.** Los bonos de Fuego Nuevo son de velocidad (más drop, más cantidad, más EXP). Los pocos bonos de poder (algunas bendiciones) son chicos, con tope, y se miden con el resolvedor real.
2. **Nada de lo permanente cuenta en el PvP** (mascotas, bendiciones, % de Fuego Nuevo, oficios): los duelos y la Arena se juegan con nivel + equipo + clase, como hoy.
3. **Nada que borre progreso se hace sin confirmación y sin una sola transacción** (`FOR UPDATE` sobre el jugador, contador esperado en la guarda: un doble click renace una sola vez).
4. **Un presupuesto de multiplicadores** (sección 8): se fija el tope total por actividad y después se reparte; si cada pieza se calcula sola, el techo se pasa en silencio.

## 3. El camino al reinicio: Zona 0, El Fogón Eterno

- **Se abre** cuando el jugador vence al jefe de la Zona 5 (`highest_zone_cleared` ≥ la última zona normal).
- **Para entrar** (`/zona 0`, en texto `aa zona 0`) hay que tener PUESTOS el **arma** y el **amuleto del Fogón**: uno solo de cada uno, iguales para todas las clases, sin sinergia de clase. Se forjan en la herrería como cualquier equipo (receta en la base) y cuestan «muchísimos materiales».
  - Estadísticas (a calibrar): arma ≈ +128 ATQ (Zona 5 × 1,6, un poco más que el arma de clase de Zona 5 que da +120), amuleto ≈ +120 DEF (75 × 1,6). Rareza: Mítico.
  - Costo (supuesto): ~500 minutos de juego perfecto por pieza (el amuleto de Zona 5 pide ~200), con ingredientes de **todas las zonas** más los materiales Míticos (Corteza del Árbol de Vida, Fragmento de Meteorito). Se mide con `report_recipe_pacing.sql`.
  - Como el equipo va en el slot, hay que vender el de Zona 5 (el encantamiento se pierde: es la regla de siempre). Es el último equipo y el reinicio lo borra de todos modos.
- **Ahí solo se puede `/boss`**: `/hunt`, `/travel` y `/raid` dicen que en el Fogón no hay nada más que el Asador. Jefe: **El Asador Eterno**, calibrado con `calibsim` (el examen: ~70-85 % de derrota con el equipo de Zona 5, ~16 % con el del Fogón, como cada jefe de la escalera). Usa el mismo cooldown del jefe (5 h si gana, 30 min si pierde, huye o se acaba el tiempo).
- **Al ganar**: el jugador vuelve a la Zona 5, se marca `gate_cleared` y se le avisa que **Fuego Nuevo está disponible**. Es voluntario: puede quedarse en la 5 con el mejor equipo del juego. Logro nuevo «Asador» (vencerlo).
- **Cada vuelta vuelve a pasar por el Fogón** (el equipo se pierde al reiniciar, hay que forjarlo de nuevo).
- **Cómo se representa**: una fila de `zones` con `kind = 'gate'` y `zone_id = 0`. `IZoneRepository.GetAllAsync` devuelve solo las zonas normales, así que `/zonas`, la escalera, `ZoneRanking`, las recetas por zona, los drops, las cajas y las mascotas **no la ven** y toda la calibración queda intacta; `GetGateAsync` la trae para `/zona 0`, `/boss` y una línea en `/zonas` («🔥 Zona 0: El Fogón Eterno»). `renumber_zones_consecutively.sql` la ignora.

## 4. Fuego Nuevo (el reinicio)

- **Nombre**: Fuego Nuevo. Comando `/fuegonuevo`, en texto `aa fuegonuevo` (alias `aa fn`). Cada reinicio sube `users.fuego_nuevo` (0 = nunca; «FN 3» = tres reinicios).
- **Pantalla de confirmación**: lista lo que se va con sus cantidades (materiales, cajas, Polvo, nivel, equipo con sus encantamientos) y lo que se queda, y pide un botón de confirmación del dueño. No se puede en combate ni en raid.
- **Se queda**: mascotas, logros (y sus reclamos), misiones reclamadas, colección de trofeos, historial, **oro en mano y oro del banco** (y la cuenta del banco), los oficios (sección 7) y las bendiciones.
- **Se va**: nivel y EXP (vuelve a 1), zona actual y zonas vencidas (`highest_zone_cleared = 0`, zona 1), arma y amuleto con sus encantamientos, **todos los materiales y drops, las cajas sin abrir y el Polvo, sin conversión ni reembolso** (decisión del dueño), buffs activos, cooldowns de combate. HP al máximo. `gate_cleared` vuelve a falso.
- **Clase**: se elige de nuevo (era la idea vieja de «bloquear el cambio de clase hasta el reinicio»: `/class` pasa a funcionar solo en el reinicio y en el primer `/start`).
- **Lo que recibe** (cada reinicio):
  1. el **% fijo** (sección 5), igual para todos;
  2. **una bendición** (sección 6);
  3. un historial (`fuego_nuevo_history`: número, clase antes y después, tiempo que tardó la vuelta, nivel) y el título «Fuego Nuevo ×N» en el perfil;
  4. en el reinicio 3 y el 5, el desbloqueo de la **fusión de mascotas** (idea del dueño; se diseña aparte, hoy solo se reserva el lugar).
- **Concurrencia**: UNA transacción: bloquear la fila del jugador, comprobar `gate_cleared` y el contador esperado, borrar inventario/cajas/Polvo, reiniciar el jugador, sumar el contador, anotar el historial y crear la oferta de bendición. Doble click o dos instancias = un solo reinicio.

## 5. Los porcentajes de cada vuelta

Decisión del dueño: suaves, **siempre iguales** (cada Fuego Nuevo suma lo mismo, así el FN 10 se nota muchísimo contra el FN 1) y **un % distinto para cada cosa**. Cada paso es una constante, y suma sobre la base (FN N = base × (1 + paso × N), no se compone).
**Valores acordados el 2026-10-06:**

| Cosa | Qué sube | Paso por FN | FN 1 | FN 5 | FN 10 | FN 20 |
|---|---|---|---|---|---|---|
| `/hunt` | la **chance** de que caiga el drop del monstruo (6 % de base) | ×1,30 | 7,8 % | 15 % | 24 % | 42 % |
| `/travel` | la **chance** del drop (60 % de base), con tope en 100 % | ×1,05 | 63 % | 75 % | 90 % | 100 % |
| `/chop` y `/mine` | la **cantidad** de unidades por acción (se redondea al azar para que sea exacta en promedio) | ×1,20 | ×1,2 | ×2 | ×3 | ×5 |
| EXP de las peleas (hunt, travel, boss, raid, autohunt) | la EXP | +10 % (supuesto, no confirmado) | +10 % | +50 % | +100 % | +200 % |
| Oro | — | sin % (el oro ya se queda entre vueltas y se acumularía sin parar) | | | | |

- **El cofre del jefe no cambia** (igual que con las mascotas). Los % del Fuego Nuevo se **multiplican** con los de las mascotas (la Salamandrita al máximo suma +6 % relativo a la chance de cacería y de viaje).
- **Efecto en las recetas**, medido con `report_recipe_pacing.sql` (camino del arma de clase y el amuleto de las 5 zonas, promedio de las 4 clases, uno después del otro; el script da ~2.480 min = ~41 h en la vuelta 1, los objetivos escritos en `CLAUDE.md` daban menos):

| FN | 0 | 1 | 2 | 3 | 5 | 7 | 10 | 14 | 20 | 30 |
|---|---|---|---|---|---|---|---|---|---|---|
| Tiempo vs. FN 0 | 100 % | 89 % | 81 % | 75 % | 65 % | 59 % | 51 % | 44 % | 42 % | 40 % |
| Horas | 41 | 37 | 33 | 31 | 27 | 24 | 21 | 18 | 17 | 16 |

- **El tope de `/travel`**: con +5 % por FN la chance llega a 100 % en el FN 14, y desde ahí las piezas que piden el drop de viaje (armas de clase y amuletos) solo mejoran con otras fuentes. Por eso el tiempo total se aplana en ~40-44 %. Es lo esperado: deja espacio para oficios y bendiciones. Antes del FN 14 se decide si lo que sobre de 100 % se convierte en un segundo drop por viaje (supuesto, no hace falta todavía).
- Sin tope general; si hace falta un techo se agrega después sin romper nada.
- Se aplican en los mismos puntos que las mascotas: `CombatRewardCalculator` (drop y EXP, que ya recibe un parámetro de bonus) y `GatheringYield` (hoy `RunMultiplier` es un entero fijo en 1; pasa a factor decimal con redondeo probabilístico).

## 6. Bendiciones

- **Una por Fuego Nuevo**, elegida entre **3 sorteadas** del pool. La oferta queda guardada en la base (si el jugador cierra Discord, la encuentra al volver con `/fuegonuevo` o `/blessings`).
- **Variadas, a pedido del dueño** (un ítem, una mejora permanente, algo de ATQ, algo de mascotas...). Cada una tiene niveles **I a V**: si sale de nuevo y la elegís, sube de nivel (el pool no tiene que ser enorme para que alcancen muchas vueltas).
- **Categorías y ejemplos** (nombres y números son un borrador; se calibran):

| Categoría | Ejemplo | Efecto por nivel |
|---|---|---|
| Velocidad | Mano de Leñador / Pico Fino | +5 % de cantidad en `/chop` / `/mine` |
| Velocidad | Brasa Vieja | +5 % de drop en `/travel` |
| Poder (chico, con tope) | Filo Antiguo | +1 % de ATQ total (tope V: +5 %) |
| Poder (chico, con tope) | Piel Curtida | +1 % de DEF total (tope V: +5 %) |
| Poder (chico, con tope) | Corazón de Brasa | +2 % de vida máxima (tope V: +10 %) |
| Mascotas | Manada | +10 % al bonus de todas las mascotas |
| Mascotas | Buen Pienso | −5 min al cooldown de alimentar a cada mascota |
| Utilidad | Buen Mate | la comida cura +10 % |
| Ítems | Alforja del Fogonero | al renacer, empezás con 3 Comidas para Mascotas y un cofre de Zona 1 (por nivel, más cosas) |
| Cosmética | Brasa de Color | un título o color de perfil nuevo |

- **Reglas**: ninguna bendición cuenta en duelos ni Arena. Las de poder se miden con `calibsim` (los topes de arriba suman como mucho +5 % ATQ, +5 % DEF y +10 % de vida; con las mascotas y los encantamientos hay que volver a comprobar que la escalera aguanta). Nada de cooldowns más cortos (todo está calibrado por minuto).

## 7. Oficios (diseño aparte, v0.13)

Idea del dueño: tablitas como las de logros, una por actividad (Leñador, Minero, Cazador, Herrero, Encantador, ...), que se llenan con la XP de esa actividad hasta el **nivel 100**. Cada nivel sube un % chico de sacar un drop mejor, más o algo extra; al 100 se desbloquea una versión avanzada del comando (por ejemplo `/chop avanzado`). Decidido: **se quedan al reiniciar** (como los logros) y **conviven con las bendiciones**.
Queda para su propio diseño: la curva de XP (objetivo: ~4-6 semanas de uso regular para llegar al 100), el efecto exacto por nivel, el cooldown y lo que da la versión avanzada, y el reparto del presupuesto.

## 8. El presupuesto de multiplicadores

Cada actividad tiene un multiplicador total = (1 + Fuego Nuevo) × (1 + oficio) × (1 + mascotas) × (1 + bendiciones) (los de poder aparte). Antes de cada versión se calcula el tiempo de recetas resultante con `report_recipe_pacing.sql` en tres casos (jugador nuevo, FN 10 con oficios en 50, y el techo teórico) y se compara con el objetivo: **el camino de recetas de las 5 zonas no baja de ~30 % del tiempo de la vuelta 1**, para que la escalera siga teniendo sentido. Solo con Fuego Nuevo (sección 5) el piso queda en ~40-44 %: los ~10-14 puntos que sobran son el presupuesto de los oficios, las bendiciones y las mascotas. Los topes de cada fuente salen de ahí.

## 9. Cambios técnicos

- **Base** (v0.11): `zones.kind`; fila de la Zona 0; items del Fogón (arma, amuleto) y sus recetas; el monstruo El Asador Eterno; `users.gate_cleared`. (v0.12): `users.fuego_nuevo`, `fuego_nuevo_history`, `player_blessings`, `blessing_offers`. Todo en un `add_*.sql` re-ejecutable + `schema.sql` + `run_fresh_install.sql`, y la comparación instalación limpia vs. base real.
- **Código «atado a 5 zonas»** (hoy una zona 6 usaría en silencio los números de la 5): `MissionRewards.GoldPerHunt` y `Enchantments.DustPerAttempt` son arrays de 5 con `Math.Clamp`; los tiers de cajas y rarezas son 5; las mascotas son una por zona. Para la v0.11 alcanza con una **validación al arrancar** que avise si hay más zonas normales que filas en esas tablas (no se agrega una Zona 6 todavía); la Zona 0 no cuenta porque no entra en la escalera.
- **Los arneses de prueba van al repo** (hoy viven en una carpeta temporal): proyectos en `tests/` con referencia al proyecto principal y un script que los corre todos; las pruebas que escriben en la base se pasan a una base descartable (`asado_test`, creada con `run_fresh_install.sql`) en vez de la base real.
- **Puntos de enganche**: `ZoneModule` (`/zona 0`), `AdventureCombatStarter.PrepareBossAsync` (jefe de la puerta, sin el cálculo de nivel de la zona siguiente), `AdventureRepository.ApplyBossVictoryAsync` (volver a la 5 y marcar `gate_cleared` en la MISMA transacción, y no subir `highest_zone_cleared` con la zona 0), `CombatRewardCalculator` y `GatheringYield` (v0.12), `PlayerCombatProfileCalculator` (bendiciones de poder, v0.12).

## 10. Versiones

1. **v0.11 — El Fogón Eterno**: zona puerta, equipo, Asador Eterno, `/zona 0`, logro, ayuda (`/info tema:fogon`), validación de tablas por zona, tests al repo. Al vencerlo avisa «Fuego Nuevo llega en la próxima versión».
2. **v0.12 — Fuego Nuevo v1**: el reinicio, los % por vuelta, bendiciones, `/class` bloqueado fuera del reinicio, historial.
3. **v0.13 — Oficios** (con su propio diseño).
4. Después, según los datos: fusión de mascotas (FN 3 y 5), Zona 6, desafíos opcionales de vuelta («sin comida en las peleas»).

## 11. Supuestos y preguntas abiertas

- El Polvo se va con el reinicio (el dueño dijo «nada de polvo ni nada»). La pantalla de confirmación lo muestra para que nadie se sorprenda.
- Costo y estadísticas del equipo del Fogón y los números de las bendiciones son un borrador: salen de `report_recipe_pacing.sql` y `calibsim`. Los pasos de la sección 5 (hunt ×1,30, travel ×1,05, chop/mine ×1,20) los fijó el dueño; el de EXP (+10 %) es mío.
- Sin tope general en los % de Fuego Nuevo; solo el tope natural de 100 % en la chance de `/travel` (FN 14).
- El oro se queda y no tiene % de vuelta; hay que vigilar que haya en qué gastarlo (cajas de 120.000, comida de mascotas, encantamientos).
- Riesgo principal: que la vuelta 2 sea solo repetir; lo mitigan la clase nueva, las bendiciones, los oficios y, más adelante, la Zona 6 y los desafíos.
