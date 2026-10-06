# Asado y Acero RPG — Estado y mejoras pendientes

_Última revisión: 2026-10-06 (v0.12.0: Fuego Nuevo y bendiciones)_

## Fuego Nuevo y bendiciones (v0.12.0, 2026-10-06)

- **Qué es**: el reinicio voluntario que se habilita al vencer al Asador Eterno (`/fuegonuevo`, `aa fn`). Pierdas nivel, equipo, materiales y Polvo; ganás porcentajes **permanentes** y una **bendición** por vuelta. Diseño aprobado por el dueño: `docs/superpowers/specs/2026-10-06-fuego-nuevo-design.md`. Detalle técnico en `CLAUDE.md` (párrafo «Fuego Nuevo»).
- **Porcentajes** (lineales por vuelta, así el FN 10 se nota muchísimo contra el 1): chance de drop de `/hunt` +30 % de la base por Fuego Nuevo, la de `/travel` +10 % bajando 0,5 puntos por vuelta (piso +2 %), cantidad de `/chop` y `/mine` +20 %, EXP +10 %. El oro y el cofre del jefe no se tocan. Medido con el script de ritmo: el camino de recetas de las 5 zonas queda en ~49 % del tiempo de la vuelta 1 en el FN 10 y ~34 % en el FN 30.
- **Bendiciones**: 13, de a una por vuelta entre 3 sorteadas, niveles I–V al repetir. De velocidad, de poder chico (solo contra monstruos), de mascotas, oro, ítems (Alforja del Fogonero) y cosmética (Brasa de Color). `/blessings` las muestra.
- **Decisiones mías que hay que confirmar con el dueño**: (1) los **huevos y la comida de mascotas se quedan** al reiniciar (él dijo «mascotas se quedan»; perder un huevo sin abrir me pareció castigar justo eso); (2) **`/class` solo con nivel 1 y 0 de EXP** y, después, con cada Fuego Nuevo; (3) el paso de EXP (**+10 % por vuelta**) lo puse yo: él fijó hunt, travel, chop y mine; (4) la **forma del paso de `/travel`** (arranca en +10 % y baja 0,5 puntos por vuelta): él dijo «que arranque en 1,1 y vaya bajando por fn»; (5) saqué «Buen Mate» (curación) del pool de bendiciones para no ampliar la superficie.
- **Para desplegar**: correr `Database/add_fuego_nuevo.sql` ANTES de arrancar el bot nuevo (`DEPLOY.md`). La base viva ya lo tiene aplicado desde el desarrollo; un bot v0.11.x que siga corriendo hasta reiniciarse no usa las columnas nuevas (no se rompe).
- **Probado**: arnés `fntest` (141 chequeos contra la base real con un usuario descartable: reglas, matemática de recompensas y recolección medida, perfil de combate, el reinicio y la elección transaccionales, 20 reinicios y 20 clicks a la vez, la Alforja, mascotas con Buen Pienso, `/class`, pantallas dentro de los límites de Discord, ruteo de botones con el `InteractionService` real y armado de TODOS los módulos con la inyección de dependencias) y los 29 arneses de antes.
- **Falta / ideas**: los **Oficios** (tablas de nivel por actividad hasta el 100, con «chop avanzado» al llegar; v0.13, necesita su propio diseño); el chequeo de «no estás en combate» del reinicio es en memoria y no es atómico con la transacción (aceptado: un combate solo toca la base con deltas de vida); íconos propios para las bendiciones; un anuncio público del Fuego Nuevo en el canal (hoy la pantalla es solo para quien la usa).

## El Fogón Eterno, la «Zona 0» (v0.11.0, 2026-10-06)

- Es el primer paso del diseño aprobado de Fuego Nuevo (`docs/superpowers/specs/2026-10-06-fuego-nuevo-design.md`): **una puerta al final del mundo** que se abre al vencer al jefe de la última zona. Ganarle al jefe de la puerta es lo que va a habilitar el reinicio (Fuego Nuevo, v0.12); hoy solo deja el aviso y el logro.
- **Cómo se entra**: `/zona 0` (`aa zona 0`; aparece en `/zonas` y en el autocompletado recién con la puerta abierta). Hace falta nivel **25** y llevar **PUESTOS** el **Trinche del Asador Eterno** (+128 ATQ) y la **Brasa del Fogón Eterno** (+120 DEF): uno solo de cada uno, iguales para todas las clases (sin sinergia: el salto ×1,6 sobre el equipo de Zona 5).
  Como el equipo va en su casillero, hay que vender primero el de Zona 5 (el encantamiento se pierde; el Fuego Nuevo lo borra igual).
- **El equipo es lo más caro del juego**: cada pieza pide los **10 drops de CACERÍA de las 5 zonas ×5**, madera/mineral Raros y Legendarios (Madera de Ébano ×4 + Hierro ×18 el arma; Gema de Zafiro ×4 + Carbón ×18 el amuleto) y **25.000 de oro**. Sin materiales Míticos (el 0,5 % por acción es una lotería demasiado cruel para exigirla). Medido con `report_recipe_pacing.sql -v gate=1`: ~14 h de juego perfecto (el
  script muestra 571 min porque no cuenta que los drops hay que juntarlos zona por zona: 5 × 167 min). Aparece en el menú de la herrería, en la página de recetas de la última zona (su propio bloque «🔥 El Fogón Eterno») y en `/tips`, solo con la puerta abierta. Las zonas anteriores vuelven a servir: hay que volver a cazar en las 5.
- **Adentro solo hay `/boss`**: contra **El Asador Eterno** (no hay cacería, viajes ni raid; `/autohunt` tampoco). Usa el cooldown del jefe (5 h si gana, 30 min si pierde, huye o se acaba el tiempo) y la penalidad por morir de siempre. **Calibrado con el resolvedor real a nivel 28** (el examen de cada jefe): con el equipo del Fogón pierde ~16 %, con el arma de clase y el amuleto de Zona 5 ~75 %. HP 2002-2401, daño 188-244, oro y EXP 1500/1500, sin drops.
- **Ganarle** (en la MISMA transacción que el premio): `gate_cleared`, sale de la puerta y vuelve a la última zona (no toca `highest_zone_cleared`); la victoria dice que se habilitó el Fuego Nuevo y que todavía no está (llega en la v0.12). Evento `gate_win` y logro **Asador** (1/3/10, solo oro y XP; ahora son **20 logros**). También se avisa «se abrió El Fogón Eterno» la PRIMERA vez que vence al jefe de la última zona.
- **Cómo no rompe lo demás**: la puerta es una fila de `zones` (id 0, `kind = 'gate'`) que `IZoneRepository.GetAllAsync` y `IMonsterRepository.GetAllAsync` no devuelven, y el jugador nunca tiene `current_zone_id = 0` (`users.in_gate` dice que está parado ahí): la escalera, los drops, las cajas, las mascotas, el trueque y todo lo que mira la zona actual no se enteran. Los scripts SQL que recorren zonas la ignoran (`zone_id > 0`).
- **Probado** (arnés `fogontest`, 60+ chequeos contra la base real con un usuario descartable, y los 28 arneses de antes): la puerta y la escalera, las recetas, entrar (sin abrir / nivel / equipo / ok / ya estoy / salir), `/hunt` `/travel` `/raid` bloqueados, `/boss` con y sin equipo, la victoria transaccional con 6 a la vez, `Opened` solo la primera vez, el menú de la herrería, los límites de Discord y la ayuda (`/info tema:fogon`).
- **Para desplegar**: correr `Database/add_fogon.sql` ANTES de arrancar el bot nuevo (ver `DEPLOY.md`). Ojo: la base viva ya la tiene aplicada desde el desarrollo; un bot v0.10.x que siga corriendo hasta reiniciarse vería «Zona 0» como una sexta zona en `/zonas` (cosmético: nadie la puede cruzar sin haber vencido al jefe de la 5).
- **Falta / ideas**: íconos propios (emojis de la aplicación) para el equipo del Fogón y el Asador; el aviso de «dónde cazar cada drop» en `/tips` hoy solo dice el comando, no la zona; el reinicio (v0.12).

## El drop de `/travel` baja de 60 % a 40 % (v0.10.2, 2026-10-06)

- Pedido del dueño: «60 es una banda, tirálo a 40». Es `CombatRewardCalculator.TravelDropChancePercent`; `/drops` y `/info tema:hunt` leen la constante.
- **Efecto medido** (`report_recipe_pacing.sql`, arma de clase + amuleto de las 5 zonas, promedio de las 4 clases, uno después del otro): de **~2.480 min (41 h) a ~3.120 min (52 h)**, un **26 % más largo**. Lo que se enlentece son las piezas que piden el drop de viaje (las armas de clase y los amuletos): ese material cae 1,5 veces más lento (1,33 % por minuto de cooldown, antes 2 %). Las armas generales, que solo piden drops de `/hunt`, no cambian.
- **No se tocaron las cantidades de las recetas a propósito** (el dueño quiere que avanzar cueste). Si la vuelta 1 resulta demasiado larga para los jugadores, la forma de devolverlo es bajar las cantidades de drop de viaje en `rework_drops_and_recipes.sql` (y correr el script de ritmos).
- Las cajas casi no se mueven (`report_box_economy.sql`, ya con el 40 %): el Cofre de Oro pasa de 60 % a 57 % de lo que vale su contenido; no hace falta cambiar precios. El trueque 3 por 1 sigue igual de útil, y más: el drop de viaje escaso es justo el que más cuesta.
- La chance de las mascotas (+6 % relativo de la Salamandrita) y los % de Fuego Nuevo se aplican sobre el 40 %. Los porcentajes del trueque de la v0.9.7 («~23 % más rápido») eran con el 60 % y no se volvieron a medir.
- Sin cambios de base de datos.

## Tragamonedas arregladas (v0.10.1, 2026-10-06)

- El hallazgo de la v0.9.6 (las slots regalaban oro: ×1,4375 por tirada) quedó resuelto. Pedido del dueño: arreglarlas, pero que el par pague **×1,5** («sacarle todo es de rata»), no devolver la apuesta pelada.
- Con 4 símbolos eso solo no alcanza (par ×1,5 + tres iguales ×5 sigue dando ×1,156), así que se sumó un **5.º símbolo, el 🔥** (las brasas del asado) y las cuentas quedaron: tres iguales **4 %** × 5, par **48 %** × 1,5, tres distintos **48 %** pierden =
  **retorno ×0,92: la casa se queda con el 8 %** (`CasinoService.SlotsReturnToPlayer`, calculado de las mismas constantes). El par se redondea hacia abajo (apuesta mínima 10 → paga 15) y ganar siempre deja al menos +5. El coinflip no cambia (×2 al 50 %).
  La ayuda (`/info tema:play`) lee todo de `CasinoService` (símbolos, chances, multiplicadores y la ventaja de la casa), así que no se desincroniza.
- Probado con el arnés `casinotest`: chances exactas, premios, 2 millones de tiradas (4,02 % / 47,98 % / 48,00 %, retorno ×0,9207) y el viejo agujero cerrado (jugar «all» repetido ya no multiplica el oro).
- Sin cambios de base de datos. Si se quiere otra ventaja de la casa: los tres números son constantes (`SlotsThreeMatchMultiplier`, `SlotsPairMultiplier` y la lista de símbolos).

## Mascotas (v0.10.0, 2026-10-06)

- Pedido del dueño: una mascota por zona, **pasivas y todas a la vez**, que sigan después del reset, con **comida especial** (un consumible nuevo) que se les da **una vez por hora** («si el oro no es un impedimento se rompe rápido»),
  con bonus de oro / EXP / defensa / drop de monstruos y **más EXP la del 5** (para que el reset sea más sencillo), y que **el huevo llegue con el cofre del jefe y se abra con /open**.
- **Las cinco** (tabla `pet_species`; el catálogo lo carga `Database/seed_pets.sql`): Zona 1 **Ñandusito** 🐦 oro hasta +5 % · Zona 2 **Cachorro de Puma** 🐆 EXP +5 % · Zona 3 **Gólem de bolsillo** 🪨 defensa +6 % · Zona 4 **Salamandrita** 🦎
  drop de monstruos +6 % (relativo) · Zona 5 **Quimerita** 🐉 EXP +10 % (con la del Puma suman +15 % de EXP).
- **Cómo se consiguen**: la **primera vez** que un jugador vence al jefe de una zona (`/boss`, o `/raid` por participante) recibe el **huevo** de esa zona, en la MISMA transacción que el cofre (`items.type = 'Huevo'`, sin precio). Se abre con
  **`/open`** (`aa open <huevo>`, también aparece en su lista desplegable): el huevo se gasta y nace la mascota en una sola transacción; si ya tenés esa especie, el huevo NO se gasta. `Database/add_pets.sql` entregó los huevos a quienes ya habían
  vencido jefes (en la base real: 7 huevos para 3 jugadores).
- **Cómo crecen**: nivel 1 a 10 (`GameData/PetRules.cs`); el bonus es el tope × nivel / 10 (nivel 1 = 10 % del tope). Comen **Comida para Mascotas** (`items.type = 'PetFood'`, **100 de oro** en la tienda y la taberna, se revende a 25): **una vez por hora
  cada mascota** (el reloj es el de la base de datos). Comidas por nivel 1, 1, 2, 2, 3, 3, 4, 4, 5 = **25 por mascota** (25 horas seguidas; 125 comidas = 12.500 de oro para las cinco). El oro no acelera nada: el límite es la hora.
- **Comandos**: `/pet view` (`aa pet`, `aa mascota`) muestra cada una con su nivel, bonus, progreso, «lista para comer» o «vuelve a comer en…», las que faltan descubrir y un botón **Alimentar**; `/pet feed [mascota]` alimenta a la elegida o, sin elegir, a todas las que
  puedan (una comida cada una, hasta donde alcance). `/profile` suma el campo «🐾 Mascotas» y marca la defensa con el bonus (`🐾 +6 %`). `/info tema:pets` lo explica. Logros nuevos **Domador** (1 / 3 / 5 mascotas) y **Criador** (10 / 50 / 125 comidas = las cinco al máximo),
  solo oro y XP: ahora son **19 logros**. El inventario muestra la comida con los consumibles y el huevo con las cajas («Cajas y huevos»).
- **Dónde valen**: el oro y la EXP en `/hunt`, `/travel`, `/boss`, `/raid` y `/autohunt`, sobre la recompensa entera (ya con su ×15 o ×30); la defensa en esas mismas peleas (se fija al empezar, como el banquete); el drop **solo en cacería y viaje** y **relativo**
  (+6 % sobre un 6 % = 6,36 %; sobre el 60 % del viaje, 63,6 %), el cofre del jefe NO. **No pelean en duelos ni en la Arena**: el balance PvP medido no se toca.
- **Impacto sobre el balance** (hecho: lo que se puede calcular a mano; **no se simuló** contra la escalera): con las cinco al máximo la EXP rinde +15 % (subir de nivel ~13 % más rápido), el oro +5 %, y el drop solo acelera las recetas que dependen de un drop por
  menos de ~6 %. La defensa +6 % del Gólem es lo único que toca la dificultad de las peleas y no se midió con `docs/calibration` (el resolvedor real); si hiciera falta, es el primer número a mirar. Se tarda mínimo ~25 horas en dejar a una mascota al máximo, así que
  nadie llega a los topes de golpe, y las cinco recién cuando tenga las cinco zonas.
- **Probado** (arnés `pettest` contra la base real con usuarios descartables, más la regresión de los otros 26 arneses): reglas de nivel y bonus, abrir y alimentar con **12 pedidos simultáneos** (pasa uno solo, la comida nunca queda en negativo, un huevo nunca se gasta de más), el huevo del jefe
  (primera vez sí, después no, 8 victorias simultáneas = un solo huevo), las recompensas con y sin mascotas (medias y chances sorteadas), el cofre del jefe sin tocar, duelos/Arena sin mascotas, el botón con el `InteractionService` real y los límites de Discord de todos los mensajes.
  La instalación limpia en una base nueva quedó idéntica a la real (incluida `pet_species`).
- **Falta / ideas**: **fusión de mascotas o un bonus especial en el reset 3 o 5** (idea del dueño, no construida); íconos propios (emojis de la aplicación) para las cinco y los huevos (hoy son unicode 🐦🐆🪨🦎🐉 / 🥚 / 🦴); una pantalla con la imagen de cada mascota;
  decidir si el bonus de defensa/EXP necesita recalibrar la escalera cuando se desbloquee el reset. Las misiones no incluyen «alimentar mascotas» a propósito (el pool exige que *cualquier* jugador pueda hacerlas y las mascotas piden haber vencido un jefe).
- **Para desplegar**: correr `Database/add_pets.sql` ANTES de arrancar el bot nuevo (ver `DEPLOY.md`).

## El trueque de drops con el tabernero (v0.9.7, 2026-10-06)

- Pedido del dueño: «que me cambie 3 drops x 1: cambiás 3 de una zona por 1 de la misma; así, si tenés mucha mala suerte, compensás». **`/exchange dar recibir [veces]`** (en texto `aa exchange "Pluma de Ñandú" "Cuero Grueso" 2`, alias `aa canje`): entregás **3 de un drop de una zona** y te llevás **1 distinto de la misma zona**, hasta 50 cambios juntos. Las listas desplegables te muestran solo lo que podés hacer (en «recibir», los otros dos drops de la zona del que elegiste).
  En la **/taberna** hay una lista nueva «Cambiar drops (3 por 1)» (aparece si tenés al menos 3 de algún drop): elegís qué das y después qué querés, y hace un cambio. `/info tema:exchange` lo explica.
- **Qué entra**: solo los **15 drops de monstruos** (los dos de cacería y el de viaje de cada zona). **No** madera ni minerales (eso es entre jugadores, `/trade`), ni los trofeos de las cajas (no tienen zona), ni los cofres de los jefes. Las reglas se validan en la base dentro de la misma transacción que el cobro: se prueba con 20 pedidos a la vez sobre 30 drops y salen exactamente 10 cambios.
- **Efecto medido sobre las recetas reales** (juego perfecto: cazar sin parar y viajar cada 30 minutos): el **arma de clase** se junta ~23 % más rápido (150 ➜ ~116 min) y el **amuleto** ~17 % (200 ➜ ~167); el arma general no cambia (pide los dos drops de cacería por igual). Es un empujón moderado, pensado para la mala racha. Si se siente mucho, subir `DropExchange.GiveAmount` a 4 lo deja en ~14 %.
- Sin cambios de base de datos.

## Ayuda por tema (`/info tema:<tema>`) y arreglo de `info enchant` (v0.9.6, 2026-10-06)

- **El error de `info enchant`**: no era del código publicado sino de un cambio local sin guardar (`Enchantments.MaxTier = 100` en la carpeta principal): la tabla tiene 5 tiers y `TierName(6)` se salía del arreglo. Para que no se pueda repetir, `MaxTier` ya no es un número suelto: sale del largo de la tabla de tiers (`IsValidTier` protege cada acceso), y hay prueba con los tiers 0, 6 y 100.
- **Ayuda por tema**, como pidió el dueño («info enchant, info play, info xxx»): `/info tema:<tema>` (con lista desplegable) y `aa info <tema>` explican **cómo funciona** cada cosa, con sus comandos y sus números. **18 temas**: enchant, dismantle (Polvo), bank, death (penalidad), play (casino), hunt (combate, viaje, autohunt), boss (jefes y raids), forge (herrería y equipo), zone, boxes, shop (tienda, taberna, curarte), missions (y logros),
  arena (y duelos), trade, classes (pasivas y habilidades), gather (talar y minar), daily (y regalar oro), stats (nivel, vida, ataque, defensa). Se encuentran por la clave, el nombre o un alias en español (`aa info casino`, `aa info polvo`, `aa info muerte`, `aa info ench`). Un tema que no existe responde con la lista de temas. `/info` sin tema sigue siendo la lista de comandos y ahora nombra los temas.
- Los números **salen de las constantes del juego** (cooldowns, penalidad, banco, casino, encantamientos...), no están escritos a mano: si se retocan, la ayuda se actualiza sola. Lo que vive en la base y el dueño edita (precios y rangos de las cajas, niveles de las zonas) no se repite: la ayuda manda a `/shop view` y `/zonas`.
- **Hallazgo importante (RESUELTO en la v0.10.1, ver arriba): las tragamonedas regalaban oro.** Con 4 símbolos, dos iguales salen el 56 % de las veces y pagan ×2, tres iguales el 6 % y pagan ×5, y tres distintos el 37,5 % (pierde): el retorno esperado es **×1,4375 lo apostado (+43,75 % por tirada)** y `/play` no tiene cooldown ni tope, así que `/play slots all` repetido duplica el oro en pocas tiradas. El coinflip es justo (×2 al 50 %).
  La solución mínima es que el par devuelva la apuesta (×1) y dejar los tres iguales en ×5 (retorno 87,5 %, casa gana 12,5 %), o ajustar los multiplicadores; son las constantes `CasinoService.SlotsPairMultiplier` y `SlotsThreeMatchMultiplier` y la ayuda las lee, así que se cambian en un solo lugar. Hasta que se decida, `/info tema:play` solo dice lo que paga cada jugada.

## Desmantelar hasta 100 por vez (v0.9.5, 2026-10-06)

- `/dismantle` (y `aa desmantelar [cantidad] <ítem>`) ahora acepta de **1 a 100** unidades por comando (eran 5). Es la misma constante de siempre, `Dismantling.MaxPerCommand`, y los textos (ayuda, descripciones del comando, `/enchant info`) la leen de ahí.
- **No cambia el ritmo del Polvo**: se paga por unidad y las unidades se gastan de verdad, así que solo hacen falta menos comandos. El caso más grande es 100 unidades Míticas = 50.000 de Polvo (entra de sobra en la columna).
- Probado: 100 de golpe, pedir más de lo que tenés o más del tope (se rechaza sin tocar nada) y dos pedidos de 100 a la vez con solo 150 (paga uno solo, nunca queda negativo).
- Ojo menor: el logro Desmantelador (20 / 150 / 1.000 unidades) se calculó con 5 por comando; ahora se llega en pocos comandos, pero igual hay que tener y gastar esas unidades.
- Sin cambios de base de datos.

## Calibración de encantamientos y penalidad, `enchant info` y «Consumibles» (v0.9.4, 2026-10-06)

- **«Consumibles»** en el inventario (en vez de «Comida»). `/shop` y la taberna siguen diciendo Comida.
- **`/enchant info`** (también `aa enchant info`, `aa encantar info` y `aa info enchant`; `/info tema:Encantamientos`): los 5 tiers con su **bonus** y su **chance** por intento, cuántos intentos llevan en promedio, lo que cuesta cada intento según la zona de la pieza y de dónde sale el Polvo. Sale de las mismas constantes que el juego. `/enchant` sin elegir pieza sigue mostrando tu estado y manda a `/enchant info`.
- **Calibración** (simulación con el resolvedor real y los monstruos reales de la base, 600 peleas por clase y monstruo; el programa y los resultados completos quedaron en `docs/calibration/`):
  - **Encantamientos: no se tocó nada.** La escalera sigue intacta con tier 0 (comunes 13-18 % de vida, jefe ~16 % de derrota con el equipo propio; entrar con el equipo anterior ~47 % y 69-87 % de derrota del jefe). Con tier 5 en arma y amuleto los comunes cuestan la mitad (7-10 %) y el jefe propio casi no se pierde (16 % ➜ 1-2 %); con el equipo ANTERIOR al tier 5 el jefe siguiente se pierde 44-61 % (antes 69-87 %): suaviza un escalón de equipo pero no lo salta.
    Cuesta mucho a propósito: el Soberano son ~33 intentos (~11 h de Polvo en Zona 1, ~67 h en Zona 5). Ojo: el oro pesa poco en el costo (8 cacerías por intento contra 20-120 minutos de Polvo); si querés que sea un sumidero de oro de verdad, se sube `GoldUnitsPerAttempt`.
  - **Penalidad por morir: no es una espiral.** Una muerte promedio cuesta ~15-22 minutos de juego (la mitad de un nivel son ~15-22 cacerías) y el 5 % del oro son ~2,5 cacerías. En peleas normales casi no se pierde (0 % de derrota con vida llena en las zonas propias). El jefe con el equipo anterior sigue siendo valor esperado ~0 en EXP (se gana más de un nivel de EXP si ganás, se pierde medio si perdés) y con el propio es +60 cacerías.
  - **Lo único que había que arreglar: `/autohunt` con poca vida.** Pelea solo y no puede huir, cada cacería cuesta ~16 % de la vida y la chance de PERDER sube rápido: con 65 % de vida o más no pasa de ~2 % en ninguna zona, con 50 % ya es 3-10 % y con 25 % es 38-57 %. Como una muerte vale ~20 cacerías y una cacería rinde 1, el valor esperado se daba vuelta por debajo de ~5 %. Ahora **`/autohunt` (y `aa ah`) pide al menos 65 % de vida**
    (`AutoHuntRules.MinHpPercent`), avisa que te cures con `/heal` o pelees a mano con `/hunt` (donde podés huir), y NO gasta el cooldown. Es una sola constante: si no te gusta, se saca o se baja. `/hunt` a mano no tiene la regla.
- Sin cambios de base de datos.

## El inventario en dos columnas (v0.9.3, 2026-10-06)

- Pedido del dueño: orden madera, minerales, drops, consumibles, con los consumibles «en una columna sola debajo de minerales» y solo dos columnas. Queda: **izquierda Madera y debajo los Drops de monstruo; derecha Mineral y debajo la Comida (con las Cajas)**, un ítem por renglón.
- Discord pone los campos en línea de a tres por fila y cada uno ocupa un tercio del ancho; para que queden dos, cada fila lleva un tercer campo en línea vacío que la cierra (al costado no suma altura). **Ojo con el costo**: cada columna sigue siendo angosta, así que un nombre largo («Cuero Curtido de Pradera») se parte en dos renglones dentro de su columna. Si molesta, la vuelta atrás es el diseño de la v0.9.2 (drops a ancho completo).
- Sin cambios de base de datos.

## Sin huecos enormes en el perfil (v0.9.2, 2026-10-05)

- La captura del dueño mostró que cada campo «en blanco» (el separador de la v0.9.1) dejaba un hueco enorme entre Vida y Ataque y antes de Habilidad, y que Polvo y Racha, en columnas, quedaban muy lejos. Se **sacaron todos los separadores** (perfil, inventario, `/enchant`, `/dismantle`) y Polvo y Racha pasan a ir uno debajo del otro: ahora todo el perfil es apilado. Regla en CLAUDE.md: no usar campos vacíos de separación.
- **Limpieza de datos (solo la cuenta del dueño, agus_42)**: se sacó de su inventario lo que hoy no usa ninguna receta ni suelta ningún monstruo: 12 trofeos de cajas (Collar de Cuero Viejo ×3, Cuero Curtido de Pradera, Pelaje Oscuro, Piedra Caliente ×2, Garra del Alfa ×2, Polvo de Mina Sagrada, Martillo del Capataz ×3, Roca Volcánica Pura, Corona de Cerdas ×4, Garra Maldita ×2,
  Hueso Añejo ×3, Rama Carbonizada ×4). Los otros tres jugadores no se tocaron, y el logro Coleccionista no cambia (cuenta los trofeos encontrados, no los que tenés). Ojo: **desmantelados valían ~437 Polvo**. Quedó un respaldo restaurable fuera del repo (`bot_ds_rpg_backups/inventario_agus_42_2026-10-05.sql`).

## Inventario y perfil más aireados, y la Arena en `/cd` (v0.9.1, 2026-10-05)

A partir de las capturas del dueño (inventario y perfil de la v0.9.0):
- **Inventario**: los drops ahora van **uno por renglón**, a ancho completo, con una fila en blanco entre las columnas de arriba (Madera | Mineral | Comida) y ellos. Las "fichas" de la v0.8.4 no aguantaron: Discord igual corta la línea entre el ícono (que es una imagen) y el texto, así que el ícono de un ítem quedaba al final del renglón anterior, pegado al ítem equivocado.
  Con todo el catálogo a 9.999 entra (3.870 de 6.000 caracteres; los 32 drops en 3 campos).
- **Perfil apilado**: Ataque, debajo Defensa, debajo Oro y debajo Banco (cada uno en su renglón, ya no en columnas angostas); Polvo y Racha en una fila aparte. Se sacó el «(+N)» del arma y del amuleto (ya está sumado en el número grande) y queda el encantamiento debajo del arma. El campo «Zona actual» pasó a una línea
  debajo del título: «Zona 1: Praderas del Mate (máx. Zona 4)», donde el máximo es la zona más alta que tenés desbloqueada (la misma regla que `/zona`).
- **`/enchant` y `/dismantle`** con párrafos separados, una fila en blanco bajo la descripción y los campos apilados (los tiers posibles en dos renglones cortos, no en uno apretado).
- **`/cd` incluye la Arena** como última línea: «anotado ✅ — se juega en 1h 35m» o «¡todavía no te anotaste! ⚠️ Se juega en ...: /arena join». Es para que nadie se olvide de anotarse; solo lee el torneo de hoy y, si la consulta falla, la línea no sale (`/cd` nunca se rompe por la Arena).
- Sin cambios de base de datos.

## Banco, penalidad por muerte, Polvo, encantamientos y 7 logros nuevos (v0.9.0, 2026-10-05)

Lo que el dueño aprobó de su lista de ideas (punto 1 y 2 tal cual los dijo; el 3 sin "craftear"; los logros de comandos y enemigos). Relación y mascotas quedan para más adelante.

- **Banco** (`/bank view|open|deposit|withdraw`, `aa bank`): la cuenta **se compra** una vez por **1.000 de oro**; después se deposita y se retira (acepta `all`). El oro del banco **no lo toca la penalidad**. Sin interés ni tope por ahora (anotado abajo).
- **Penalidad por morir**: al perder un combate, **a cualquier nivel**, la **EXP del nivel actual vuelve a 0** y se pierde el **5 % del oro de la billetera** (redondeado hacia abajo: con menos de 20 de oro no se pierde nada; nunca se baja de nivel). Se aplica en `/hunt`, `/travel`, `/boss`,
  el `/use` que te mata en plena pelea, **`/autohunt`** y a cada caído de un **raid** que cae entero. **No** se aplica al huir, al vencerse el tiempo, ni en duelos y Arena. Los mensajes de derrota traen un campo «☠️ Penalidad» con lo que perdiste (el del raid explica la regla en general).
- **Desmantelar → Polvo** (`/dismantle <ítem> [1-5]`, `aa desmantelar 3 Hierro`): destruye Madera, Mineral o drops de monstruo (nunca cajas, comida ni equipo) y da **Polvo**: 1 / 6 / 24 / 70 / 500 por unidad según la rareza, calibrado a ~0,5 Polvo por minuto de farmeo en TODAS las rarezas
  (para que desmantelar "lo más eficiente" no rompa el costo de encantar). El tope de 5 por comando es una sola constante (`Dismantling.MaxPerCommand`). **Craftear queda descartado.**
- **Encantamientos** (`/enchant [arma|amuleto]`, `aa encantar arma`): cuestan **Polvo + oro** según la zona de la pieza (rareza → zona; Zona 1: 10 Polvo + 112 oro, Zona 5: 60 Polvo + 3.000 oro) y sale un **tier al azar**: Tibio 40 % (+4 %) · Al Rojo 30 % (+8 %) · Ardiente 18 % (+13 %) ·
  Incandescente 9 % (+19 %) · Soberano 3 % (+26 %) del stat de **la pieza** (mínimo +1). El intento se paga siempre; el tier nuevo **solo reemplaza** al actual si es mejor. **Vender o reemplazar la pieza equipada borra su encantamiento.** El arma lleva un «Filo» y el amuleto una «Guarda» (nombres de brasa, no del EPIC RPG).
  `/profile` muestra el banco, el Polvo y el encantamiento con su %, y el ataque/defensa ya lo incluyen (es la misma cuenta del combate).
- **7 logros nuevos (17 en total) y `/achievements` con páginas**: Comandante (comandos usados, de barra o `aa`), Exterminador (enemigos vencidos), Misionero, Desmantelador, Encantador, Afortunado (oro ganado en el casino) y Gladiador (torneos de la Arena). **Pagan solo oro y XP, poco, nunca cajas**
  (sus contadores se pueden inflar: un comando cuenta aunque sea de mirar). La pantalla va por **categorías** (Combate, Oficios, Economía, Constancia) con un desplegable, y el botón «Reclamar» cobra todas las páginas y vuelve a la misma. `aa logros 2` / `aa logros oficios` abren una página.
- **Para desplegar una base viva**: correr `Database/add_bank_dust_enchants.sql` ANTES de arrancar la v0.9.0 (agrega 5 columnas a `users` que el código lee). Es re-ejecutable y se verifica solo. Ver DEPLOY.md.
- **Ojo con el balance (decisión para el dueño)**: el escalón entre zonas se midió SIN encantamientos. Con el equipo de la zona, el tier más alto (3 % de los intentos) suma ~+15–20 % de ataque total; un jugador con muchos intentos va a pelear más cómodo que lo calibrado. Si se siente fácil, el contrapeso previsto es endurecer los monstruos de la segunda vuelta (después del reset),
  no tocar los de la primera. La penalidad por morir suma presión del otro lado.
- **`game_events` crece más**: ahora hay una fila por comando usado y otra por enemigo vencido. Si la tabla llega a pesar, el contador de `command_used` se puede llevar solo en `player_stats` sin guardar cada evento.
- Pendientes que quedaron anotados: interés o tope del banco, relación/casamiento y mascotas (la lista completa está en "Ideas a futuro").

## Inventario sin nombres partidos (v0.8.4, 2026-10-05)

- En la captura del dueño (PC), los drops en tres columnas angostas partían los nombres largos en dos renglones ("Collar de Cuero / Viejo: 3"). Ahora **Madera | Mineral | Comida (con las Cajas debajo)** siguen en columnas, y los **drops van a ancho completo**: cada ítem es una ficha que no se parte por dentro
  (espacios que no se cortan) y la línea solo se corta entre un ítem y otro, así que entran dos o tres por renglón. Con los 54 ítems a 9.999 sigue entrando (3.900 de 6.000 caracteres). Detalle y por qué no volver a columnas en CLAUDE.md.
- Se anotó en "Ideas a futuro" la lista de siete ideas del dueño (logros, desmantelar/craftear, relación, encantamientos, banco, muerte con penalidad, mascotas) con el orden sugerido y qué cuidar en cada una.

## Todos los monstruos hablan, la cara siempre y el inventario en columnas (v0.8.3, 2026-10-05)

- **Los 20 monstruos dicen algo**: al aparecer, al caer y cuando te vencen (los jefes ya lo hacían; ahora también los 15 de cacería y de viaje, dos frases por situación,
  `GameData/NpcDialogue.cs`). Sale en `/hunt`, `/travel`, `/boss`, `/use` en plena pelea y `/autohunt`, ganes o pierdas. Un monstruo nuevo sin frases propias usa unas genéricas.
- **La cara del monstruo va SIEMPRE de miniatura**, también cuando la victoria trae un drop (antes ahí la miniatura pasaba a ser el ítem); el drop se sigue anunciando en su campo.
- **Inventario en columnas**, como el de la referencia que te gustó: fila 1 Madera | Mineral | Comida (con las Cajas debajo de la comida), fila 2 los drops repartidos en 1 a 3 columnas;
  cada renglón es «ícono **Nombre**: cantidad» con separador de miles. Probado con el peor caso (los 54 ítems a 9.999: 3.900 caracteres, bajo el límite de 6.000).
  **Los emojis no se pueden agrandar**: dentro de un texto Discord los dibuja siempre de 22 px. Si más adelante querés íconos grandes de verdad, la salida es un inventario como
  IMAGEN generada por el bot (una grilla con los íconos a 64 px y la cantidad), que es un proyecto aparte (una librería de imágenes nueva); en texto, lo que se puede es lo de acá.

## Caras de los enemigos, Maderas nuevas y ATQ/DEF al subir de nivel (v0.8.2, 2026-10-05)

- **Cada enemigo tiene su cara** (los 20, un retrato por monstruo subido como emoji de la aplicación): sale de miniatura arriba a la derecha en `/hunt`, `/travel`, `/boss`,
  `/autohunt`, `/use` en plena pelea y todo el raid (sala de espera, pelea, victoria y derrota). Si ganás y el monstruo suelta algo, la miniatura es el ítem (como antes); un
  monstruo sin cara cargada manda el mensaje sin miniatura. La cara vive en una columna aparte, `monsters.portrait_emoji` (`Database/update_monster_portraits.sql`); el
  `monsters.emoji` de siempre sigue siendo el unicode que va dentro del texto.
- **Las 4 Maderas rehechas** y pasadas a emojis de la aplicación (eran del servidor): el Roble con aura azul y estrellitas bien marcada para que no se confunda con el Pino, el Nogal
  violeta y el Ébano dorado. Ya no queda ningún emoji de ítem que dependa del servidor, y las 10 armas de Zona 4 y 5 también tienen el suyo (armas 25/28).
- **La subida de nivel dice cuánto subís de Ataque y Defensa**, además de la vida: +2 ATQ y +1 DEF por nivel (se calcula con `CombatStats`, así que sigue a la fórmula del combate
  y de `/profile`; el arma y el amuleto suman aparte). Con varios niveles de una, suma todos.
- **Ojo con la base real**: dos monstruos (Espíritu del Monte y Puma de las Cenizas) tenían en `monsters.emoji` el código del emoji custom de su cara, puesto a mano. Se devolvieron a su unicode
  (👻 y 🐆), que es lo que da una instalación limpia; la comparación instalación limpia vs base real quedó idéntica.
- **Para desplegar una base viva**: correr `update_item_emojis.sql` y `update_monster_portraits.sql` ANTES de arrancar la v0.8.2 (el código lee la columna nueva). Ver DEPLOY.md.

## Cooldowns de jefe/raid y cajas, y aviso de nivel al instante (v0.8.1, 2026-10-05)

Pedido del dueño: "las cajas estaban muy rotas", así que **solo cooldowns**, sin tocar recompensas ni drops.

- **Jefe y raid: 5 horas** (un solo cooldown, una sola línea en `/cd`). Si perdés, escapás o abandonás siguen siendo solo 30 minutos para reintentar
  (`RetryAfterFailure`, aprobado en la v0.8.0): ganar es lo que cuesta las 5 horas. La recompensa se queda en ×15 (el dueño pidió no tocarla): por minuto de
  cooldown el jefe ahora rinde bastante menos que un viaje; si hace falta que vuelva a valer la espera, `CombatRewardCalculator.BossRewardMultiplier` es la perilla.
- **Comprar cajas: una cada 2 horas** (era 1). Los textos de la taberna, `/shop` y los errores lo dicen.
- **Las cajas quedan como las dejó el dueño** en `rework_boxes.sql` (la base real ya las tiene): 1–5 ítems a 2.300, 5–10 a 14.000, 10–25 a 42.000, 25–60 a
  120.000 y el Arca 60–100. Solo se actualizaron los comentarios y los arneses que decían los números viejos.
- **"¡SUBISTE DE NIVEL!" (y misiones/logros) llegaba recién con el comando siguiente**: Discord.Net corre los comandos de barra en modo asíncrono, así que
  `ExecuteCommandAsync` vuelve apenas LANZA el comando y los avisos se entregaban con la cola todavía vacía. Ahora salen cuando el comando termina
  (`Services/NoticeDelivery.cs`, enganchado a `InteractionExecuted`; los comandos de texto ya andaban). Reproducido con el `InteractionService` real en el arnés
  `noticetest`. Límite conocido: en un raid, solo quien da el golpe final tiene una interacción donde contestar; el resto recibe su banner en su próximo comando.
- **`Assest/`** (los retratos del herrero y el tabernero) entra al repositorio.

## Cajas v2 y cooldowns de viaje y jefe (v0.8.0, 2026-10-05)

_(Los números de abajo son los de la v0.8.0: el dueño ajustó rangos y precios de las cajas y el cooldown del jefe volvió a 5 horas, ver la v0.8.1 de arriba.)_

Pedido del dueño: que una caja diga cuántos ítems da, que cueste mucho más, que lo que sale tenga las probabilidades reales de farmear y
que no regale oro. Plan y ledger: `docs/superpowers/plans/2026-10-05-cajas-v2-y-cooldowns.md`.

- **Cada caja dice su rango de ítems**: Cajón de Pino 1–10, Baúl de Roble 5–20, Arcón de Hierro 10–35, Cofre de Oro 20–60 y Arca del Soberano
  40–100 (el oro no cuenta como ítem). Se ve en `/shop view`, la taberna, las listas de `/shop buy` y `/open`, y al abrir ("Cada Baúl de Roble trae entre 5 y 20 ítems").
- **Precios por minutos de farmeo**: 1.000 / 10.000 / 35.000 / 110.000 (antes 150 / 700 / 1.800 / 3.500). `Database/report_box_economy.sql` valora cada
  caja en minutos de farmeo × el oro de una hora de `/hunt` de su zona: el precio queda en 60–79 % de ese valor, o sea que comprar nunca es más rápido que jugar.
  **Las cajas ya no se revenden** (venta 0).
- **El botín sale con las chances reales**: cada tirada es recolección (la misma tabla de `/chop` y `/mine`; el Mítico baja a **0,1 % por ítem**, pedido
  del dueño), drops de zona (hunt y travel en su proporción real 3 : 2), comida/trofeos/la caja de abajo, y el oro pasó a ser un **bono raro del 3 %**
  que no cuenta como ítem. Cambia una regla vieja: las cajas SÍ dan drops de zona y materiales (antes no, por el ritmo; los precios y los topes de abajo lo cuidan).
- **Solo hasta la zona desbloqueada**: una caja es de la zona de su rareza y solo se compra si ya la desbloqueaste (🔒 en la lista, el pedido se rechaza
  sin cobrar ni gastar el cooldown), y lo que sale nunca trae drops de una zona que el jugador no tiene abierta, aunque la caja sea de una más alta.
- **`/travel` cada 30 minutos** (eran 10), con la recompensa ×30 y 60 % de drop: lo mismo por minuto que antes.
- **Jefe y raid: 1 hora** (30 minutos si perdés o escapás) y la recompensa ×15 (era ×6, "un poco más" porque ahora esperás más). Un solo mecanismo: el cooldown se
  reclama al empezar dejando solo 30 minutos y una **victoria** lo completa a 1 hora en la misma transacción que paga.
- **Encontrado en la revisión final**: `/shop sellall` borraba TODA la mochila, también lo que la tienda no compra (venta 0), así que con las cajas sin reventa
  se las habría llevado gratis (el Arca del Soberano ya corría ese riesgo). Ahora solo vende y borra lo que tiene precio de venta; el resto se queda. Y
  `aa taberna` no marcaba con 🔒 las cajas cerradas (la compra sí las rechazaba): ahora usa el mismo contexto de zonas que `/taberna`.
- **Para desplegar una base viva**: correr `Database/rework_boxes.sql` ANTES de arrancar la v0.8.0 (agrega columnas que el código lee). Es re-ejecutable.
- **Pendiente / a mirar**: el valor de las cajas altas incluye la caja de abajo que a veces traen (se valora a su precio de compra); si en la práctica la cadena
  Cofre → Arcón → Baúl hace que se sienta de más, se baja el peso de esa entrada en `rework_boxes.sql`. El Hierro sigue siendo el cuello de botella de varias recetas.

## Eventos de juego, `/give`, cajas, banquetes, misiones y logros (v0.6.0, 2026-10-01)

Primer paquete de "más cosas en qué gastar el oro y más datos para decidir". Se hizo por etapas, cada una compilada, probada contra
la base real con usuarios descartables y recién ahí integrada a `develop`.

- **Registro de eventos** (etapa 1): tabla `game_events` + contadores `player_stats`, con un solo punto de entrada
  (`IGameEvents.RecordAsync`, no tira nunca excepción). Se registran inicio, subida de nivel, victorias (hunt/travel/boss/raid), derrotas,
  `/chop`, `/mine` (con unidades), forja, `/daily`, oro gastado/ganado en la tienda, oro dado/recibido y cajas abiertas.
  `Database/report_game_events.sql` resume cuánto se juega de cada cosa. Es la base de las misiones y los logros (etapa 3).
- **`/give`** (`aa give @jugador 100`): pasa monedas entre jugadores, atómico y sin posibilidad de trabarse aunque dos se den
  mutuamente al mismo tiempo. Sin impuesto por ahora; si aparecen cuentas alternativas pasándose el `/daily`, se ve en los eventos y se
  le pone tope o impuesto en un solo lugar.
- **Cajas** (etapa 2a): cinco tiers (Cajón de Pino 150, Baúl de Roble 700, Arcón de Hierro 1.800, Cofre de Oro 3.500 y el Arca del
  Soberano, que no se compra: es premio). `/open` (de 1 a 10 por vez) da oro, materiales de recolección, trofeos de monstruo que ya no
  suelta nadie, comida y cajas de menor tier. **No dan** los drops de zona de las recetas (rompería el ritmo calibrado) y las metas
  larguísimas (Corteza del Árbol de Vida, Fragmento de Meteorito) solo salen de la caja Mítica. Valor esperado de las de la tienda:
  54–67 % del precio (sumidero de oro). La tienda ahora lista comida y cajas.
- **Comida de 9 a 6 y banquetes** (etapa 2b): salen Pan Casero, Choripán y Vacío al Disco (quien los tuviera recibió su valor en
  oro). Los dos Míticos pasaron a ser **banquetes**: cuestan ~3 veces más (2.100 y 2.800) y dan **+15 % de ataque por 30 minutos**
  además de curar. Un banquete nuevo reemplaza al anterior (no se acumulan, tampoco en plena pelea). Se ve en `/profile`, `/shop view`,
  la lista de `/shop buy` y el desplegable de curar; `/heal` nunca los gasta (el valor está en el buff) y `/use` los usa aunque estés
  con la vida llena. El porcentaje y la duración están en la tabla `item_buffs`: se retocan con un UPDATE, sin redesplegar.
- **Hallazgo al probar una instalación desde cero**: el seed creaba Carbón como Común y la base viva lo tenía Raro (cambiado a mano,
  nunca capturado en un script). Una base nueva habría dado el doble de Hierro por `/mine` y recetas descalibradas. Corregido en
  `seed_class_gear_and_monster_drops.sql`; la comparación base nueva vs viva quedó como práctica en CLAUDE.md.
- **Misiones y logros** (etapa 3): 3 misiones diarias y 2 semanales (`/missions`, `aa missions`) que se reinician a medianoche hora de
  Uruguay (las semanales el lunes), **iguales para todos** ese día, y 10 logros de 3 tramos (`/achievements`: Cazador, Viajero, Matajefes,
  Recolector, Herrero, Comerciante, Generoso, Abridor, Coleccionista y Constante). El progreso sale del registro de eventos (no se
  guarda nada aparte, así no se puede desfasar); se cobra con un botón (`aa missions claim` en texto) y el bot avisa apenas
  cumplís una meta. Premios: oro que escala con tu zona (N "cacerías de oro" de esa zona), XP (un % de tu nivel) y cajas —completar
  las 3 diarias da la caja de tu zona y las 2 semanales una más arriba—. El Arca del Soberano (la Mítica, que no se compra) solo sale de
  Matajefes III y Coleccionista III. Cobrar es atómico: 20 cobros simultáneos de lo mismo pagan una sola vez. Los trofeos de las
  cajas pasaron a contar en una **colección** (distintos, no repetidos) que alimenta el logro Coleccionista, y `/open` avisa cuáles son nuevos.
  Los logros cuentan desde que se activó el registro de eventos, no antes.
- **Ajustes después de probarlo** (2026-10-01): (1) **Probabilidades de `/chop` y `/mine`**: lo común sube de 60 % a 68 % y lo demás
  baja (Raro 25 → 21, Épico 10 → 7, Legendario 4,5 → 3,5; el Mítico queda en 0,5). Están en una sola tabla, `RarityCatalog.GatheringWeights`
  (en milésimos), y el reporte `report_recipe_pacing.sql` acepta `-v wc= wr= we= wl= wm=` para probar un escenario antes de aplicarlo.
  **Costo medido**: 23 de las 35 recetas tardan más (el tiempo medio sube ~16 %, la que más 43 %) porque dependen de Hierro, Nogal,
  Oro Puro, Ébano o Zafiro; las que dependen de drops de monstruo no cambian. El Hierro pasa de 12,5 % a 10,5 % por `/mine`
  (el cuello de botella conocido ahora es ~19 % más lento: el Hacha de Hierro MK3 de ~200 a ~238 min). Si se siente muy lento, subir
  Raro en esa tabla. (2) **Cajas: una por compra y una compra por hora** (`CooldownCatalog.BoxBuy`, `ShopCatalog.BoxesPerPurchase`); sale en
  `/cd` y el cooldown solo se gasta si la compra sale bien (sin oro no se gasta); la comida no tiene límite. (3) **Tiempos con días y
  horas** en `/cd` y en todos los avisos de cooldown (`TimeFormat.Remaining`: "2d 3h 5m", "1h 5m 10s", "4m 20s"). (4) **Listas
  desplegables** también en `/shop sell` y `/use` (ya estaban en `/shop buy`, `/equip`, `/open`, `/forge make` y `/zona`), y todas
  heredan de `SafeAutocompleteHandler`: si armar la lista falla, queda en el log y se devuelve vacía en vez de romperse. (5) El nombre
  de un ítem ya no distingue mayúsculas, tildes **ni espacios sobrantes** (`"Mate Amargo "` fallaba). (6) Misiones y logros con una línea en
  blanco entre cada una.
- **Jefe y raid: UN solo cooldown de 5 horas** (2026-10-05, v0.7.1, pedido del dueño): `CooldownCatalog.Raid` pasó a ser un alias de `CooldownCatalog.Boss` ("Jefe / Raid", 5 h en vez de 30 min), así que `/cd` muestra UNA línea y reclamar uno bloquea al otro (ya compartían el nombre interno). Se actualizaron las descripciones de `/boss` y `/raid` (y los textos de `aa`). **Dos consecuencias para decidir**: (1) el cooldown se cobra al EMPEZAR la pelea, así que perder o huir también te deja 5 horas sin jefe, y el jefe es la puerta a la zona siguiente (con tu equipo: ~11% de derrota; entrando con el equipo de la zona anterior: 67–87%); (2) el x6 de recompensa se calibró con 30 minutos: ahora cada pelea es un evento 10 veces más raro y paga lo mismo (por minuto de cooldown es 10 veces menos). Ninguna de las dos se tocó sin preguntar. Los jugadores que ya habían usado el jefe en las últimas horas quedan bloqueados hasta 5 h después de su última pelea.
- **Rework de drops, jefes y recetas — v0.7.0** (2026-10-05, pedido del dueño: "que no quede tan cargado"): (1) **20 mobs**: 2 de `/hunt` + 1 de `/travel` + 1 jefe por zona (sale el Perro Cimarrón: la Zona 1 queda con 2 de hunt). (2) **3 drops de material por zona** (los 2 de hunt, uno "físico" para Guerrero y Ninja y uno "arcano" para Arquero y Hechicero, y el de travel). **El jefe no suelta material: da el cofre de su zona** (Cajón de Pino, Baúl de Roble, Arcón de Hierro, Cofre de Oro en la 4 y la 5) — **100% la primera vez que ESE jugador lo derrota y 40% después**, solo y en raid (por participante); el cofre se abre con `/open`. (3) **6 recetas por zona (30)**: 4 armas de clase + 1 general + 1 amuleto único; cada jugador ve 3. La **general** tiene el mismo ATQ base que las de clase pero **sin familia** (sin boost de clase) y la receta más accesible (~100 min); la **de clase** (~150 min) rinde ×1,5 con su clase; el **amuleto** (~200 min) pide los 3 drops de la zona + 1 recolección. ATQ base 9 / 20 / 32 / 50 / 80, DEF 10 / 18 / 30 / 46 / 75 (los "bajos" con los que se calibró la escalera). Máximo 4 tipos de ingrediente por receta (antes 26 de 35 pasaban de 3, hasta 5). Las 20 armas de clase quedan exclusivas de su clase. (4) **`/chop` y `/mine` por rareza**: Común 1–5, Raro 1–3, Épico 1–2, Legendario y Mítico 1 (nunca 5 de algo raro; antes Raro y Épico eran 1–3; `RunMultiplier` sigue en 1 y el reset lo sube). **Cómo se aplica**: `Database/rework_drops_and_recipes.sql` (idempotente, se verifica solo, va al final de `run_fresh_install.sql`); sobre la base viva, con `pg_dump` antes, y **borra sin reembolso** (autorizado por el dueño): se retiraron el Colmillo de Cimarrón, los 5 drops de jefe (Colmillo del Rey Jabalí, Pelaje Plateado del Alfa, Yunque del Capataz, Brasa Eterna, Corazón del Soberano) y los 5 amuletos "bajos" (Amuleto del Levantador, Mate Tallado en Cenizas, Casco de Capataz, Coraza de Escamas Ígneas, Égida del Devorador). Al aplicarlo en la base real, 3 jugadores perdieron 6 unidades de materiales y 2 quedaron sin el Amuleto del Levantador. **Verificado**: la instalación limpia da la MISMA base que la real ya migrada (11 resúmenes de estructura y catálogo), los minutos del script de ritmos coinciden con el modelo en las 30 recetas, y los arneses (jefe 100% / 40%, 3 recetas por clase y zona, `/drops` y `/forge` bajo los 6000 caracteres, el cofre llega a la mochila) están en verde. Las armas de clase de la Zona 5 siguen pidiendo 1 material mítico (~1000 min de recolección): es la meta larga de la run 1. **Ojo**: ya no se corren SOLOS `finalize_monster_roster.sql`, `seed_zone_bosses.sql`, `seed_recipes.sql` ni `seed_zoneN_gear_and_recipes.sql` sobre una base viva (volverían al estado viejo). Emojis: Material 32/32 y Amulet 7/11 (los emojis de lo retirado quedan libres en el portal). Spec: `docs/superpowers/specs/2026-10-05-drops-y-recetas-rework-design.md`.
- **Listo para desplegar en Railway** (2026-10-05): el dueño liberó espacio en su plan Hobby y eligió Railway para dejar el bot prendido todo el día. Agregado: `railway.toml` (worker con Dockerfile, 1 réplica, reinicio siempre, la copia vieja tiene 20 s para despedirse de Discord), `deploy/railway.env.example` (plantilla de variables; la cadena de la base usa referencias `${{Postgres.PGHOST}}` y por eso el servicio de la base tiene que llamarse `Postgres`), `deploy/migrate-to-railway.ps1` (copia la base real a la de Railway, compara las cuentas y se niega a pisar una base con datos), `.env.*` en `.gitignore` (el `.env.railway` con el token real nunca se sube) y la sección "Railway" de `DEPLOY.md` con el paso a paso y las cuentas. Verificado: la instalación limpia (`run_fresh_install.sql`) da la MISMA estructura y catálogo que la base real (apareció una deriva chica: `items.emoji` era TEXT en `schema.sql` y VARCHAR(100) en la base real; alineado), la copia por `pg_dump`/`pg_restore` sale idéntica y la cadena de conexión funciona con Npgsql 10. **No probado**: el build real en Railway y Docker (el primer deploy es esa prueba). Pendiente tuyo: crear el proyecto en Railway, correr el script de migración, pegar las variables y apagar el bot de tu PC (mismo token, una sola instancia).
- **Íconos más grandes, inventario en una columna y sin espadas repetidas** (2026-10-02): (1) **`/inventory` es UNA columna** (Madera, Mineral, Drops de monstruo, Comida, Cajas, de arriba hacia abajo, con una línea en blanco entre bloques) y los drops ya **no se parten en "(1/2)" y "(2/2)"**: ahora todo va en la descripción del embed (entra aunque tengas los 104 ítems en 9999 cada uno: ~4000 de 4096 caracteres) y, si algún día el catálogo no entra, lo que sobra sigue sin título. (2) **Sin íconos de espada/amuleto repetidos**: el perfil ya no pone 🗡️ / 📿 antes del arma y el amuleto (queda solo su emoji) y la página de recetas del herrero ya no antepone 🎯 / ⚔️ / 📿 al nombre ni tiene la leyenda al pie: el tipo (arma de tu clase / arma general / amuleto) va en cursiva debajo del nombre. (3) **Íconos grandes donde se puede**: dentro de un texto Discord muestra todos los emojis a 22 px y no hay forma de agrandarlos, así que donde UN ítem es el protagonista el mensaje lleva su imagen de **miniatura** (~80 px): lo que juntás con `/chop` y `/mine`, el drop de una victoria o auto-cacería, lo que forjás, la comida que usás y la caja que abrís. No lo hice en las listas (inventario, tienda, recetas) porque ahí hay muchos ítems a la vez y un emoji suelto de ese tamaño no entra; si querés otro lugar con miniatura decime cuál. Las opciones de los menús desplegables siguen sin emoji propio (si Discord rechaza uno, rechaza el mensaje entero), por eso el menú de venta de la taberna conserva su 🗡️ / 📿. Además: las armas de Zona 3 ya tienen emoji (15/28 en total).
- **Emojis de ítems pasados a emojis de la aplicación** (2026-10-02): los 87 emojis cargados (materiales 38/38, minerales, comidas, cajas, 10 amuletos + Capa y Carcaj de Pelaje Oscuro, Corteza del Árbol de Vida) ahora apuntan a los emojis del Developer Portal (no gastan los 50 lugares del servidor y se ven en cualquier servidor con el bot). `Database/update_item_emojis.sql` se regeneró entero y ya está aplicado a la base real. Decisiones mías para revisar: **Yunque del Capataz = `forja_hierro`** (subido en la misma tanda que Pelaje Plateado/Garra del Alfa/Brasa Eterna), **Cordero Patagónico = `cordero_epico`** (volvió a existir; antes usaba `fileteasado_epico`), **Empanada = `empanada_raro1`** y **Piedra = `PIEDRA_COMUN2`** (hay dos de cada una; quedó la numerada). Subidos y sin ítem (sobran en el portal): `pan_comun`, `chori_epicos`, `fileteasado_epico`, `empanada_raro`, `PIEDRA_COMUN`. **Armas**: las 15 de Zonas 1 a 3 ya tienen emoji (se cargaron enseguida, mismo script); **pendiente de emoji**: las 10 armas de Zonas 4 y 5 que se consiguen forjando (+ Arco Largo del Cazador, Báculo del Aprendiz y Dagas Gemelas de Sombra, que hoy no se consiguen), 4 amuletos que hoy no se consiguen (Bombilla de Hierro Maldito, Botas de Silencio, Collar de Hueso, Ojo de Jabalí) y las **4 maderas comunes** (siguen siendo emojis de servidor: resubirlas al portal antes de borrarlas del servidor).
- **`/heal` cura todo de una, raid con su cooldown y `all` en oro** (2026-10-02): (1) **`/heal`** ahora llena la vida de una: come de la mochila **lo necesario** (la combinación que menos curación desperdicia; no toca los banquetes) y lo anota en un mensaje; si no alcanza la comida, se la come toda y dice cuánto te falta. Opcional: `/heal comida:<nombre>` (con lista) para usar solo esa. En la taberna, "Comer algo" suma arriba de todo "🍖 Curarme del todo". (2) **El raid figura en `/cd`** (comparte cooldown con el jefe, 30 min) y **se bloquea al arrancar y al unirse** si lo tenés ocupado, diciendo cuánto falta. (3) **`/play` y `/give` aceptan `all`** (también todo / toda / max) además de un número ("500" o "1.000"). Idea a futuro, anotada: `marriage` (ver abajo).
- **El equipo se forja directo a equipamiento, nuevo perfil e historiales** (2026-10-02): (1) **Armas y amuletos ya no pasan por el inventario ni existe `/equip`**: al forjar quedan equipados solos; para cambiar hay que
  **vender el que llevás puesto** (en `/taberna` o con `/shop sell`) y después forjar el nuevo. Si el casillero está ocupado el herrero no cobra ni gasta nada y te avisa; la lista de la forja marca 🔁 "primero vendé tu arma"
  y 🟢 "ya la llevás". En la taberna, vender lo que llevás puesto sale primero en la lista con un ⚠️ y **pide confirmación** (un click de más te deja sin la pieza, y forjarla cuesta mucho más de lo que pagan).
  El consejo de `/tips` cuenta lo equipado como ya forjado, solo propone lo que MEJORA y avisa si hay que vender antes. A los jugadores que ya existían se los ordenó con `Database/move_gear_to_equipment.sql`
  (lo equipado se queda, sin copia en la mochila; las piezas de más se pagaron a su precio de venta: en la base real solo hubo una, 20 de oro). (2) **Inventario sin armas ni amuletos.** (3) **Perfil**: el arma
  va debajo del Ataque, el amuleto debajo de la Defensa y la racha debajo del Oro, en tres columnas, con filas en blanco entre bloques; ya no muestra el PvP. (4) **`/history`**: por juego, cuántas veces
  jugaste, ganaste y perdiste (cacería, viaje, jefe, raid, duelos, arena) y, en el casino, el oro ganado y perdido y el balance; **`/duels`**: tu récord y los últimos rivales con racha. Salen de los eventos que
  ya se registran (el casino y los duelos empezaron a registrarse hoy; la derrota de un raid también); se puede mirar el de otro jugador. Pendiente: filtrar el historial por período.
- **Sin rareza en drops y equipo** (2026-10-02): en un drop de monstruo, y en las armas y amuletos de la forja, la rareza es lo mismo que la zona (1 a 1), así que el "(Raro)" solo ocupaba lugar: `/drops`, el mensaje de victoria, el de `/autohunt` y el de `/equip` ya no lo muestran. La columna `items.rarity` NO se toca: sigue mandando en lo que sí la usa de verdad (la tirada de `/chop` y `/mine`, las cajas, el canje `/trade` que exige igual rareza y el tier de la comida). Para el arte de los íconos de drops y armas, el color por zona sigue sirviendo igual (es lo que ya tienen).
- **PvP: duelo amistoso y Arena diaria** (2026-10-02): (1) **`/fight @jugador`** (`aa fight @jugador`, también `aa duelo`): desafío con Aceptar/Rechazar y pelea por turnos con Atacar, la
  habilidad de la clase y Rendirse (60 s por turno; el que no juega pierde). Es solo por el honor: los dos arrancan con la vida completa y no se toca nada (ni vida, ni oro, ni XP). (2) **`/arena`**:
  `join` te anota en el torneo de hoy (máx. 32, mínimo 2), `listplayers` muestra quiénes van y `results` la llave del último. A las **00:00 de Uruguay** se arma una llave de eliminación directa con todos
  los anotados, las peleas se juegan solas (con tu nivel y tu equipo de ese momento; la habilidad se usa apenas está lista) y el **campeón** cobra 15 cacerías de oro de su zona + 6 % de XP + una caja de su zona.
  El bot **narra el torneo en el canal ronda por ronda** (apertura con los peleadores, un mensaje por ronda con una frase por pelea —paliza, "por un pelo", batalla larguísima— y el cierre con el campeón y la llave; 4 s entre mensajes) en el canal donde se anotó el primero (o en `Arena__ChannelId`), y arranca el torneo del día siguiente; si el bot estaba apagado a medianoche, lo juega apenas vuelve. `/profile` muestra una línea "⚔️ PvP" (duelos ganados/perdidos y campeonatos) solo si peleaste. (3) **Balance**:
  midiendo 4000 duelos por cruce salió que el Guerrero le ganaba 74-85 % a todos y el Arquero perdía 80-85 %, así que en PvP la vida se ajusta por clase (Guerrero ×0,90, Hechicero ×1,20, Arquero ×1,25,
  Ninja ×1,0): el peor cruce queda en 66/34 y se arma un piedra-papel-tijera (Guerrero > Ninja > Arquero/Hechicero > Guerrero). Hay que volver a medirlo si se tocan las clases o sus habilidades.
  Pendiente: un salón de la fama de campeones, premio para el subcampeón y duelos con apuesta (ver ideas).
- **Jefe que paga, nivel que se festeja, `/tips`, recetas por zona y respuestas aparte** (2026-10-01): (1) **El jefe pagaba menos que un `/travel`** (en Zona 2: ~280 XP contra ~520, una pelea dura con
  30 min de cooldown rendía la mitad que una de 10): ahora es la fórmula de `/hunt` con el bono grande del jefe **x6** (`CombatRewardCalculator.BossRewardMultiplier`) = ~3,2 veces un viaje de su
  zona (oro y XP; ~70 % de un nivel por jefe en zonas 2-5). Vale para `/boss` y `/raid` (cada participante cobra entero). Ojo con el ritmo: quien juega cazar + viajar + jefe en cada cooldown sube
  de nivel ~40 % más rápido; si hay que frenarlo, se baja esa constante. (2) **Subir de nivel es un mensaje propio** ("🎉 ¡SUBISTE DE NIVEL! 🎉", dorado y público): quién subió, `Nivel 6 ➜ Nivel 7`,
  +15 de vida máxima por nivel, vida curada, un aviso si el nivel nuevo alcanza una zona, y una frase al azar. Sale del evento `level_up` que ya registraban todos los comandos, así que sirve para combate,
  `/daily`, misiones y logros sin tocar cada módulo (en un raid solo lo recibe en el acto quien dio el golpe final; el resto lo ve en el mensaje del raid, y el aviso vence a los 2 minutos).
  (3) **El consejo de "qué farmear" salió de `/chop`, `/mine` y las victorias**: ahora es el comando **`/tips`** (`aa tips`, `aa consejo`; en slash solo lo ves vos), y esos mensajes quedan más cortos. (4) **Las
  recetas vuelven a verse por zona**: la herrería (`/forge`) tiene una segunda lista, "📜 Ver las recetas de una zona", que cambia la escena a las recetas de esa zona (con lo que tenés de cada material y
  un 🔒 si todavía no llegás al nivel); en texto, `aa forge recipes [zona]`. (5) **La respuesta del tabernero y la del herrero salen en un mensaje aparte**, debajo de la escena, en vez de pisar su texto.
- **`/forge` solo y `/taberna`** (2026-10-01): (1) `/forge` ya no tiene subcomandos: abre la herrería (foto del herrero + desplegable de recetas); `/forge make`, `/forge recipes` y
  `/blacksmith` se sacaron (en texto siguen `aa herrero`, `aa forge`, `aa forge make` y `aa forge recipes`). (2) **`/taberna`** (`aa taberna`): el tendero pasó a ser el tabernero (una sola taberna):
  su foto, la carta en dos columnas y cuatro desplegables para **comer** algo de tu mochila, **comprar comida**, **comprar una caja** (una por hora) y **vender**; cada elección es de
  una unidad y el tabernero contesta con su charla y tu oro actualizado. `/shop` queda como atajo directo (para cantidades). Si querés sacar `/shop` del todo, es borrar un módulo.
- **Cajas más difíciles, cofres con emoji y tienda más limpia** (2026-10-01): (1) Se recortó a la mitad la chance de lo mejor de cada caja (jackpots de oro, materiales Épicos y
  Legendarios, trofeos) y se subió el peso del oro común. Medido con el sorteo real: el **Cofre de Oro** trae algún material Legendario en ~36 % de las aperturas (antes ~64 %), el
  **Arcón de Hierro** uno Épico en ~50 % (antes ~70 %), y que una caja devuelva más de lo que costó pasó de 11-16 % a 6-9 %; el valor esperado quedó en 52-60 % del precio (antes
  54-67 %). El Arca del Soberano (premio) también bajó un poco. No se tocaron los precios: si todavía parece generoso, el siguiente paso es subirlos o recortar más (los pesos
  están en `Database/seed_boxes.sql`, que es re-ejecutable). (2) Las 5 cajas tienen su cofre por rareza (`cofre_comun` ... `cofre_mitico`). (3) `/shop view`: dos columnas
  (Comida | Cajas), cada ítem con su emoji y una línea de detalle; sin la rareza escrita, sin emojis de adorno y sin el precio de venta.
- **Fotos del herrero y el tabernero, "qué farmear" y minieventos** (2026-10-01): (1) Las fotos (256x256, ~25 KB) viajan con el bot en `Assets/npc/` y se adjuntan al
  mensaje como miniatura (`/blacksmith`, `/heal`): no hace falta hospedar nada; una URL en `Images__*` del `.env` gana si está. (2) **Consejo de qué farmear**: después de
  `/chop`, `/mine` y de ganar un `/hunt`, `/travel` o `/boss`, un campo "💡 Para tu próxima forja" dice qué receta tenés más cerca, qué te falta (hasta 2 cosas) y con
  qué comando se consigue; si ya podés forjar algo te manda al herrero. (3) **Minievento al azar**: después de un comando, con 3 % de chance (máximo uno cada 30
  min por canal) aparece "a un minero se le cayó una bolsa de piedras" (o leña, o plata) con un botón; quienes lo tocan en 15 segundos se llevan una recompensa chica
  que **crece con la cantidad de personas** (piedra/madera: 2 + 2 por persona, tope 10; plata: tantas cacerías de oro de tu zona como personas). Para probarlo:
  `MiniEvent__ChancePercent=100` y `MiniEvent__ChannelCooldownMinutes=0` en el `.env` (aparece uno en cada comando).
- **Inventario en columnas y recetas con "tenés X de Y"** (2026-10-01): `/inventory` ahora va en columnas con aire entre filas: Recolección (primero toda la madera y
  después la piedra y los minerales, por tipo) junto a Drops de monstruo, después Armas | Amuletos y Comida | Cajas; sin la rareza escrita en cada renglón. `/forge
  recipes` muestra por ingrediente cuánto tenés de cuánto hace falta (✅/❌) y el oro también.
- **Personajes que hablan, `/blacksmith`, drop de `/hunt` y mensaje de jefe** (2026-10-01): (1) Todo lo que dicen el **herrero, el tendero, el tabernero** (`/heal`) y los
  **5 jefes** (al aparecer, al caer y al vencerte; también en el raid) sale de `GameData/NpcDialogue.cs`, con varias frases por situación elegidas al azar. (2)
  **`/blacksmith`** (`aa herrero`): escena del herrero con su imagen (opcional: URL en `Images__Blacksmith` del `.env`), te pregunta qué necesitás y elegís de un
  desplegable (✅ lo que podés forjar / ❌ lo que te falta); te responde "En camino, loco" o "No tenés lo suficiente, crack". `/forge make` y `/forge recipes`
  se dejaron como atajo directo (si preferís sacarlos es borrar dos comandos). (3) **Drop de `/hunt`: 10 % → 6 %**. Medido: las recetas tardan ~11 % más en
  promedio (Zona 2: 159 → 182 min) porque casi todas ya estaban limitadas por la minería (Hierro) y no por los drops; el tiempo de juntar los materiales de monstruo
  sube ~40-70 %. Si querés más freno, bajar de nuevo la chance (o subir las cantidades de las recetas). (4) **Mensaje del jefe**: solo la PRIMERA vez que cae se
  anuncia que se abre la próxima zona; las siguientes dice "¡Volviste a ganarle!".
- **Tradeo, comandos en inglés y arreglos** (2026-10-01): (1) **`/trade`** (`aa trade @jugador "Madera de Roble" Hierro`): cambia 1 material por 1 de otro
  material **de la misma rareza** con otro jugador, solo de lo que dropean `/chop` y `/mine`. Uno propone, el otro acepta con un botón (2 minutos),
  y el cambio es atómico (probado con 40 cambios cruzados a la vez: no se traba y no se pierde ni se duplica nada). Si querías un cambio contra el
  bot en vez de entre jugadores, es un cambio chico, pero ojo: convertiría Roble y Carbón en Hierro sin límite. (2) **Bug del daily**: `aa daily` no
  registraba el evento, así que la misión del daily (y el logro Constante) no avanzaban para quien lo usa por texto; arreglado. (3) La misión semanal
  de materiales pasó de 60 a **300** (y la de 120 a 500, para que no quede más fácil que la de 300). (4) Comandos en inglés: `/open`, `/missions`,
  `/achievements` (`aa open`, `aa missions`, `aa achievements`; los nombres viejos siguen como alias de texto). (5) El herrero habla: sin materiales
  dice "No tenés lo suficiente, crack. Andá a farmear y después hablamo", y al forjar "¡En camino, loco! Queda pronta".
- **A mirar jugando (calibración)**: las 3 diarias valen ~15 cacerías de oro + una caja de la zona y las 2 semanales ~90 + una caja
  mayor. A un jugador que juega poco le suma mucho (rinde más cuanto menos jugás: es a propósito, para que vuelvan) y a uno que juega horas
  le suma ~10-15%. Si el oro sobra de más, se bajan las unidades en `GameData/MissionCatalog.cs` (sin tocar la base).
- **Migración de una base existente (v0.5.0 -> v0.6.0)**, en este orden (todos re-ejecutables): `add_game_events_and_stats.sql`,
  `add_boxes.sql`, `add_buffs.sql`, `add_missions_and_achievements.sql`, `rebalance_consumable_prices.sql`, `rework_food_catalog.sql` y
  `seed_boxes.sql`. **Hacé un backup antes**: `rework_food_catalog.sql` borra 3 comidas (reembolsa su valor en oro a quien las tenga).
- **Anotado para después** (ideas, no empezadas): un mini-evento aleatorio en el canal (una bolsa de piedras que se le cae a un
  minero: un botón, ~10 segundos para sumarse y un premio chico), el campeonato de PvP diario que se juega solo y deja un premio al
  ganador, y —muy a futuro— una membresía que baje los cooldowns (ojo: todo está calibrado por minuto, hay que re-medir antes).
- **Medido y por medir**: el +15 % de ataque no se simuló contra los jefes (el balance de zonas se calibró sin buff). A 2.100 de oro por
  30 minutos es caro a propósito; si en la práctica facilita demasiado los jefes, se baja el porcentaje en `item_buffs`.

## v0.5.0: listo para desplegar en cualquier hosting (2026-10-01)

- **Decisión de hosting**: Railway queda descartado por ahora —el proyecto del truco ya consume ~US$3,5 de los US$5 que incluye
  el plan, así que sumar este bot lo pasaría de largo—. Se dejó **independiente del proveedor**: `Dockerfile`,
  `docker-compose.yml` (bot + Postgres, la base carga sola el esquema la primera vez) y `DEPLOY.md` con las opciones
  (VPS con compose, otro hosting con Dockerfile, o tu PC) y una comparación de costos. **Todavía no está desplegado en ningún lado.**
- **Errores registrados** (lo más importante para producción): los 52 `catch (Exception)` de los módulos le respondían
  "¡Upa! Algo falló" al jugador y se tragaban la excepción; ahora cada uno hace `BotLog.Error(ex)` (archivo, método y stack).
  Los dos temporizadores en segundo plano (abandono de combate y lobby del raid) tenían un `catch { }` pelado que ocultaba hasta
  los errores de base; el de abandono de combate ahora separa "falló la base y el HP no se guardó" (error) de "ya no se puede
  editar el mensaje" (aviso). También se registran las excepciones sin manejar.
- **Arranque que falla rápido**: si la base no responde o está vacía corta con código 1 y un mensaje que dice qué hacer; si no
  conecta a Discord en 90 s (token inválido o intent privilegiado sin activar) también —antes quedaba un proceso "vivo" que no
  respondía—; apagado limpio con SIGTERM/Ctrl+C. Versión `0.5.0` en el log de arranque y en el pie de `/info`.
- **Medido**: el bot usa ~84 MB de RAM y 1,7 s de CPU en su primer minuto (en Windows; en Linux puede variar), así que la base de
  Postgres es lo que más pesa en el costo.
- **No probado** (sin Docker en la PC de desarrollo): construir la imagen y el compose, el apagado por SIGTERM en Linux, y
  clickear botones y desplegable en un Discord real. Está dicho en `DEPLOY.md`.

## Recolección por cantidad, curar en combate, comidas más caras y pantallas más limpias (2026-09-30)

- **`/chop` y `/mine` dan varias unidades** (`GameData/GatheringYield.cs`): **Común 1-5** (Madera de Pino, Piedra),
  **Raro y Épico 1-3** (Roble, Carbón, Hierro, Nogal, Oro Puro) y **Legendario y Mítico 1** (Ébano, Zafiro, Corteza,
  Meteorito). Hay un `RunMultiplier` (1 en la run 1) para que el reset post-Zona 5 lo suba sin reescribir nada. El
  resultado dice la cantidad ("Conseguiste **4× Madera de Pino**").
- **Recetas compensadas**: todo material de recolección pide ~3x (Común) o ~2x (Raro/Épico) lo de antes; los
  Legendarios y Míticos no se tocaron. Medí los minutos con el reporte (ahora incluye recolección): el ritmo quedó
  **igual** que antes (p. ej. Hacha de Hierro MK3: 5 Hierro a 1 por minada = 10 Hierro a ~2 por minada = 200 min).
  Dato que salió de medir: **el Hierro ya era el cuello de botella** (12.5% por `/mine` cada 5 min) — varias armas de
  zona 2-4 piden 150-200 min de minar contra ~100 de cazar drops, y las de Zona 5 llevan Meteorito/Corteza (~17 h).
  No lo toqué porque pediste paridad; si querés aflojarlo hay que bajar cantidades de Hierro o subir su chance.
- **Desplegable "Curar" en el combate, solo `/travel` y `/boss`** (`GameData/CombatHeal.cs`): un menú con la comida que
  tenés (la que más cura primero, se vuelve a leer cada turno), **una vez por pelea**; al usarla queda deshabilitado
  diciendo "Ya te curaste en esta pelea". En `/hunt` y en el raid no existe. Curarse sigue costando el turno (el
  monstruo contraataca). Elegir una comida pasa por **la misma lógica que `/use`**.
- **Decidí sin preguntar**: (1) el límite de 1 vale también para `/use` escrito en travel/boss (si no, el comando es una
  puerta trasera); (2) en **`/hunt` el `/use` escrito sigue como estaba, sin límite** — me dijiste que el desplegable no
  exista ahí, no que se prohíba curarse; si querés que tampoco se pueda, es una línea en `UseModule`; (3) **Épico entra
  en "intermedia" (1-3)**, no en una escala propia; (4) las opciones del desplegable **no llevan emoji**: si el bot no
  tiene acceso a un emoji custom, Discord rechaza el mensaje entero y se caería el inicio de cada travel/boss.
- **Comidas más caras** (`rebalance_consumable_prices.sql`, en la instalación limpia): antes todas valían ~0.22 oro/HP
  (la de 300 HP costaba 67 oro). Ahora compra ≈ 0.1·HP^1.6: **0.5 oro/HP la más chica y ~3 oro/HP la más grande** (Mate
  Amargo 8, Pan 12, Empanada 36, Choripán 52, Vacío 110, Asado de Tira 160, Cordero 300, Asado Completo 700, Mate
  Dulce 920); venta = 75%. Con una sola curación por pelea la que más cura pasó a valer bastante más, y con travel
  pagando ×10 el oro sobraba.
- **Pantallas más legibles**: `/forge recipes` muestra **una receta por bloque** (ítem y cuánto suma, el oro y cada
  ingrediente en su línea, con 🎯/⚔️/📿 y una leyenda al pie) en vez de una línea larga por receta; `/drops` manda **una
  tarjeta por zona** (color propio, un bloque por tipo de pelea, dos líneas cortas por monstruo) en vez de un bloque
  enorme por zona. Medí con datos reales todas las zonas y clases: el embed de recetas más grande mide 1068 de 6000 y
  `/drops` entero 1811.
- **Migración de una base existente**: re-correr `seed_recipes.sql`, `seed_zone2..5_gear_and_recipes.sql` y
  `rebalance_consumable_prices.sql` (idempotentes). Una instalación limpia (ahora 18 scripts) da el mismo resultado:
  65 filas (21 monstruos con sus drops + 35 recetas con ingredientes + 9 comidas con precio) idénticas a la base real.
- **Verificación**: arneses contra la base real con un usuario descartable (rendimientos, cantidades, precios, el
  desplegable de punta a punta con `/use`, el límite en travel y boss, hunt sin límite, el tamaño de los embeds) y los de
  raid, fugas y autocompletado de forja, todos OK. Lo que **no** pude probar desde acá: clickear el desplegable en Discord.

## Drops: un solo ítem por monstruo, chances bajas y `/drops` (2026-09-30)

- **Pedido**: un comando que liste por zona los monstruos y qué sueltan; que cada monstruo suelte **un** ítem y no
  dos; bajar las chances porque avanzar era muy fácil (primero dijiste 20% hunt / 30% travel y después lo
  dejaste en **10% hunt / 20% travel**); menos monstruos de `/hunt`; y recetas de modo que queden **2 armas y 2
  amuletos**. Todo son valores de la run 1: el reset que se desbloquea al terminar la Zona 5 los va a ir subiendo.
- **`/drops` y `aa drops`** (`Modules/DropsModule.cs`, armado puro en `GameData/DropsCatalog.cs`): una zona por
  bloque con Cazar / Viajar / Jefe, la chance y `🐗 Jabalí Rabioso → Colmillo de Jabalí (Raro)` por monstruo; marca 📍
  tu zona y no crea cuenta al mirarlo. Lee las chances de las mismas constantes que usa el combate.
- **Plantel** (`finalize_monster_roster.sql`, fuente de verdad de los drops de hunt y jefe): Zona 1 pasa de 6 a **3
  monstruos de `/hunt`** (Jabalí Rabioso, Perro Cimarrón, Ñandú Salvaje; se van Lobisón de las Cenizas, Gólem de
  Escoria y Cuatrero No-Muerto) escalados ×1.15 HP / ×1.1 daño para que la zona cueste lo mismo (medido: 6.0 turnos
  y 32% de la vida, contra 33% con los 6); zonas 2-5 siguen con 2. Más 1 de travel y 1 jefe por zona. **Cada uno suelta
  exactamente 1 ítem**; los nombres de travel se ajustaron a lo que sueltan (Toro Bravo → Cuero Grueso, Ciervo
  Sagrado → Ceniza Bendita, Mole de Escoria → Escoria Metálica Densa, Dragón de Lava → Aliento de Fuego Eterno,
  Quimera del Abismo → Ceniza del Abismo). Cada zona tiene 4 materiales de drop: 2 "a granel" (hunt), 1 "escaso"
  (travel) y 1 "raro" (jefe).
- **Chances**: hunt **10%**, travel **20%**, y el **jefe 15%** (constante propia). El jefe no me lo habías pedido: hasta
  ahora compartía la fórmula de hunt, y bajar hunt a 10% lo habría dejado en 10% sin que lo decidas; con un solo
  ítem por jefe, 15% mantiene el ritmo de siempre (~200 min por unidad).
- **La trampa que hubo que compensar**: con un ítem por monstruo cada ítem puntual cae **más seguido** que con dos
  (zonas 2-5: de 7.5% a 5% por cacería con las chances nuevas, pero con las de 20/30 habría sido 10%), así que bajar
  solo los porcentajes no hacía más lento el avance. Medí los minutos de farmeo de cada receta
  (`Database/report_recipe_pacing.sql`, 1 cacería por minuto, 1 viaje cada 10, 1 jefe cada 30) y subí las cantidades:

  | | Antes (30% / 50%, 2 ítems) | Ahora (10% / 20% / 15%, 1 ítem) |
  |---|---|---|
  | Zona 2-5: arma de afinidad | 20-40 min | **~100 min** (2 del drop de travel) |
  | Zona 2-5: arma general / amuleto bajo | 10-40 min | **~80 / ~100 min** |
  | Zona 2-5: amuleto alto (drop de jefe) | ~200 min | **~200 min** (igual) |
  | Zona 1: armas iniciales / Hoja +15 / Levantador | 40 / 20 / 20 min | **30 / 60 / 30 min** |
  | Zona 1: Hombreras (drop de jefe) | 120 min | **~200 min** |

  La vara: el equipo de una zona tiene que costar más o menos lo que cuesta subir de nivel dentro de ella (~95-110 min
  de juego seguido en las zonas 2-5: XP por nivel `100·L^1.5`, ~1000 XP cada 10 min con hunts + un travel).
  Esos minutos son de juego SEGUIDO sin curarse: en la práctica son más.
- **Recetas: 7 por zona (4 afinidad + 1 general + 2 amuletos), 35 en total; cada jugador ve 2 armas y 2 amuletos.**
  Se sacó la segunda arma general de cada zona (Machete de Chacra, Alabarda del Alfa, Martillo de Fragua, Hacha de
  Obsidiana, Guadaña de Almas): con un drop por monstruo no alcanzaban las fuentes y ninguna le ganaba al arma de
  afinidad de nadie, así que el escalón de equipo (afinidad +20/+32/+50/+80) no cambia. Las recetas se reescribieron
  contra los drops nuevos (los "a granel" van en cantidades grandes, el de travel x2 en cada arma de afinidad y el
  del jefe x1 en el amuleto alto). **Todos los drops los usa alguna receta y todo material de receta tiene fuente**
  (chequeado con la base).
- **Sobrantes, sin tocar**: 17 materiales quedaron sin ningún monstruo que los suelte (Pelaje Oscuro, Garra Maldita,
  Núcleo Ígneo, Hueso Añejo, Cuero Curtido de Pradera, Collar de Cuero Viejo, Tela Rasgada, Piedra Caliente, Corona de
  Cerdas, Rama Carbonizada, Garra del Alfa, Polvo de Mina Sagrada, Martillo del Capataz, Roca Volcánica Pura, Colmillo
  del Señor del Volcán, Escoria Pura del Cráter, Corona de Escoria Viva). No los borré: algunos están en mochilas
  (Hueso Añejo x5, Piedra Caliente x3...) y `inventory.item_id` es `ON DELETE CASCADE`. Siguen vendiéndose; si querés
  limpiar el catálogo es un script con guarda de inventario. Las 5 armas generales sacadas sí se borraron del
  catálogo (nadie las tenía; `remove_extra_general_recipes.sql` lo chequea).
- **Migración de una base existente**: re-correr `seed_travel_monsters.sql`, `finalize_monster_roster.sql`,
  `seed_recipes.sql`, `seed_zone2..5_gear_and_recipes.sql` y al final `remove_extra_general_recipes.sql`. Una
  instalación limpia (`run_fresh_install.sql`, ahora 17 scripts) da **exactamente** el mismo resultado: probado
  instalando en una base descartable y comparando fila por fila monstruos, drops, recetas e ingredientes.

## `/travel` sigue la escalera: un monstruo élite por zona y recompensa x10 (2026-09-30)

_Reemplaza lo que dicen más abajo sobre `/travel` (pool fijo de 5 monstruos genéricos, drop por rareza sorteada,
"mismo pool de `type = 'Material'`" y recompensa fija). Los drops y las chances de esta sección quedaron
superados por la sección de arriba (1 ítem por monstruo, travel 20%)._

- **Problema**: la escalera de zonas (monstruos, jefes y recetas) dejó afuera a `/travel`. Seguía con 5 monstruos
  genéricos de 50-90 HP / 12-24 de daño **sin importar la zona** (en Zona 5 era un paseo: un común de ahí tiene
  541-812 HP), una recompensa fija (~150 oro / ~155 XP a nivel 5 y `+6` por nivel) y un drop de material al azar
  de cualquier zona. En Zona 5 una sola cacería común ya pagaba más (~377 oro / ~285 XP) que un `/travel`
  entero, que encima tiene 10 minutos de cooldown contra 1.
- **Un monstruo dedicado por zona** (`monsters.is_travel`, separado del pool de `/hunt` y del jefe; tu pedido:
  "2-3 por zona para hunt y 1 para el travel"): Toro Bravo 🐂, Ciervo Sagrado 🦌, Mole de Escoria 🪨, Dragón de
  Lava 🐉 y Quimera del Abismo 🦂. Se cargan con `seed_travel_monsters.sql` (migración para bases existentes:
  `add_monster_is_travel.sql`). El pool de `/hunt` **no creció** (y después se recortó la Zona 1 a 3, ver arriba).
- **Dificultad medida** con el `CombatTurnResolver` real (mismas 4 clases, equipos y niveles que la escalera): el
  monstruo de travel es el promedio de los rangos de los comunes de su zona con **HP ×1.25 y daño ×1.1** —un
  élite, no un jefe—. Al entrar con el equipo de la zona anterior cuesta ~62-68% de la vida (un común: ~47%) y se
  pierde 12-16% de las veces (Zona 1, ~2%); con el equipo propio cuesta ~23-32% (un común: 13-18%) y casi nunca se
  pierde. Se probó también ×1.35/×1.15 y ×1.5/×1.2: ya con el primero se pierde 22-31% al entrar, demasiado para
  algo opcional. Huir lo cierra sin perder nada salvo la recompensa, como en cualquier pelea.
- **Drop específico**: el travel suelta el ítem **de su propio monstruo** (primero eran 2 con 50% de chance; hoy es
  1 con 20%, ver arriba). Son materiales que **ya existían** en la zona: no nacen ítems sin receta ni sin emoji.
  Verificado en la base que **todos** los materiales tienen algún monstruo de origen, así que sacar el sorteo por
  rareza (`RarityCatalog.RollTravelRarity`) no dejó nada sin fuente. Se borró también el pool fijo
  `MonsterCatalog.TravelMonsters`.
- **Recompensa**: la fórmula entera de `/hunt` (nivel + bonus del monstruo) **×10** (`TravelRewardMultiplier`),
  que es lo que vale el cooldown de 10 minutos. Así sube sola con la zona y en Zona 1 queda casi igual que antes
  (~120 oro / ~150 XP a nivel 1 contra ~126 / ~131). A nivel de entrada de cada zona: Z2 ~580 oro / ~490 XP,
  Z3 ~1200 / ~950, Z4 ~2200 / ~1700, Z5 ~3800 / ~2850 (un nivel de XP cuesta 1118 / 3162 / 5809 / 8944).
- **Decidí sin preguntar** (decime si querés otra cosa): (1) que el travel **reuse** materiales de la zona en vez
  de inventar ítems exclusivos —los 10 ítems nuevos quedarían sin receta y sin emoji—; si querés drops exclusivos
  de travel que entren en recetas, es un cambio chico en el seed; (2) el factor del élite y el ×10 (los dos son
  una constante); (3) la Zona 1 seguía con 6 monstruos de `/hunt` (luego me pediste recortar y se hizo: ver arriba).
- **A vigilar**: el oro de travel en Zona 4-5 (miles por viaje) es mucho más que antes —hoy el oro solo compra
  consumibles y recetas (150-350), no es cuello de botella— y se hace más relevante si después se suma un sumidero
  de oro o el reset post-Zona 5. Se ajusta en una sola constante.
- **Verificación**: arnés contra la base real (monstruo por zona, fuera del pool de `/hunt`, rangos de recompensa
  exactos, chance de drop, arranque de punta a punta con un usuario descartable, cooldown no cobrado si la zona no
  tiene monstruo de travel) + los arneses previos de raid, fugas y autocompletado, todos OK.

## Escalera de zonas: las 5 zonas con un salto de dificultad y de equipo cada una (2026-09-30)

- **Diagnóstico medido** (simulación con el `CombatTurnResolver` real, 4 clases, sin consumibles): la dificultad
  no subía pareja y el equipo de Zona 1 rompía todo. Sin ningún arma, un nivel 7 en Zona 2 la pasaba en 3 turnos
  perdiendo 6% de vida; en Zona 3, 4.5 turnos y 18%; con el Hacha +35 (que se forjaba en Zona 1) hasta el jefe
  de Zona 2 caía perdiendo 24%; y Zona 5 era una pared (93% de derrota sin arma).
- **Principio de diseño**: cada zona pide un **salto de equipo**, y el equipo de cada zona es ×1.6 el de la
  anterior. Los monstruos se calibran para que (1) al **entrar** con el equipo de la zona anterior una pelea común
  dure ~5 turnos y cueste ~47% de la vida (~2% de derrota), (2) al **salir** con el equipo propio cueste 13-18%
  (cómoda, nunca un paseo), (3) el **jefe** sea el examen: con el equipo de la zona anterior se pierde 67-87% de
  las veces; con el de la zona, ~16% (con ~70% de la vida); con el amuleto alto que dropea el propio jefe, 4-7%.
- **"Bajar el daño y la def de las cosas" (tu idea): sí, y fue lo correcto.** Mi primera escalera (+35 · +55 ·
  +72 · +85) era demasiado fuerte: con ella la pelea común al salir de cada zona costaba 2-8% de la vida. Resolví
  la escalera con el simulador en vez de inventarla: el objetivo de "salida" controla qué tan rápido se infla
  (con ~8% de vida al salir el arma de Zona 5 pedía +160; con ~15% pide +80), y además fijé una escalera
  **nominalmente creciente** (con el objetivo "libre" el arma de Zona 2 salía igual que la Hoja de Zona 1).
- **La escalera** (arma de afinidad, ×1.5 por sinergia de clase · armas generales bajo/alto, sin familia ·
  amuletos bajo/alto, siempre generales):

  | Zona | Afinidad | Generales | Amuletos | Rareza |
  |---|---|---|---|---|
  | 1 Praderas | +5 (inicial) | +10 / +15 (Hoja) | +10 / +16 | Común-Raro |
  | 2 Bosque de Cenizas | **+20** | +18 / +26 | +18 / +24 | Épico |
  | 3 Minas del Yunque | **+32** | +28 / +40 | +30 / +38 | Legendario |
  | 4 Cordillera del Fuego | **+50** | +44 / +66 | +46 / +60 | Legendario |
  | 5 Cráter de la Escoria | **+80** | +70 / +105 | +75 / +95 | Mítico |

  El "bajo" se forja sin drop de jefe; el "alto" lleva un drop del jefe de la zona (Garra del Alfa, Pelaje
  Plateado del Alfa, Martillo/Yunque del Capataz, Colmillo del Señor del Volcán, Brasa Eterna, Corona de Escoria
  Viva, Corazón del Soberano). Las 4 de afinidad de Zona 5 llevan además **1 material Mítico de recolección**
  (Fragmento de Meteorito o Corteza del Árbol de Vida, ~0.5% por acción: ~17 h por unidad): el objetivo de largo
  plazo de la run. Las recetas son `seed_zoneN_gear_and_recipes.sql` (N = 2 a 5; la de Zona 1 es
  `seed_recipes.sql`): 8 por zona, **40 en total**, todas con el molde 4+2+2 y verificación en voz alta.
- **Monstruos y jefes** (HP / daño; recompensa de los comunes ×2.5 la de antes, porque cada pelea cuesta vida):

  | Zona | Comunes | Jefe (nivel al que se desafía) | Bonus comunes | Bonus jefe |
  |---|---|---|---|---|
  | 2 | 136-234 / 33-60 | Lobisón Alfa 499-635 / 54-79 (nv 10) | 38 oro / 30 XP | 300 / 255 |
  | 3 | 250-397 / 55-95 | Capataz de Hierro 743-929 / 78-112 (nv 15) | 88 / 70 | 600 / 510 |
  | 4 | 376-563 / 82-127 | **Señor del Volcán** (nuevo) 1072-1310 / 112-149 (nv 20) | 175 / 138 | 950 / 900 |
  | 5 | 541-812 / 111-175 | **Soberano de la Escoria** (nuevo, el jefe FINAL de la run) 1540-1847 / 157-203 | 325 / 250 | 1500 / 1500 |

  Los jefes de las zonas 4 y 5 no existían (el bloqueo de progresión se apagaba desde Zona 3); ahora hay uno por
  zona, con 2 drops nuevos cada uno. El de Zona 5 no tiene zona siguiente, así que `/boss` no le pide nivel: el
  filtro es el equipo. Zona 1 (incluido el Rey Jabalí) **no se tocó** salvo las Hombreras (+20 → +16, para que los
  amuletos de Zona 2 sean nominalmente mayores).
- **Verificado con los datos REALES de la base** (monstruos, jefes e ítems de verdad, sinergia real de cada arma
  por clase, cálculo del bot): entrar con el equipo de la zona anterior = 4.7-5.0 turnos y 45-48% de vida (1-2% de
  derrota); entrar **sin arma** = 40-79% de derrota (Z2 40%, Z3 54%, Z4 63%, Z5 79%); salir = 13-18%; jefe con el
  equipo anterior 67-87% de derrota, con el de la zona 15-18%, con el amuleto alto 4-7%.
- **Raid** (se mantienen ×1.5/×1.1 de la Zona 2 en adelante): con el equipo de la zona (afinidad + amuleto
  bajo) da **solo 25-28%, de a dos 55-59%, de a tres 63-70%, de a cuatro 80-87%**, igual en las 4 zonas, así que
  un solo multiplicador alcanza. (Antes de este rebalance el raid de Zona 2 había quedado imposible; ver
  `RaidDifficulty`.)
- **Aplicado hoy** (base real + seeds): `seed_zones_and_monsters.sql` → `seed_zone_bosses.sql` → `seed_recipes.sql` →
  `seed_zone2..5_gear_and_recipes.sql`, **ya corridos**, ensayados antes en una copia con `pg_dump` (inventario y
  equipados intactos). Instalación limpia probada con `run_fresh_install.sql` (15 scripts): 40 recetas, 5 jefes.
  Se eliminó el borrador `draft_zone2_3_gear_and_recipes.sql` (obsoleto) y `rebalance_zone2.sql` (redundante: los
  seeds son re-ejecutables y son la fuente de verdad).
- **Bug latente que encontré y arreglé**: `/forge make` con un nombre mal escrito respondía con la lista de TODAS
  las recetas; con 40 (y el emoji de cada una) pasaba el límite de 2000 caracteres de Discord y el comando
  fallaba justo cuando te equivocabas. Ahora te manda a `/forge recipes` o a la lista desplegable.
- **Lo que hay que tener en cuenta (decisiones que tomé)**: las Legendarias de afinidad de Zonas 3 y 4 ya existían
  y son **exclusivas de su clase** (`class_requirement`), mientras que las de Zonas 1, 2 y 5 son por familia: no
  cambia nada de lo que ve cada clase, pero es una inconsistencia; los nombres de esas Legendarias viejas
  (Facón de Hueso Añejo, Cuchillos de Ceniza…) no combinan con sus zonas (Cordillera del Fuego) — si querés, se
  renombran. Los materiales de las zonas altas salen del cuello de botella de siempre (Hierro ~12.5% por `/mine`;
  Zafiro/Ébano ~4.5%; los Míticos 0.5%).
- **Reset a futuro** (dicho por vos, sin implementar): se desbloquea al terminar la Zona 5 (derrotar al
  Soberano) y da más % de drop y más cantidad de materiales. Todo está calibrado contra las tasas **base de la
  run 1** (30% de drop por cacería, 1 material por drop, 1 por `/chop` o `/mine`). Para sumar zonas más allá de la
  5: repetir el procedimiento (correr el simulador de escalera con el equipo de la zona anterior como entrada,
  ×1.6 de arma por zona, y cargar monstruos + jefe + 8 recetas).
- **Sin probar en Discord**: cómo se siente jugar cada zona. Las cifras salen de simulación (sin consumibles, con
  habilidad apenas está lista), no del criterio de cada jugador; la curva real depende de cuánto se cure la gente.

## Recetas: molde de 8 por zona, y cada uno ve solo la de su zona (2026-09-30) — _hoy son 7 por zona (4+1+2), ver "Drops" arriba_

- **Molde por zona** (decisión tuya): **4 armas de afinidad (1 por clase) + 2 armas generales + 2 amuletos
  (siempre generales) = 8 recetas**. Cada jugador ve solo **5**: su arma de afinidad + las 2 generales + los 2
  amuletos, y solo las de **su zona actual** — así el mensaje de `/forge recipes` mide ~900-970 caracteres de los
  6000 que permite Discord (antes, con todas las zonas mezcladas, había un caso que reventaba el embed).
- **Zona 1, tal como quedó** (en tu base real y en `seed_recipes.sql`):

  | Grupo | Receta | Suma | Oro | Materiales |
  |---|---|---|---|---|
  | Afinidad (Guerrero) | Espada de Madera | +5 ATQ | 40 | 3 Madera de Pino + 1 Hierro |
  | Afinidad (Ninja) | Daga Oxidada | +5 ATQ | 40 | 1 Hierro + 2 Piedra + 1 Colmillo de Cimarrón |
  | Afinidad (Arquero) | Arco Corto de Sauce | +5 ATQ | 40 | 3 Madera de Pino + 1 Tela Rasgada (la "cuerda") |
  | Afinidad (Hechicero) | Grimorio Desgastado | +5 ATQ | 40 | 2 Madera de Pino + 1 Pluma de Ñandú |
  | General | Hoja de Acero Puro | +15 ATQ | 180 | 3 Hierro + 2 Madera de Pino + 1 Colmillo de Jabalí |
  | General | Machete de Chacra | +10 ATQ | 100 | 2 Hierro + 2 Madera de Pino + 1 Cuero Grueso |
  | Amuleto | Amuleto del Levantador | +10 DEF | 150 | 5 Piedra + 1 Colmillo de Jabalí |
  | Amuleto | Hombreras de Cuero Grueso | +16 DEF | 200 | 5 Cuero Grueso + 3 Piedra Caliente |

- **Qué muestra cada comando** (`GameData/RecipeCatalog.cs`, un solo lugar para los dos): `/forge recipes` (y
  `aa fr`) dice la zona, agrupa en "🎯 Tu arma de clase / ⚔️ Armas generales / 📿 Amuletos" y pone al lado de
  cada ítem cuánto suma (`+5 ATQ (+8 con tu clase ⭐)`, `+20 DEF`). La lista de `/forge make` muestra
  exactamente las mismas 5, con ✅ las que ya podés hacer y ❌ qué te falta en las demás. **Si tu zona todavía no
  tiene recetas propias** (hoy solo hay de Zona 1 — tu Hechicero está en la zona 2) te muestra las de la zona
  anterior más cercana que sí, con un aviso, para no dejar sin herrería a quien ya avanzó. Forjar por nombre
  sigue funcionando para cualquier receta (solo se limita lo que se *muestra*).
- **Datos**: `recipes.zone_id` y `recipes.affinity` (`schema.sql`; `add_recipe_zone_and_affinity.sql` para bases
  existentes). `affinity` es un dato explícito porque una arma **general** también tiene familia (la Hoja es de
  Espadas) y no se podía deducir cuál es cuál mirando el ítem. Los 7 amuletos Legendarios de clase perdieron su
  clase exclusiva (los amuletos son siempre generales).
- **Lo que se sacó**: las 15 recetas Legendarias de clase (+45/+55) y las 8 de zonas 2 y 3 que había agregado.
  Los ítems Legendarios de clase siguen en el catálogo, sin receta (las recetas están en el historial de git).
  Los 8 ítems nuevos de zonas 2 y 3 (Arco de Ébano, Espadón de Zafiro, etc.) se **borraron** del catálogo — nadie
  los tenía — pero el diseño quedó guardado como **borrador** en `Database/draft_zone2_3_gear_and_recipes.sql`
  (NO se corre en la instalación) para rehacerlo con este molde (4 afinidad + 2 generales + 2 amuletos por zona).
- **Migración de tu base real**: `add_recipe_zone_and_affinity.sql` → `trim_recipes_to_zone_template.sql` →
  `seed_recipes.sql`, **ya corridos** (31 → 8 recetas). El trim borra ítems solo si nadie los tiene
  (`inventory.item_id` es `ON DELETE CASCADE`, borraría inventarios en silencio): lo probé en una copia exacta de tu
  base (con `pg_dump`) — con un jugador con el ítem **aborta** sin tocar nada, sin él corre y da el molde.
- **`seed_recipes.sql` se verifica solo**: si falta un ítem o zona **falla en voz alta** nombrando cuál (antes una
  receta con un ítem inexistente se omitía en silencio o se creaba sin ese ingrediente). Instalación limpia
  probada con `run_fresh_install.sql`: 8 recetas, 4 de afinidad, 3 jefes (el script de jefes tampoco estaba en la
  instalación; ya se agregó).
- **Sobre el raid** (lo que te avisé antes): al salir las Legendarias +55 de las recetas, lo más fuerte que se puede
  forjar hoy es el Hacha +35 (+52 con sinergia de Espadas). El raid difícil sigue fácil para niveles altos
  (medido a nivel 8: con un +15 un jugador solo gana ~97%), porque su calibración es para nivel ~6 con un arma
  +5. Si querés que aguante más nivel, la palanca es que el jefe escale con el nivel/poder del grupo.
- **Sin probar en Discord**: cómo se ve el embed (las líneas con emojis de ingredientes son largas) y la lista
  desplegable. Sí se verificó contra la base real con los repositorios de verdad (el mapeo de Dapper y el texto
  exacto que ve cada clase).
- **Idea chica, sin hacer**: en la lista de `/forge make`, marcar lo que ya tenés (hoy tu Hechicero se ofrece
  forjar la Hoja de Acero Puro que ya tiene equipada).
- **Zonas renumeradas 1, 2, 3, 4, 5** (pedido tuyo): tu base tenía los IDs **1, 4, 5, 6, 7** (una carga fallida vieja
  gastó números de la secuencia), y `/zona`, `/zonas`, `/profile` y las recetas muestran ese ID, así que el Bosque
  de Cenizas era la "Zona 4". `Database/renumber_zones_consecutively.sql` los renumera **de corrido en orden de
  nivel** y actualiza todo lo que guarda un ID de zona en una sola transacción (`zones`, `monsters`, `recipes`,
  `users.current_zone_id`, `users.highest_zone_cleared`), soltando y recreando idénticas las claves foráneas. Ya
  aplicado en tu base (tu Hechicero pasó de zona 4 a zona 2, monstruos por zona intactos 7/3/3/2/2, sin
  huérfanos, la próxima zona nueva sería la 6), tras ensayarlo en una copia con `pg_dump`. Es re-ejecutable (si ya
  están consecutivas no hace nada). Una instalación limpia ya sale 1..5 sola.

## Listas de zona y forja + 5 recetas de entrada (2026-09-30)

> **Parcialmente superado** por la sección de arriba: las recetas quedaron recortadas al molde de 8 por zona
> (las de entrada siguen, como armas de afinidad y generales). Lo de las listas desplegables de `/zona` y
> `/forge make` sigue vigente. Lo del cuello de botella del Hierro (~12.5% por `/mine`) también.

- **`/zona`**: lista todas las zonas en orden de dificultad, marcadas: 📍 estás acá / 🔒 te falta nivel /
  🔒 derrotá a <jefe> / sin marca si podés entrar. Las bloqueadas se listan igual (elegirlas da el aviso
  de siempre) porque sirve ver cuál sigue y qué falta. Se busca por nombre o por ID. La regla del
  jefe-guardián estaba inline en `ZoneModule`; la saqué a `ZoneRanking.PendingGatekeeperZone` para que
  `/zona` y la lista usen **una sola definición** (verificado: da lo mismo que la regla original en las
  24 combinaciones zona × progreso).
- **`/forge make`**: lista las recetas de tu clase con lo que **ya podés forjar arriba (✅ + costo)** y
  debajo lo demás diciendo qué falta (❌ falta: 2 Hierro, 80 oro), ordenado de lo más cerca a lo más
  lejos — sirve de guía de qué conseguir. Las exclusivas de otras clases no se ofrecen.
- **Recetas de entrada (5 nuevas)**: las 4 armas Comunes +5 y la primera Rara +15 ya existían como
  ítems pero **no tenían ninguna receta**, y las demás saltan directo a Legendarios (+45/+55). Casi todo
  con recolección y **como mucho 1 unidad de 1 drop**: cada drop puntual sale ~2.5% por cacería
  (30% de drop × 1 de 6 monstruos × 1 de 2 drops) o sea ~40 cacerías por unidad, así que pedir más de 1
  vuelve la receta un suplicio.

  | Receta | Oro | Materiales |
  |---|---|---|
  | Espada de Madera (+5) | 40 | 3 Madera de Pino + 1 Hierro |
  | Arco Corto de Sauce (+5) | 40 | 3 Madera de Pino + 1 Tela Rasgada (la "cuerda") |
  | Daga Oxidada (+5) | 40 | 1 Hierro + 2 Piedra + 1 Colmillo de Cimarrón |
  | Grimorio Desgastado (+5) | 40 | 2 Madera de Pino + 1 Pluma de Ñandú |
  | Hoja de Acero Puro (Raro +15) | 180 | 3 Hierro + 2 Madera de Pino + 1 Colmillo de Jabalí |

  Oro por encima del precio de venta del resultado (20 / 150) más los materiales, para que forjar y
  revender no sea negocio (el problema del Hacha MK3). Además bajé el **Amuleto del Levantador** de 3 a 1
  Colmillo de Jabalí (3 eran ~2 horas de cazar) — su receta **no existía en tu base** (estaba en el seed
  pero nunca se corrió), quedó repuesta. La base real pasó de 17 a 23 recetas.
- **Cuello de botella que queda**: el **Hierro** sale ~12.5% por `/mine` (25% de rareza Rara × 1 de 2
  minerales Raros) con 5 min de cooldown, o sea ~40 min por unidad. La Espada de Madera pide 1 y la Hoja
  de Acero 3 (~2 h de minar). Si sigue siendo lento, la palanca es el peso del Hierro en el sorteo de
  `/mine`, no las recetas.
- **⚠️ Bug de orden en la instalación desde cero (encontrado y arreglado)**: `seed_recipes.sql` corría
  *antes* de `seed_zones_and_monsters.sql` y `seed_consumables_and_base_swords.sql`, donde nacen ítems que
  las recetas nuevas usan. Una receta cuyo ítem no existe se omite **en silencio** — o peor, se crea sin
  ese ingrediente: la Daga quedaba sin Colmillo y el Grimorio sin Pluma (más fáciles de lo previsto, sin
  ningún error). Reproducido en una base vacía descartable (21 recetas, 2 faltando, 2 incompletas) y
  arreglado moviendo `seed_recipes.sql` al final de los seeds (`run_fresh_install.sql` y `CLAUDE.md`
  actualizados): instalación limpia = **23 recetas, todas completas**. Ya está aplicado en tu base real.
- **Deriva que noté (RESUELTA: `seed_recipes.sql` fija Común +10, ver la escalera de zonas)**: el **Amuleto del Levantador** es *Común +10* en tu base real y *Épico +15*
  en una instalación limpia desde los scripts. No sé cuál es el correcto; lo dejo para que decidas.
- **Sin probar en Discord**: el look de los desplegables (mismas limitaciones que las de comprar/equipar).

## Raid mucho más difícil + listas desplegables para comprar y equipar (2026-09-30)

- **Raid, medido**: hasta ahora el jefe de raid era idéntico al de `/boss` con HP fijo. Un jugador solo
  lo ganaba el **98%** de las veces y tres jugadores el **100%** en ~2 rondas (simulación con el
  `CombatTurnResolver` real, 4 clases mezcladas, cada jugador con su vida y sin curaciones). Ahora el
  jefe de raid tiene **HP ×2.2, daño ×1.35, y +50% del HP base por cada jugador extra** (se calcula al
  arrancar, con el grupo ya definitivo; `GameData/RaidDifficulty.cs`, todo en constantes). Resultado,
  nivel 6 con un arma +5 (la más barata): solo **20%**, de a dos **65%**, de a tres **79%**, de a
  cuatro **91%**; con un arma +15 hasta uno solo gana ~75%. **Sin arma, nivel 6: solo 1%, de a dos
  13%, de a cuatro 41%** — a propósito, el raid asume equipo. Ojo con probarlo solo (`Raid__MinParticipants=1`):
  hace falta un arma equipada, y cuanto más fuerte mejor.
- **Recompensa del raid sin tocar** (por participante, la misma que `/boss`, ×3 desde el rebalance de
  arriba). Ahora que es varias veces más difícil, quizá convenga un multiplicador propio del raid; lo
  dejé fuera porque toca la economía y no lo pediste.
- **Listas para elegir** (autocompletado de Discord): `/shop buy` muestra los consumibles con cuánto
  curan y cuestan (y "te falta oro" en los que no alcanzás a pagar), y `/equip` muestra solo lo que
  tenés en el inventario, es un arma o amuleto y puede usar tu clase — armas primero, la más fuerte
  arriba, con el daño ya con la sinergia de clase (⭐) y "equipado" en lo que llevás puesto. Escribir
  filtra (sin importar mayúsculas ni tildes). Elegir de la lista no es obligatorio: tipear el nombre a
  mano sigue andando. Código en `Modules/ItemAutocomplete.cs`; la lógica de armar las opciones está en
  funciones estáticas puras (`ItemChoices`), sin Discord, y se verificó con un arnés (21 checks). Ese
  arnés encontró un caso borde real: un nombre de ítem de más de 100 caracteres hacía explotar la lista
  entera (Discord.Net tira excepción), así que ahora se omite.
- **Limitaciones**: los comandos de texto (`aa shop buy`, `aa equip`) **no** pueden tener lista — Discord
  no ofrece autocompletado para mensajes normales. `/shop sell`, `/use` y `/forge craft` también piden
  un nombre y se les puede sumar la misma lista con pocas líneas (pediste comprar y equipar, no los toqué).
  **No probado en Discord**: el look del desplegable (cada tecla dispara una consulta a la base; con
  este catálogo chico no debería notarse, y Discord exige responder en menos de 3 s).

## Fix del raid trabado + rebalance de Zona 1 y recompensas (2026-09-30)

- **Bug grave, encontrado probando el raid**: quien arrancaba un `/raid` quedaba registrado como
  "ocupado" en el índice jugador→raid pero NO figuraba en el roster del lobby. Resultado: "Unirse" le
  contestaba "Ya estás en medio de un combate", "Empezar ya" decía que no había nadie (aun con
  `Raid__MinParticipants=1`), y al vencer el lobby `RaidSessionService.Remove` liberaba solo a
  `session.Participants` — o sea, a nadie — así que **quedaba bloqueado para `/hunt`, `/travel`,
  `/autohunt` hasta reiniciar el bot**. Arreglo en tres capas: (1) `BuildSessionAsync` anota a quien
  arranca desde el primer momento; (2) `Remove` libera por el índice, no por la lista (la clase de bug
  entera, no solo este caso); (3) `TryActivateAsync` cierra el raid si explota a mitad (antes quedaba en
  `Activating` para siempre con todos registrados). Verificado con un arnés contra el `.dll` real: **5
  checks fallaban sin el fix, 7/7 pasan con él**, y el arnés de concurrencia del raid sigue en verde
  (3000 raids × 12 hilos, 0 dobles; 2000 activaciones simultáneas, 0 fallos).
- **⚠️ Sigue abierto (no lo toqué)**: un raid `Active` **no tiene timeout por inactividad**. Si todos
  abandonan sin clickear "Huir" (cierran Discord a mitad de pelea), quedan registrados hasta reiniciar
  el bot — mismo síntoma que el bug de arriba. El combate solitario sí tiene 30 s por turno. Habría que
  sumar un timeout que se reinicie en cada click y persista el HP una vez por participante, bajo el
  mismo lock (regla 2 de la sección del raid en `CLAUDE.md`). (El otro pendiente que anoté acá, que el
  HP del jefe no escalaba con los participantes, ya está resuelto: ver la sección de abajo.)
- **Rebalance de Zona 1** (medido con el `CombatTurnResolver` real, promedio de las 4 clases, sin
  consumibles, arrancando con la vida llena): los bichos de `/hunt` pasan a tener HP ×2.0 y daño ×1.3 sobre los originales.
  Antes un nivel 1 sin arma tardaba 3.5 turnos y perdía 16% de vida; con un arma +5 a nivel 3, el 90%
  de las peleas eran de 1-2 clicks. Ahora: nivel 1 sin arma **6.5 turnos, ~48% de vida perdida, 4% de
  derrota** (0.3% usando habilidad); nivel 3 con arma +5 **3.3 turnos** (16% de 1-2 clicks); nivel 5
  sin arma 3.9 turnos. Un arma +15 (195 de oro) sigue aplastándolos (82% en ≤2 turnos): es el equipo
  funcionando, y a ese punto ya toca la zona 2. Los valores viven en `seed_zones_and_monsters.sql`.
- **Rey Jabalí** (jefe de zona 1): HP ×1.4 y daño ×1.15 (100-140 / 20-32 → 140-196 / 23-37). Nivel 6
  sin arma: antes 5.8 turnos, 44% de vida y 0.1% de derrota; ahora 7.7 turnos, 75% de vida y ~18% de
  derrota **solo atacando** (con habilidad: 53% de vida, ~2% de derrota). Con consumibles a mitad de
  pelea (que el simulador no usa) se gana bien. Los jefes de zona 2 y 3 **no** cambiaron de dureza.
- **Recompensas**: `/travel` ~2.5× de oro y ~3× de XP (a nivel 5: ~150 oro / ~155 XP contra ~65 / ~52) y
  50% de drop en vez de 40% — antes rendía *menos* XP por minuto que farmear `/hunt`, con 10× el
  cooldown. Los tres jefes existentes pagan ×3 de bonus oro/XP (~9-14% de un nivel por jefe, antes
  ~3-6%); el raid cobra lo mismo por participante. Constantes en `CombatRewardCalculator.cs` y `seed_zone_bosses.sql`.
- **Zona 2 no se tocó**, pero la brecha con la zona 1 nueva se achicó (a nivel 5 sin arma: zona 2 dura
  4.1 turnos y saca 21% de vida; zona 1 ahora 3.9 turnos y 9%). Sigue siendo más dura, sobre todo en
  daño, pero conviene una pasada de rebalance de las zonas 2-5 aparte.
- **Base real**: `Database/rebalance_zone1_and_bosses.sql` **ya corrido** (2026-09-30, 9 filas). Las
  instalaciones nuevas ya traen los valores en los seeds; no hace falta correr nada más.

## Habilidades activas por clase (2026-09-30)

- **Qué hay**: un botón de habilidad propio por clase en la pelea manual (`/hunt`, `/travel`, `/boss`,
  y "Habilidad" genérico en `/raid`), cooldown de **3 turnos** para todas. 🛡️ **Aguante** (Guerrero):
  ataca al 70% y recibe −60% de daño 2 turnos · 🔥 **Bola de Fuego** (Hechicero): golpe de 220% ·
  🌑 **Sombra** (Ninja): no ataca, 2 turnos con 75% de esquive y la próxima emboscada es crítico
  garantizado +25% · 🏹 **Lluvia de Flechas** (Arquero): 3 flechas al 60% con crítico individual.
  Se ve en `/profile` (campo "Habilidad") y en la línea "✨ Habilidad" de cada pelea.
- **`/autohunt` NO usa habilidades**, a propósito (decisión tuya: es para farmear bichos débiles AFK).
- **Balance medido** (simulación, habilidad apenas está lista vs. solo atacar): Hechicero daño ×1.30,
  Arquero ×1.20, Ninja ×1.07 (recibe ×0.66), Guerrero ×0.92 (recibe ×0.70). Los jefes ya estaban
  afinados para ataque básico: quedan ~10–30% más fáciles con habilidades. Si hay que retocar, es una
  constante en `AbilityTuning` (`GameData/ClassAbilities.cs`), no stats de jefes.
- **Refactor grande de paso**: la lógica de turno (golpe → Sifón → contraataque) estaba copiada en
  `AdventureModule`, `AutoHuntModule`, `UseModule` y `RaidModule`; ahora es una sola función pura,
  `GameData/CombatTurnResolver.cs`. Ver `CLAUDE.md`.
- **Verificado con arnés** (reflection/referencia directa al `.dll`, no a ojo): cooldown exacto
  (indisponible 3 turnos, lista al 4º), efectos de duración exacta, multiplicadores de daño de cada
  habilidad, esquive 75/75/20% de Sombra, **ataque básico equivalente a la lógica vieja en las 4
  clases**, fuzz de invariantes (0 violaciones) y el arnés de concurrencia del raid con habilidades
  mezcladas al azar (3000 raids, 0 fallos).
- **NO probado en Discord**: el look de los botones (nombre + emoji + "(N)" de cooldown, verde) y los
  textos del log. Se prueba con cualquier `/hunt`; el raid solo con `Raid__MinParticipants=1`.
- **Próximo (anotado)**: explicar cada clase antes de elegirla y bloquear el cambio de clase hasta
  resetear la run (futuro sistema de runs).

## Jefes de Zona cooperativos — `/raid` (2026-09-29)

- **Qué hace**: `/raid` (y `aa raid`) abre un lobby de 60s para 2 a 6 jugadores contra el jefe de tu
  zona actual. Cada jugador ataca a su propio ritmo (sin rondas sincronizadas) contra un HP de jefe
  compartido; el jefe contraataca solo a quien le pegó. Al ganar, **cada participante que pegó al
  menos un golpe y no huyó cobra la recompensa COMPLETA** (no se reparte) y sube su
  `highest_zone_cleared`. Mismo gate de nivel (nivel de la próxima zona) y mismo cooldown de 30 min
  que `/boss`, por jugador, reclamado recién cuando el raid arranca (un lobby cancelado no gasta
  cooldown de nadie). **Sin cambios de esquema ni migraciones nuevas** — todo vive en memoria.
- **Verificado con un arnés de concurrencia** (reflection contra el `.dll` real, 3000 raids × 12
  hilos): encontró un bug propio antes de shippear — el raid se marcaba `Resolved` *después* de
  soltar el lock, así que clicks simultáneos disparaban victorias múltiples (hasta ×7 recompensas).
  Corregido moviendo la transición de fase adentro del lock; también se cerró la carrera entre
  "Empezar ya" y el timeout del lobby (fase transitoria `Activating`). Ambas pruebas fallan sin el
  fix y pasan con él.
- **NO probado en Discord de verdad**: no hay forma de simular varias cuentas de usuario desde acá.
  Lo que sí falta probar a mano con 2+ cuentas: el flujo de botones (Unirse → Empezar ya → Atacar →
  Huir), que el mensaje compartido se edita bien con clicks de gente distinta, y el caso de texto
  (`aa raid`). Riesgo conocido y aceptado: entre que `aa raid` manda el mensaje y registra la sesión
  hay una ventana de milisegundos donde un click devuelve "ese raid ya no existe" (mismo patrón que
  `aa hunt`).
- **Decisiones de diseño que tomé sin preguntar** (fáciles de cambiar): tope de 6 jugadores; huir
  renuncia a la recompensa; el que se une no necesita estar en la misma zona que el jefe (sí cumplir
  el gate de nivel); un jugador derribado antes de que caiga el jefe igual cobra si ya había pegado.
- **Refactor de paso**: `GameData/ZoneRanking.cs` y `GameData/PlayerCombatProfileCalculator.cs`
  sacan lógica que estaba duplicada entre `/boss`, `/zona` y el raid.

## Jefes de Zona + fix de exploit económico (2026-09-08, cerrado 2026-09-29)

- ✅ **Ya corrido contra la base real** (verificado en vivo el 2026-09-29): `Database/seed_zone_bosses.sql`
  (los 3 jefes están cargados y correctamente mapeados a su zona por nombre) y
  `Database/fix_hacha_hierro_mk3_price.sql` (sell_price/buy_price quedaron en 60/78, seguros contra
  el gold_cost de 150 de su receta). No queda nada pendiente de esta tanda.
- **`/boss`** (y `aa boss`) enfrenta al jefe de la zona actual (Rey Jabalí en Zona 1, Lobisón Alfa
  en Zona 2, Capataz de Hierro en Zona 3 — Zona 4 y 5 todavía no tienen jefe). Cooldown de 30 min,
  separado del de `/hunt`. Nunca sale al azar en un `/hunt` normal.
- **`/zona` ahora exige haber derrotado al jefe anterior** para avanzar más de un escalón
  (`users.highest_zone_cleared`). El gate se desactiva solo para zonas sin jefe cargado (4 y 5 por
  ahora), así nadie queda trabado permanentemente por contenido que todavía no existe — cuando
  cargues jefes ahí, el gate se activa automáticamente sin tocar código.
- **Exploit real encontrado y arreglado**: "Hacha de Hierro MK3" costaba 150 de oro forjarla y
  vendía por 500 — +350 de oro garantizado por ciclo, sin cooldown en `/forge make`, con
  materiales comunes. Bajada a sell 60 / buy 78. Causa: dos escalas de precio (armas base sin
  receta vs. equipo de clase) conviviendo sin coordinarse — si agregás una receta nueva a un ítem
  que ya tenía precio fijado en la escala vieja (20/150/500), volvé a chequear este invariante:
  `sell_price ≤ 0.5 × gold_cost` de su receta.
- **Auditoría de `/shop`/`/forge`**: la parte transaccional está sólida (guarded UPDATE + `FOR
  UPDATE`, no hay forma de vender lo que no tenés ni de forjar sin materiales). El punto flojo es
  que `ShopModule.ExecuteSellAsync` no restringe por `type` — cualquier ítem con receta nueva
  necesita el chequeo manual de arriba, no hay nada automático que lo fuerce.

## Sistema de Zonas (2026-09-07)

- **Tablas nuevas**: `zones` (id, name, description, min_level, emoji) y `monsters`/`monster_drops`
  (`Database/schema.sql` para instalaciones nuevas, `Database/add_zones_and_monsters.sql` para tu
  base actual). `users.current_zone_id` (default 1, FK a `zones`) define en qué zona caza `/hunt`.
- **Los monstruos de /hunt se mudaron de código a base**: `GameData/MonsterCatalog.HuntMonsters` se
  borró — ahora viven en `monsters`/`monster_drops` (`Repositories/IMonsterRepository.cs`), mismo
  patrón que ya se usó para migrar las recetas (`CraftingCatalog.cs` → `RecipeRepository`).
  `/hunt` (y `aa hunt`/`aa autohunt`) resuelve su pool por la zona ACTUAL del jugador vía
  `IAdventureCombatStarter.PrepareHuntAsync`. **`/travel` NO se tocó a propósito** — sigue con su
  pool fijo en código (`MonsterCatalog.TravelMonsters`), decisión explícita para no chocar con el
  comando ya existente (ver más abajo).
- **`Database/seed_zones_and_monsters.sql`**: carga las 5 zonas (Praderas del Mate Nv.1, Bosque de
  Cenizas Nv.5, Minas del Yunque Nv.10, Cordillera del Fuego Nv.15, Cráter de la Escoria Nv.20) +
  14 monstruos (los 4 que ya existían en código, migrados a Zona 1 con los mismos stats/drops de
  siempre, + 10 nuevos — 2 por zona, con 20 materiales nuevos tipo `Material`). Los monstruos de
  Zona 5 pegan 140-220 de daño, capaz de matar de un golpe a un jugador de nivel bajo — es la zona
  de riesgo real, pensada para equipo top de `/forge`. Totalmente re-ejecutable (`ON CONFLICT`).
- **`/zona [id]`** (y `aa zona <id>`) cambia de zona validando `min_level`; **`/zonas`** (y
  `aa zonas`) lista todas con nivel requerido y marca dónde estás parado. Nombrado `/zona` (no
  `/travel`) porque `/travel` YA es un comando de combate existente y muy distinto (viaje difícil,
  cooldown de 10 min) — reusar el nombre iba a chocar, así que se armó un comando nuevo en vez de
  reinterpretarlo.
- **Recompensa de `/hunt` ahora tiene un componente por zona**: `monsters.gold_reward`/`xp_reward`
  es un bonus FIJO que se SUMA a la fórmula de siempre (`CombatRewardCalculator.RollHuntReward`),
  no la reemplaza — los monstruos de Zona 1 quedaron en bonus 0/0 a propósito (cero cambio de
  balance para el contenido que ya existía), y el bonus escala en zonas 2 a 5 (+15/+12 hasta
  +130/+100 de oro/XP) para que viajar a una zona más difícil realmente convenga.
- **Pendiente de tu lado**: correr `Database/add_zones_and_monsters.sql` y después
  `Database/seed_zones_and_monsters.sql` contra tu base real (este segundo necesita que
  `seed_class_gear_and_monster_drops.sql` ya haya corrido antes, porque los 4 monstruos migrados
  referencian sus drops por nombre). Sin esto, cualquier `/hunt` real fallará (la tabla no existe).

## Pendiente de acción tuya (2026-09-03, tarde)

- **Correr `Database/finalize_consumable_catalog.sql`.** El pase de arte de Consumibles definió
  una escala sin Legendario (Común x2, Raro x1 — solo la Empanada —, Épico x4 — Choripán y Cordero
  Patagónico subieron/bajaron de rareza —, Mítico x2). "Matambre Arrollado" quedó sin escalón y se
  saca del catálogo. Los precios de Choripán/Cordero NO se tocaron (quedaron con los de su rareza
  vieja) — avisame si querés que también seas rebalanceen a la progresión de Épico.
- **Correr `Database/update_item_emojis.sql`.** Reemplaza a `add_item_emoji.sql` +
  `update_item_emojis_batch1_materiales.sql` + `update_item_emojis_batch2_minerales.sql` (los tres
  se borraron) — a pedido, de acá en más los emojis van TODOS en este único archivo, que se sigue
  editando en el lugar cada vez que hay códigos nuevos en vez de crear un `batchN` por tanda. Ya
  trae los 11 de Madera+Mineral (completo) y agrega la columna `emoji` sola si esta base todavía
  no la tiene (pasó: no todas las máquinas corrieron la migración vieja).
- **Correr `Database/dedupe_items_and_add_unique_name.sql` contra tu base real.** `items` nunca
  tuvo `UNIQUE(name)`, así que con el tiempo se duplicaron 3 nombres (`Espada de Madera`,
  `Hacha de Hierro MK3`, `Hoja de Acero Puro` — estos dos últimos con los nombres CRUZADOS entre
  sus dos copias) y quedaron 5 ítems huérfanos de una era anterior del proyecto
  (`Cuero de Jabalí`, `Colmillo de Lobo`, `Núcleo de Golem`, `Medalla del Náutico`,
  `Sello de Forja MK3`) sin receta ni drop que los referencie hoy. El script los limpia (con los
  `item_id` reales, confirmados por vos) y agrega la constraint. `Database/schema.sql` ya la tiene
  para instalaciones nuevas.
- Antes de esto, corría un riesgo real y no solo estético: `ItemRepository.GetByNameAsync` usa
  `LIMIT 1` sin `ORDER BY`, así que un nombre duplicado podía hacer que `/equip`, `/shop buy` o
  `/forge make` resolvieran a cualquiera de las copias de forma inconsistente entre ejecuciones.
- **Correr `Database/remove_legacy_consumables.sql`** (después de `seed_consumables_and_base_swords.sql`
  si todavía no lo corriste) para sacar los 3 Consumibles viejos sin trackear (`Mate (Canarias
  Suave)`, `Refuerzo de Milanesa`, `Tira de Asado`) que dispararon el reporte original de precios rotos.

## Rework de /heal + emojis de ítems + rebalance de Consumibles (2026-09-03)

- **`items.emoji`** (columna nueva, ahora dentro de `Database/update_item_emojis.sql` — ver arriba):
  emoji personalizado de Discord por ítem ("<:nombre:id>"), mostrado en todo lugar que muestre nombres de ítems (`/profile`,
  `/inventory`, `/shop`, `/forge recipes`/`make`, `/equip`, `/use`, drops de `/hunt`/`/travel`/
  `/autohunt`/`/chop`/`/mine`) vía `GameData/ItemDisplay.Format`. Los códigos se cargan por nombre
  (no por `item_id`, que puede variar entre instalaciones) en `Database/update_item_emojis.sql`
  (Madera+Mineral completo, 11/58 — el resto sigue en `NULL`, cae a mostrar solo el nombre).
- **`/heal` reworkeado:** ya no gasta oro directo. Ahora consume automáticamente el Consumable más
  barato que el jugador tenga en inventario (tiene que haberlo comprado antes en `/shop buy`); si
  no tiene ninguno, te manda a la tienda. `IUserRepository.HealAsync` (el viejo, a oro) se borró —
  quedó reemplazado por `RestoreHpAsync`, compartido con `/use`. Sigue bloqueado en combate, igual
  que antes (para eso está `/use`, que sí funciona en pelea).
- **`/shop buy` bloqueado en combate:** no existía ese chequeo — un jugador podía comprar
  consumibles en pleno `/hunt`/`/travel`. Ahora usa el mismo `ICombatSessionService.Peek` que
  `/heal`. `/shop sell`/`sellall` siguen sin bloquear (vender no da ninguna ventaja en combate).
- **Rebalance de precios de Consumibles** (`Database/rebalance_consumables.sql`): la carga inicial
  escalaba el heal linealmente con la rareza pero el precio más rápido, así que la eficiencia
  (HP por oro) caía de ~3.75 (Común) a ~0.31 (Mítico) — nunca convenía comprar nada más caro que
  el ítem Común más barato. Ajustado a ~4.4-5.0 HP/oro parejo en toda la escala.
  `seed_consumables_and_base_swords.sql` ya quedó con los números nuevos (una instalación nueva
  no necesita correr el rebalance).

## Nota de sincronización entre máquinas (2026-09-03)

Al traer estos cambios a otra PC, la base de esa máquina estaba en el estado de la primerísima
instalación (solo `schema.sql`/`seed.sql`/`add_hp_columns.sql`/`fix_material_types.sql`
aplicados) — mucho más atrás de lo que este archivo asumía. Se recreó desde cero con el
`schema.sql` actual (ya autosuficiente) + los 4 seeds de abajo, y además se corrió
**`Database/seed_consumables_and_base_swords.sql`** (nuevo): completa la familia Espadas
(le faltaban Común/Raro/Épico — "Hacha de Hierro MK3" incluido, cuya receta en
`seed_recipes.sql` no insertaba nada por esto) y crea el catálogo de Consumibles completo
(`type = 'Consumable'`), que no existía ninguno y por eso `/shop view`/`/shop buy`/`/use`
no tenían nada para mostrar. Si vas a instalar en una tercera máquina, corré ese script
también, después de `seed_recipes.sql`.

## Pendiente de acción tuya

- **Correr contra la base real, EN ESTE ORDEN** (ninguno es idempotente salvo que se aclare lo contrario; correrlos dos veces duplica filas):
  1. `Database/add_class_requirement.sql`
  2. `Database/add_recipes_tables.sql`
  3. `Database/seed_class_gear_and_monster_drops.sql`
  4. `Database/seed_recipes.sql` (este sí es re-ejecutable: usa upsert tanto para la receta como para sus ingredientes)
- **`Hacha de Hierro MK3`:** su receta nueva la referencia por nombre (no la vuelve a crear como ítem) — asume que ya existe en tu base, porque `add_weapon_family.sql` ya la daba por existente antes. Si en tu base real no existe con ese nombre exacto, el bloque de `seed_recipes.sql` para esa receta no inserta nada (0 filas, sin error) — avisame y la creo como ítem nuevo.

## Profundidad del Core Loop: loot separado + equipo por clase + recetas en la base (2026-09-03)

- **Drops mezclados, arreglado de raíz:** el drop de `/hunt`/`/travel` usaba `GetRandomByRarityAsync(rarity)`, sin filtrar por tipo — podía entregar madera, piedra, un arma o un amuleto como "botín de combate". Se eliminó ese método; ahora `/hunt` tiene un pool de drops **fijo por monstruo** (`MonsterTemplate.DropItemNames`) y `/travel` usa `GetRandomByTypeAndRarityAsync("Material", rareza)` — ninguno de los dos puede tocar `Madera`/`Mineral` (exclusivos de `/chop`/`/mine`) ni Weapon/Amulet/Consumable. Ver `Modules/AdventureModule.ResolveDroppedItemAsync`.
- **Roster de `/hunt` renovado:** Jabalí Rabioso, Lobisón de las Cenizas, Gólem de Escoria, Cuatrero No-Muerto (reemplazan a los 5 genéricos anteriores), cada uno con sus 2 drops propios. `/travel` no se tocó (mismos 5 monstruos), solo se corrigió que ya no dropee cualquier cosa.
- **`class_requirement` en `items`:** nueva columna nullable (NULL = cualquier clase). Los 16 ítems de equipo por clase (2 armas + 2 amuletos × 4 clases) solo se pueden **equipar** (`EquipModule`) y **forjar** (`ForgeModule.ExecuteMakeAsync`) por la clase dueña — antes la columna hubiera sido puramente decorativa si no se aplicaba en los dos lugares.
- **Recetas migradas de código a base de datos**, a pedido explícito: tablas nuevas `recipes` (result_item_id UNIQUE + gold_cost) y `recipe_ingredients` (N ingredientes por receta). `GameData/CraftingCatalog.cs` se borró — `Repositories/RecipeRepository.cs` (`IRecipeRepository`) reemplaza esa lógica leyendo de la base. `CraftingRepository.CraftAsync` (la transacción que realmente descuenta oro/ingredientes) no necesitó ningún cambio: ya recibía los ingredientes resueltos como parámetros, sin importarle de dónde salía la receta.
- **`/forge recipes` sigue filtrando por clase:** las recetas de tu clase se muestran primero, las genéricas (`class_requirement` NULL) después, las de otras clases se ocultan.
- **Sustituciones en el seed:** "Oro" del pedido → `Oro Puro` (ítem Mineral que ya existía, para no duplicar catálogo); agregué `Carbón` (Mineral) porque lo pedían varias recetas y no existía en ningún seed trackeado.
- **Recuperé las 2 recetas genéricas** que habían quedado sin dueño al borrar `CraftingCatalog.cs` (`Hacha de Hierro MK3`, `Amuleto del Levantador`) — sin `class_requirement`, costo/cantidades más bajos que las 16 de clase (son la entrada, no el tope).

### Validación de balance (análisis, no simulación jugada)

Repasé probabilidades y tiempos esperados a mano — no hay forma de "jugarlo" de verdad sin un bot corriendo contra Discord, así que esto es cálculo de valor esperado, no una partida real:

- **Bug real encontrado y arreglado:** había puesto `Carbón` en rareza Raro, la misma que `Hierro`. Como `RollGatheringRarity()` primero sortea la rareza y recién después un ítem al azar *dentro* de esa rareza, tener 2 ítems Raro partía la probabilidad de Hierro a la mitad (25% → 12.5% por `/mine`) — duplicaba el tiempo de farmeo de las 6 recetas que ya pedían Hierro x5. Lo pasé a Común (comparte con Piedra, que casi no se usa en recetas), restaurando el 25% original.
- **Oro:** no es el cuello de botella. `/daily` solo (100 oro día 1, hasta 1000/día con racha completa) cubre cualquier receta (150-350 oro) en pocos días; `/hunt`/`/travel` suman más arriba de eso.
- **Materiales de recolección** (post-fix): conseguir Hierro x5 para una receta ronda ~20 intentos de `/mine` esperados (~100 min de cooldowns, no de juego activo). Es un farmeo real pero no descabellado para equipo tope de gama.
- **Drops de monstruo:** conseguir 3x de un drop específico de `/hunt` (ej. Núcleo Ígneo del Gólem) ronda ~90 intentos esperados (~90 min de cooldowns) — `/travel` suma una fuente extra en paralelo (mismo pool de `type = 'Material'`, cooldown independiente), así que en la práctica es menos que eso jugando ambos comandos.
- **Nada "imposible":** con oro sobrando y materiales en el orden de 1-3 horas de cooldowns acumulados por receta (no de un tirón, se puede espaciar en varios días), ninguna de las 18 recetas actuales requiere una cantidad de recursos inalcanzable.
- **Dato para tener en cuenta, no un bug:** el equipo de clase (stat 45-55, con sinergia ×1.5 si coincide la familia) es un salto de poder grande comparado con las armas base de la tienda (stat 5-35) — un Guerrero con Mazo de Escoria pasa de Ataque ~10 a ~78 y empieza a matar de un golpe a los bichos de `/hunt`. Es coherente con que sea equipo "de esfuerzo" (varias horas de farmeo), pero como todavía no hay Zonas de Dificultad, hoy no hay nada que lo desafíe después de conseguirlo. Cuando armemos zonas, este sería el punto de referencia para la siguiente escala de dificultad.
- **No relacionado con esto pero lo vi de paso:** `/shop sell` no filtra por tipo (a diferencia de `/shop buy`), así que un jugador puede vender por error un drop de monstruo o un arma forjada por unas pocas monedas. Es comportamiento preexistente (no lo tocué), lo dejo anotado por si en algún momento conviene un "¿estás seguro?" para ítems raros.

## Arreglado en la revisión de arquitectura (2026-09-03)

| # | Problema | Solución |
|---|----------|----------|
| 1 | **Bug crítico de HP en combate:** mientras un `/hunt`/`/travel` por turnos estaba abierto, el HP en base quedaba congelado; `/heal` (u otra fuente) podía cambiarlo mientras tanto, y al resolver el combate (huida, derrota o timeout) se pisaba con un snapshot en memoria desactualizado — plata gastada en curarse sin ningún efecto. | `CombatState.PlayerStartingHp` + `IUserRepository.ApplyCombatHpDeltaAsync` (delta transaccional con `FOR UPDATE`, ver `Services/ICombatSessionService.cs`, `Modules/AdventureModule.cs`, `Modules/AutoHuntModule.cs`, `Services/CombatSessionService.cs`). `IAdventureRepository.ApplyVictoryAsync` ahora recibe `hpDelta` en vez de un HP absoluto. |
| 2 | Por decisión de diseño, ya no se puede gastar oro en pleno combate. | `/heal` queda bloqueado mientras haya un combate activo (`TavernModule`); se agregó `/use` (+ `aa use`/`aa u`) para curarse con un consumible del inventario sin costo de oro, funciona dentro y fuera de combate (`Modules/UseModule.cs`, `IInventoryRepository.TryConsumeAsync`). Usar un ítem en combate cede el turno (el monstruo también golpea), para que no sea curación gratis ilimitada. |
| 3 | El cálculo de XP/nivelado (`SELECT FOR UPDATE` → `LevelingCalculator.ApplyXpGain` → `UPDATE`) estaba duplicado en `UserRepository.AddXpAsync`, `UserRepository.ClaimDailyAsync` y `AdventureRepository.ApplyVictoryAsync`. | Extraído a `Repositories/TransactionalHelpers.cs` → `LevelingApplier.ApplyAsync`, compartido por los tres. |
| 4 | `Modules/EconomyModule.cs` contenía `partial class ShopModule`, no una `EconomyModule` — nombre de archivo engañoso. | Renombrado a `Modules/ShopModule.View.cs` (`git mv`, preserva historial). |
| 5 | `TextCommandModule.cs` ya tenía ~450 líneas y crecía 1:1 con cada comando nuevo. | Dividido en partial classes por dominio: `TextCommandModule.cs` (core/perfil), `.Combat.cs`, `.Gathering.cs`, `.Economy.cs`. |
| 6 | `UserRepository` absorbía responsabilidades que no son "CRUD de usuario" (XP, nivelado, racha diaria). | `AddXpAsync`/`ClaimDailyAsync` se movieron a `Repositories/ProgressionRepository.cs` (`IProgressionRepository`). `UserRepository` conserva solo lo relacionado a la fila de usuario en sí (equipo, oro, HP). |

Todo esto ya compila y bootea limpio contra Discord (`dotnet build` + smoke test).

## Resumen

El bot tiene una base sólida: `/class`, `/profile`, `/hunt`, `/travel`, `/chop`, `/mine`,
`/cd`, `/heal`, `/inventory`, `/shop` (view/buy/sell/sellall), `/forge` (recipes/make) y
`/equip` funcionando de punta a punta contra PostgreSQL con Dapper. Todas las operaciones que
tocan oro, inventario o cooldowns usan transacciones reales con upserts guardados o
`SELECT ... FOR UPDATE`, así que no debería haber forma de duplicar recompensas por doble click
o ejecuciones concurrentes — es la parte que más cuidamos y la que menos me preocupa.

Los dos riesgos reales que veo hoy:

1. **Editar archivos a mano por fuera del chat rompe el build sin que nadie se entere hasta la
   próxima vez que se corre `dotnet build`.** Pasó hoy: `ICombatService.SimulateHunt` se le agregó
   un parámetro pero `CombatService`/`AdventureModule` no se actualizaron, y el repo quedó sin
   compilar. Lo arreglé (ver abajo), pero si vas a seguir tocando código directo, avisame para
   que lo built-e/pruebe enseguida — cuesta 10 segundos y evita que se acumulen dos o tres roturas
   juntas.
2. **La base de datos real ya no coincide 100% con lo que hay en git.** `weapon_id`/`amulet_id`
   y buena parte del catálogo de ítems (Consumibles, Armas, Amuletos, MonsterDrops) se cargaron a
   mano en tu Postgres local y nunca quedaron en un script versionado. Si esa base se pierde o
   hay que levantar el bot en otra máquina, se pierde ese contenido. Antes conviví con esto
   parcheando `schema.sql`/migraciones puntuales; para el catálogo completo lo mejor es que me
   pases un `pg_dump --data-only -t items` (o me copies las filas) y armo un `seed_full.sql` real.

Fuera de eso, el diseño modular (Módulos → Servicios/GameData → Repositorios) se mantuvo
consistente en las ~8 tandas de features que armamos, y eso ayudó mucho a que agregar `/forge`
o separar `/shop` no rompiera nada existente.

## Arreglado en esta revisión

| # | Problema | Dónde |
|---|----------|-------|
| 1 | El repo no compilaba: `SimulateHunt` pedía `(nivel, dañoArma)` pero `AdventureModule` lo llamaba como `Func<int, CombatResult>` | `Services/ICombatService.cs`, `Services/CombatService.cs`, `Modules/AdventureModule.cs` |
| 2 | El daño del arma equipada nunca se calculaba (parámetro agregado pero sin valor real) | `Modules/AdventureModule.cs` — ahora resuelve `player.WeaponId` → `StatValue` antes de simular combate, en `/hunt` y `/travel` |
| 3 | El drop de `/hunt` guardaba un **nombre de ítem** (`"Cuero de Jabalí"`) donde se esperaba una **rareza**, así que nunca matcheaba nada | `Services/CombatService.cs` — ahora sortea rareza con `RarityCatalog.RollTravelRarity()`, igual que `/travel` |
| 4 | `schema.sql` no tenía `weapon_id`/`amulet_id` — una instalación nueva desde cero se rompería en `/profile`, `/equip`, etc. | `Database/schema.sql` |
| 5 | Búsqueda de ítems por nombre usaba `ILIKE` con el texto del usuario tal cual: escribir `%` o `_` se interpretaba como comodín de SQL | `Repositories/ItemRepository.cs` — ahora `LOWER(name) = LOWER(@Name)` |
| 6 | El evento `Ready` de Discord.Net dispara en cada reconexión (no solo al loguear), y volvía a registrar los módulos cada vez, lo cual tira excepción sin manejar | `Program.cs` — guardado con un flag `_modulesRegistered` |
| 7 | Si la excepción de un comando ocurría antes de `Defer/Respond`, el manejo de errores fallaba al intentar consultar una respuesta que nunca existió, y esa segunda excepción quedaba sin capturar | `Program.cs` |

Todo esto ya compila y bootea limpio contra Discord.

## Brecha vs. el diseño original (para decidir, no toqué nada acá)

- ✅ **Resuelto (parcial, por decisión consciente):** en vez de restringir qué arma puede
  equiparse cada clase, agregamos `items.weapon_family` (Espadas/Dagas/Arcos/Grimorios) y
  `GameData/ClassWeaponSynergy.cs`: cualquier clase puede equipar cualquier arma, pero el daño
  del arma se multiplica x1.5 solo si la familia coincide con la de la clase (Guerrero+Espada,
  etc.). Falta cargar armas de las otras 3 familias — hoy solo hay `Espadas` en el catálogo, así
  que la sinergia solo es alcanzable jugando Guerrero.
- 🟡 **El herrero solo craftea ítems nuevos fijos, no mejora los que ya tenés.** El brief original
  hablaba de "forjar armas de mayor nivel" — hoy `/forge make` es siempre "pagá X, recibís un
  ítem predefinido", no hay upgrade de un arma existente.
- 🟡 **El amuleto no hace nada mecánicamente.** Se equipa, se muestra en `/profile`, pero a
  diferencia del arma (que ahora sí suma a `PlayerPower`), su `stat_value` no se usa en ningún
  cálculo todavía.

## Deuda técnica

- ✅ **Resuelto:** `current_weapon_id` se eliminó del modelo, `UserSql` y `schema.sql`. Corré
  `Database/cleanup_weapon_columns.sql` una vez contra tu base para sacarla ahí también y
  agregarle Foreign Key a `weapon_id`/`amulet_id`.
- 🟡 No hay tracking de qué migraciones SQL ya se corrieron (`schema.sql`, `seed.sql`,
  `add_hp_columns.sql`, `fix_material_types.sql`, `add_buy_price.sql`, `add_daily_columns.sql`,
  `cleanup_weapon_columns.sql`, `add_weapon_family.sql`...). Si el bot se instala en otra
  máquina, hay que acordarse de correrlas todas en orden a mano. Una tabla `schema_migrations`
  simple (o una herramienta tipo DbUp) resolvería esto sin mucho esfuerzo — cada vez que sumamos
  una feature con cambio de esquema, esta lista crece.
- 🟢 Cero tests. `LevelingCalculator`, `RarityCatalog` y `CombatService` son lógica pura (sin DB)
  y son los candidatos más baratos para empezar — hoy el único chequeo es "compila y bootea".
- 🟢 Logging es `Console.WriteLine` sin niveles ni persistencia. Alcanza para desarrollo; para un
  bot corriendo 24/7 en un server, conviene algo que deje rastro cuando vos no estás mirando la
  consola.

## Mejoras de UX / comandos

- 🟡 `/shop buy`, `/shop sell`, `/forge make`, `/equip` piden el nombre del ítem como texto libre
  → cualquier typo tira error. Discord.Net soporta autocomplete real (sugiere mientras escribís);
  sería la mejora de UX con más impacto por esfuerzo invertido.
- 🟢 No hay `/help` que liste los comandos disponibles agrupados por categoría.
- 🟢 No hay regeneración pasiva de HP (solo `/heal` pago). Es una decisión de diseño válida, pero
  vale confirmarla a propósito en vez de que sea un efecto colateral de no haberlo construido.

## Ideas a futuro (sin comprometerme a nada, para cuando quieran expandir)

**Lista del dueño (2026-10-05)** — con lo que se charló y lo que hay que cuidar. **HECHO en la v0.9.0: banco, penalidad por muerte, desmantelar (→ Polvo), encantamientos y logros nuevos con páginas** (ver la sección de arriba; lo de "craftear" se descartó). Quedan **relación y mascotas**. El texto de abajo es el análisis
original, se deja como registro de los motivos de cada decisión.
- **Logros: muchos más y con páginas.** Fácil y seguro: el progreso sale de `player_stats`, no se guarda nada. Hace falta paginar `/achievements` (botones o menú) y categorías (combate, recolección, economía, PvP, colección...). Cuidar la inflación de premios: más logros = más oro/cajas gratis; conviene que
  los nuevos paguen sobre todo **títulos/insignias** que se muestran en `/profile` (no inflan nada). Mejor hacerlo DESPUÉS de los sistemas nuevos para que haya logros de encantar, desmantelar, mascotas...
- **Desmantelar y craftear (craft 1–10, desmantelar 1–5).** Lo que podría romper no es la cantidad por comando sino la tasa de cambio. Reglas para que no rompa: lo que se desmantela devuelve menos de lo que cuesta (ida y vuelta siempre pierde), los drops de zona NO se pueden fabricar ni recuperar (las recetas
  están calibradas con 3 drops por zona), y nada convierte Carbón en Hierro (lo mismo que prohíbe `TradeRules`). Idea que cierra con encantamientos y banco: desmantelar (excedentes, drops de zonas viejas, trofeos repetidos) da **Polvo** por rareza, que solo sirve para encantar. Pendiente: confirmar con el dueño QUÉ se
  desmantela y QUÉ se craftea (las armas y amuletos se equipan directo, así que "1–5 / 1–10" apunta a cosas apilables).
- **Relación / casamiento**: ya estaba anotado abajo ("Casamiento"). Ideas con valor real y sin romper: intercambio 1 a 1 de drops de cacería de la MISMA zona entre la pareja (una clase usa un drop y la otra el otro, así se aprovecha lo que a cada uno le sobra), bonus chico en raid de a dos, premio del `/daily`
  compartido. Cuidar: cuentas alternativas (casarse con uno mismo), mismo tope que `/give`.
- **Encantamientos con tiers** (arma y amuleto, estilo propio de Asado y Acero: nombres de parrilla/brasa/ceniza, no los del EPIC RPG). Es poder extra, y el escalón entre zonas está medido con simulación: hay que fijar un **tope de poder total** (todo lo que no es arma: encantamiento, mascota, banquete, relación) y volver a medir con el
  simulador (hoy no está en el repo: se rehace, ver CLAUDE.md). Propuesta: tirada con chance por tier, no se pierde lo que ya tenés (solo reemplaza si sale mejor), cuesta oro + Polvo (sumidero), y los tiers altos son raros. Lo más sano es que sea el contenido de DESPUÉS de la Zona 5 / del reset, cuando los monstruos
  de la segunda vuelta se escalen a propósito, y no tocar los monstruos de la primera.
- **Banco**: oro guardado aparte, protegido de la penalidad por muerte. Mejor que "da XP": capacidad que se amplía pagando (sumidero de oro) y, si se quiere, un interés chico y con tope. Un bono de XP por oro guardado mezcla dos recursos y es difícil de calibrar.
- **Muerte con penalidad.** Perder un nivel es lo más fuerte que hay (rompe misiones, `/zona` por nivel mínimo y se siente muy mal). Más sano: perder un % chico de oro (con tope, y lo del banco no se toca) y/o un poco de XP del nivel actual SIN bajar de nivel; no en `/autohunt` ni en Zona 1 ni bajo cierto nivel. Necesita el banco primero.
- **Mascotas**: es lo que más engancha pero también lo más grande (cómo se consiguen, cómo suben, qué dan). Que den cosas chicas (oro/XP o un poco de defensa) y NUNCA toquen las chances de drop (las recetas están calibradas con ellas). Al final de la lista: depende de logros, encantamientos y del diseño del reset.

- **Casamiento (`marriage`)** — pedido por el dueño (2026-10-02), para más adelante: dos jugadores se casan (propuesta con aceptar/rechazar, como `/trade` y `/fight`) y pueden **compartir cosas** entre sí. Preguntas para cuando se encare: qué se comparte (¿oro común?, ¿cofre compartido?, ¿bonus al pelear juntos / en raid?, ¿cooldowns?), cómo se divorcia (y qué pasa con lo compartido), y cuidado con las cuentas alternativas: si se comparte algo con valor, el casamiento abre la misma puerta que `/give` (mismo tope o registro en `game_events`).
- Sistema de armadura/defensa (hoy el daño recibido no depende de ningún stat defensivo).
- Comandos de administrador (dar oro/ítems, resetear cooldowns) para moderar el server de pruebas.
- PvP o eventos temporizados de servidor (boss compartido, etc.) — mencionado como "idle" en el
  brief original, esto sería la primera mecánica no-idle si se agrega.
