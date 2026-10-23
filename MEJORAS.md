# Asado y Acero RPG — Estado y mejoras pendientes

_Última revisión: 2026-10-23 (v0.26.1: el resumen del combate en tablita y `/config` para apagarlo)_

## Pendiente de un vistazo (puesto al día el 2026-10-22)

Este archivo es sobre todo un diario de versiones (lo más nuevo, arriba). Para no tener que recorrerlo entero, esto es lo que de verdad está abierto:

**Esperando una decisión del dueño**
- **La Guarda ya se bajó (v0.25.1, ver abajo)**: el dueño dijo «bajala un poquito si te parece muy OP». Si se la quiere a la par del Filo, son +2/4/6/9/13 % (una línea en `KindTable`).
- **El jefe mundial ya está hecho (v0.24.0, ver abajo)**; lo que queda abierto es si se quiere una línea en `/cd` («anotado ✅ — se pelea en 3h»), un jefe por servidor cuando haya mucha gente, o premios que no sean solo oro.
- **Blackjack: ya está (v0.25.0, ver abajo).** Lo que queda abierto: dividir (split) y el seguro, que no se hicieron a propósito para mantenerlo simple.
- **Habilidades de clase** (pedido de los jugadores, ver «Sugerencias»): aprender, mejorar o evolucionar la habilidad, y «nerfear las iniciales». Necesita diseño antes de tocar nada: están calibradas con `PveTuning`, `PvpTuning` y los jefes, no debe contar en duelos
  ni en la Arena, y habría que preguntar CUÁL clase les parece pasada (en PvE la débil es el Arquero).
- **Idioma:** el primer corte ya está (v0.26.0, ver abajo: motor, `/language` y las pantallas principales en inglés). Lo que falta es traducir el resto pantalla por pantalla (hay una checklist con casillas en esa sección: «Lo que falta en inglés») y que alguien que hable inglés revise el tono de `Locales/en.json`.
- **Comida para las zonas altas** (el dueño avisa cuándo), un cooldown en `/play` y zonas cada ~10 niveles (opinión dada, sin construir).

**Ya hablado, falta hacerlo**
- Escena con desplegables para `/trade` y `/exchange` (como la taberna), para que el emoji se vea también al elegir: las listas de autocompletado no pueden llevarlo.
- Limpiar los emojis unicode de los títulos y de los nombres de campo, pantalla por pantalla (necesita un set propio de íconos de estadística), y apilar los dos HP del combate para poder usar barras de emojis ahí.

**Para mirar en la base viva** (necesitan datos de Railway; los corre el dueño con `psql`)
- `Database/report_live_health.sql`: el logro «Racha» tiene que desbloquear el tramo I (7 días) cuando reclamen los dos jugadores con racha de 8 a 13; si no pasa, es un bug.
- `Database/report_gold_chest_origin.sql` (nuevo): de dónde salen los Cofres de Oro (29 abiertos en 7 días en el informe del 2026-10-20). Reconstruye por tienda, jefe, `/daily`, misiones, logros, Arena y cajas, y cuadra contra lo visto.
- `/vote` con el bot público: hoy duplica los cofres gratis; con 3 votantes no preocupa. Palancas: cobrar el reseteo del jefe una vez por día, o cambiar el cofre.
- Confirmar que `apply_emojis.sql` corrió en Railway: sin eso no se ven los emojis de los ítems (ni los de los desplegables).

**Hoja de ruta, no es para ahora**
- Clase Monje (necesita una 5.ª familia de arma o una regla de «sin arma»; el dueño ya le contestó al foro que es para más adelante).
- **Membresía de pocos pesos** (el dueño, 2026-10-22): lo único que da es **bajar un % de los cooldowns** y **algún título especial**; nada de poder, drops, oro ni XP (el PvP, la Arena y el ranking no pueden verse afectados). Para cobrar con Premium Apps de Discord la app tiene que estar verificada, en un equipo con 2FA y con
  Términos y Privacidad públicos, y hay que confirmar si hay Premium Apps en Uruguay (Developer Portal, Monetization). Punto de enganche: `ReminderPolicy.IsEligible` y `CooldownCatalog`. El título especial entra como categoría «Especiales» del sistema de títulos.
- Casamiento, comandos de administrador, eventos temporizados de servidor, la fusión de mascotas en los Fuegos Nuevos 3 o 5, dos guardianes Míticos en la Zona 0, y que la chance Mítica suba con el Fuego Nuevo.
- Deuda técnica real: no hay registro de qué migraciones corrieron en cada base, y no hay CI (ver «Deuda técnica» más abajo).

## La victoria a mano como la del autohunt, pero con barra de vida (v0.26.2, 2026-10-23)

- **Pedido del dueño** (con dos capturas, la victoria de `/hunt` con tres campos «Oro ganado / EXP ganada / Tu HP» y la del `/autohunt` con su línea «Ganaste: …»): «estaría bueno que se muestre como el autohunt pero con barra de vida».
- **Cómo queda** (`/hunt`, `/travel`, `/boss`): en la descripción, el golpe final y debajo «Ganaste: <ícono de oro> **N** Oro, <ícono de XP> **N** XP.» (la misma línea del autohunt, ahora de `AdventureModule.EarnedLine`), después la frase del monstruo; ya no existen los campos «Oro ganado» y «EXP ganada». La vida sigue siendo su campo «❤️ Tu HP» con la barra, pero **a todo el ancho** (antes era una de las tres columnas) y **con la barra de ÍCONOS** (8 piezas, como la del perfil; el dueño: «como el autohunt, con los íconos, incluida la barra de vida»), que antes era la de texto porque el campo era una columna angosta; sin las piezas cargadas vuelve la de texto. La tablita del resumen arranca en una fila nueva debajo. El drop, el huevo y lo del jefe quedan como estaban.
- **El `/autohunt` no se tocó**: sigue con «Te quedan a/b HP» en texto y sin barra (se le puede sumar la barra si el dueño la quiere).
- **Sin migración**; una entrada del catálogo en inglés cambió de plantilla (la línea ya no termina en `\n\n`, lo agrega quien la usa). El arnés `stage11test` leía los campos «Oro»/«EXP» de la victoria: ahora mira la descripción. `loctest` suma los chequeos de la victoria (línea con íconos en los dos idiomas, vida a todo el ancho, tablita debajo).

## El resumen del combate en tablita y `/config` (v0.26.1, 2026-10-23)

- **Pedido del dueño** (mirando el resultado de `/autohunt` en inglés): que el oro y la XP lleven su emoji; que el resumen quede «más chill», en tablita y con un espacio chico; y que cada jugador pueda sacarlo («una opción en config o `aa set`, en la UI»).
- **Íconos**: el resultado del `/autohunt` ahora dice «Ganaste: <ícono de oro> **148** Oro, <ícono de XP> **78** XP» (los íconos propios de la interfaz si están cargados; si no, 💰 y 📊).
- **La tablita**: ya no es una línea larga. Son campos en línea de a tres por fila: ⏱️ Turnos · 🗡️ Daño hecho · 🩸 Daño recibido, y solo si hubo, 💥 Críticos · 💨 Esquives · 🔮 Curados. Sin título «Resumen del combate». Sale en `/hunt`, `/travel`, `/boss` (solo), `/autohunt`, la derrota con `/use` y la huida; el jefe en equipo tiene su propia pantalla y no se tocó. En la victoria queda debajo de oro / EXP / vida en la misma grilla de tres columnas. **Para mirar en el celu**: si «Daño recibido» se parte en dos renglones, acortar el nombre (la única palanca está en `AddCombatSummary`).
- **`/config`** (`aa config`, también `aa conf`, `aa set`, `aa settings`, `aa ajustes`): pantalla privada con la opción «📋 Resumen del combate» y un botón que la prende o la apaga; también `aa config resumen off|on|toggle` (o `summary`, `fight summary`). Se guarda en `player_settings` y vale desde la **próxima** pelea (se lee al arrancar). Hecho para sumar más opciones: cada una es un campo en `PlayerSettings`, una columna con su valor por defecto y un botón.
- **Migración**: `Database/add_player_settings.sql` (la tabla `player_settings`). **No hace falta correrla antes del binario**: sin la tabla el resumen se sigue mostrando y `/config` avisa «no pude guardar» al tocar el botón; correrla con `psql` cuando quieras que se pueda cambiar.
- **Todo en los dos idiomas** (las 21 entradas nuevas están en `Locales/en.json`); arnés nuevo `v0261test` (29 comprobaciones) y la tablita probada en `loctest`.
- **Queda abierto**: si en el celu las tres columnas se ven bien; que `/config` sume más opciones cuando haga falta (idioma en la misma pantalla, por ejemplo); y que la derrota (que tiene dos barras de vida en línea) reparta la tabla en la misma grilla — si molesta, se puede apilar la vida en esa pantalla.

## El bot en español e inglés: `/language` (v0.26.0, 2026-10-23)

- **Pedido del dueño**: pasar el bot a inglés con un selector; «son dos idiomas, español e inglés» (no se diseña para un tercero) y «más que un riesgo es una ventaja» que los comandos de texto también acepten las palabras en inglés (`aa enchant weapon`).
- **Qué hay**: el motor `GameData/Loc.cs` (`T`, `F`, `Mark`, `Tr`) con el catálogo `Locales/en.json` (plantilla en español → plantilla en inglés, 759 entradas, embebido en el programa); el idioma de cada jugador (`player_language`, `/language idioma:English` o `aa language en`; sin elegir, el de su Discord: `es*` → español, el resto → inglés); el escáner `tools/loc`; y el arnés `loctest` (~220 comprobaciones). El diseño y las 15 tareas: `docs/superpowers/specs/2026-10-23-idioma-design.md` y `docs/superpowers/plans/2026-10-23-idioma.md`; cómo se traduce una pantalla: «Language» en `CLAUDE.md`.
- **Migración**: `Database/add_language.sql` (la tabla `player_language`). **No hace falta correrla antes del binario**: sin la tabla, el servicio registra el error (una vez por minuto) y todos ven español, que es lo de siempre; correrla con `psql` cuando quieras que `/language` guarde la elección.
- **Ya está en inglés**: `/language`; los errores y avisos comunes («todavía no te registraste», «¡Upa!…», el bloqueo de pelea, «todavía no podés»); `/cd`; los recordatorios; `/profile`, el aviso de subir de nivel y `/start`; **todo el combate solitario** (`/hunt`, `/travel`, `/boss` solo, `/autohunt`, `/use` dentro y fuera de la pelea, el aviso de combate abandonado y las frases de los 15 monstruos, los 6 jefes y el Asador en sus 5 escalones); `/chop`, `/mine` e `/inventory`; la taberna, `/heal` y `/shop` con las frases del Herrero, el Tendero, el Tabernero y el del Inframundo; `/daily` y `/missions` (con los 18 títulos de misión); y la ayuda: `/info`, `/tutorial` y los temas `hunt`, `gather`, `shop`, `daily` y `missions`.
- **Los nombres se quedan en español** (decisión del dueño): ítems, monstruos, zonas, personajes, clases, habilidades, títulos, logros y sus páginas, «Fogón», «Sifón de Almas». Los oficios y las rarezas sí tienen nombre en inglés (Woodcutter, Miner, Enchanter; Common… Mythic) y lo que se guarda sigue en español.
- **Todavía en español para quien juega en inglés** (cae al español solo; se traduce igual: envolver, `--skeleton`, `en.json`): `/enchant` y `/dismantle` (y los efectos), `/bank`, `/play` y el blackjack, `/forge` y la herrería (la escena y las recetas), `/forge-special`, las zonas (`/zona`, `/zonas`, `/drops`), el jefe en equipo (`RaidModule`), el jefe mundial, `/fight` y `/arena`, `/trade` y `/exchange`, `/pet`, `/professions`, `/fuegonuevo` y las bendiciones, `/deepshop`, `/story` y toda la historia (`Lore`), `/titles` y sus 113 nombres, `/achievements` y sus textos, `/leaderboard`, `/history`, `/reminders` (la pantalla; los avisos sí), `/vote`, `/tips`, el minievento, los avisos que llegan al terminar un comando (misión completada, logro, título nuevo, capítulo), las descripciones de las habilidades de clase (`aa hab`), los textos de ayuda de `aa sell` y `aa cf` sin argumentos, el resultado de `/exchange` dentro de la taberna, y 21 de los 26 temas de `/info` (salen enteros en español, sin mezclar: `HelpTopic.InEnglish`).
- **Lo que no se puede traducir con este mecanismo**: los nombres y descripciones de los comandos en la lista de «/» de Discord (hace falta la localización propia de Discord, `NameLocalizations`/`DescriptionLocalizations`, que no se armó) y los nombres de los parámetros (`tema`, `modo`, `companero`…).
- **Palabras en inglés al teclear**: `aa enchant weapon|amulet [critical|blood|edge|guard|life|dodge] [advanced]`, `aa enchant info|options|help`, `aa sell weapon|amulet|all|inventory|bag`, `aa cf heads|tails|h|t`, `aa chop advanced`; las palabras en español siguen valiendo. El arnés prueba 28 formas contra el `CommandService` real.
- **Una trampa que se encontró al traducir**: la taberna reconocía «ya estás sano» mirando `Title.Contains("máximo")`; en inglés dejaba de funcionar y mostraba la frase equivocada. Ahora los dos lados comparan con `UseModule.AlreadyFullTitle()`. Si algún día algo decide mirando un texto, que sea el de una función compartida, nunca un literal en español.
- **Lo que queda abierto para el dueño**: que alguien que hable inglés revise el tono de `Locales/en.json` (sobre todo las frases de los monstruos y de los NPC), y decidir con qué pantallas seguir (lo que más se ve: `/enchant`, `/bank`, el jefe en equipo y los avisos de misión/logro).

### Lo que falta en inglés — checklist (anotado el 2026-10-23, de lo que más se ve a lo que menos)

Cada casilla es una tarea chica y del mismo molde (ver «Language» en `CLAUDE.md`): prueba primero en `tests/loctest`, `dotnet run --project tools/loc -- --untranslated <archivos>`, envolver, `--skeleton`, `en.json`, suite completa. Las cantidades son textos sin envolver que cuenta el escáner (aproximadas: incluye algunos nombres de comando que no se traducen).

**Alta (lo que más ve cualquiera)**
- [ ] **Avisos al terminar un comando** — `Services/ProgressNotifier.cs`: «¡Misión completada!», «¡Logro desbloqueado!», «¡Título nuevo!», capítulo de la historia, y los de `NoticeDelivery`. Salen en español a todos.
- [ ] **`/enchant` y `/dismantle`** — `Modules/DustModule.cs` (~105), `GameData/Enchantments.cs` (~39: nombres de tiers y efectos, descripciones), `Dismantling`. Los efectos (Filo, Crítico, Sangre, Guarda, Vida, Esquive) son nombres y se quedan; sus descripciones no.
- [ ] **Jefe en equipo** — `Modules/RaidModule.cs` (~169: invitación, botones Aceptar/Rechazar/Empezar/Cancelar, turnos, aviso de turno, comer, victoria y derrota), `Services/RaidTurnOutput.cs`, `PartyBossService`, `IRaidSessionService`.
- [ ] **`/bank`** (`BankModule` ~35 y `BankRules`), **`/play`** (`CasinoModule` ~18, `CasinoService`) y **blackjack** (`BlackjackModule` ~20, `BlackjackService` ~16, la mesa y los botones).
- [ ] **Herrería** — `ForgeModule` (~36: las recetas, «have/need»), `BlacksmithModule` (~23: la escena y sus menús), `ForgeSpecialModule` (~14), `ItemStatLabel`, `RecipeCatalog`.
- [ ] **Zonas** — `ZoneModule` (~29: `/zona`, `/zonas`), `DropsModule` y `DropsCatalog` (`/drops`).

**Media**
- [ ] **`/achievements`** — `AchievementsModule` (~26) y `AchievementCatalog` (~31 descripciones; los NOMBRES de logros y sus páginas se quedan).
- [ ] **`/titles`** — `TitlesModule` (~30) y `TitleCatalog` (~134: pistas y páginas; los nombres de los títulos se quedan).
- [ ] **`/pet`** — `PetModule` (~55), `PetRules`. **`/professions`** — `ProfessionModule` (~15), `ProfessionRules` (las descripciones de efectos y de la versión avanzada).
- [ ] **`/fuegonuevo` y `/blessings`** — `FuegoNuevoModule` (~87), `BlessingCatalog` (~47), `FuegoNuevoRules`.
- [ ] **`/story`** — `StoryModule` (~36) y toda `GameData/Lore.cs` (~97: los capítulos y las 13 escenas; es texto largo y literario: conviene que lo revise alguien nativo).
- [ ] **PvP** — `DuelModule` (~54), `ArenaModule` (~47), `ArenaNarrator` y `ArenaStory` (los anuncios golpe por golpe), `DuelEngine` (~16), `GameHistory` (~21: `/history` y `/duels`).
- [ ] **Jefe mundial** — `WorldBossModule` (~32), `WorldBoss` (~20).
- [ ] **Intercambios** — `TradeModule` (~31), `ExchangeModule` (~20; su resultado aparece dentro de la taberna), `GiveModule` (~14), `BoxModule` (`/open`, ~16).
- [ ] **Resto de pantallas** — `/leaderboard`, `/reminders` (la pantalla, ~17; los avisos ya están), `/vote` (`VoteModule` ~21), `/tips` (`FarmAdvisor` ~14), `/deepshop` (`DeepShopModule` ~18), el minievento (`MiniEvents` ~14, `MiniEventService` ~13), `OnboardingModule` (`/class` y lo que queda, ~13), `TextCommandModule.Economy` (~41) y `.Combat` (~24), los textos de uso de `TextCommandModule`.

**Baja**
- [ ] **Ayuda** — los 21 temas de `/info` que faltan (`HelpTopics`, ~270 textos; cada uno entero, con `InEnglish: true` al terminar), las descripciones de las habilidades de clase (`ClassAbilities`, `aa hab`), los textos de `aa sell` y `aa cf` sin argumentos (`TextShortcuts.SellHelp`/`CoinflipHelp`) y el «¿Qué hace?» del error de uso (`[Summary]` de cada comando de texto).
- [ ] **Pulido del inglés ya hecho** — plurales con «(s)» (`turn(s)`, `hit(s)`, `crit(s)`), `Loc.Percent`/`Loc.Decimal` en los pocos lugares que todavía arman decimales a mano (`HelpTopics.Dec`), y la revisión del tono por alguien nativo.
- [ ] **Lo que NO se resuelve con `Loc`** — los nombres y descripciones de los slash commands y de sus parámetros en la lista de «/» de Discord: hace falta la localización propia de Discord (`NameLocalizations` / `DescriptionLocalizations`); es un mecanismo aparte que no se armó.
- [ ] **Deuda chica del propio idioma** — el caché de `PlayerLanguageService` no borra entradas vencidas; un jugador con Discord en inglés que nunca usó un comando de barra y escribe `aa …` ve español (el mensaje de texto no trae idioma; al usar cualquier barra queda guardado).

## En plena pelea solo valen los comandos de la pelea (v0.25.4, 2026-10-23)

- **Pedido del dueño**: «bloqueá todos los comandos si estás en medio de pelea, exceptuando los de la pelea como el `use`; no quiero que pueda recolectar madera en plena pelea».
- **Qué es «en pelea»**: un combate solitario (`/hunt`, `/travel`, `/boss`), un jefe en equipo que ya está peleando (no la sala de invitaciones) o un duelo en curso.
- **Qué vale mientras tanto** (lista de lo PERMITIDO, `GameData/FightLock.cs`): `/use`, `aa attack`, `aa ability`, `aa flee` y, porque solo miran, `/profile`, `/inventory`, `/cd`, `/info` y `/tutorial`. Todo lo demás (recolectar, forjar, la taberna, el casino, el banco, las zonas, `/daily`, empezar otra pelea...) contesta «Estás en plena pelea: no podés usar `/chop` hasta que termine» y no hace nada. Los botones del combate no son comandos y nunca se bloquean. **Decisión mía**: dejé pasar los cinco que solo miran (el dueño dijo «todos»); si los quiere bloqueados también es sacarlos de la lista.
- **Dónde**: UN solo lugar, antes de despachar el comando (`Program.cs`, el de barra por su nombre y el de texto por el comando que se encontró, así `atk` y `u` valen como `attack` y `use`). Un comando nuevo queda bloqueado en pelea hasta que alguien lo permita. `v0254test` compara la lista contra los comandos reales.
- **Lo que NO se bloquea** (no se pidió): los botones y desplegables de otras escenas (taberna, misiones...), por ejemplo comprar en la taberna con una pelea abierta.
- **«No tenés ningún combate activo»**: el aviso ahora dice POR QUÉ cuando el bot lo sabe (pasó más de 1 minuto sin jugar el turno; o ese combate ya se resolvió y el mensaje es viejo) y, si no hay registro, lo atribuye al reinicio del bot (las peleas viven en memoria y cada actualización las corta) o a otra copia del bot respondiendo. También deja una línea `[COMBATE] … intentó pelear sin combate activo (causa)` en el registro de Railway. **Sospecha principal** (sin poder verlo desde acá): dos copias del bot con el mismo token a la vez (la de la compu del dueño y la de Railway) responden las dos a cada botón, y la que no tiene la pelea en memoria contesta ese error; o las actualizaciones de hoy cortando peleas. Para confirmarlo: mirar en el registro de Railway si aparecen `[COMBATE]` con «sin registro» justo después de un deploy, y que no haya otro `dotnet run` abierto.

## La misma vida en todas las pantallas (v0.25.3, 2026-10-23)

- **Lo que se vio** (capturas de un amigo del dueño, un Ninja): en UNA pelea la vida máxima pasaba de 323 a 340 y la actual de 244 a 257. No era un error de cuentas: la pelea muestra la vida de combate (la de la base por el factor de la clase: Ninja ×0,95, Arquero ×1,15, Guerrero ×1,2, más el encantamiento Vida y la bendición Corazón de Brasa), y la pantalla de victoria, el perfil, `/heal` y `/use` mostraban la real (340 × 0,95 = 323; 257 × 0,95 = 244).
- **Pedido del dueño**: «necesito que siempre sea la misma vida… sea la clase que sea, mostrá la vida que debería tener: si es ×0,95, mostrá el resultado». (Yo había propuesto mostrar siempre la real; prefirió la otra.)
- **Cambio**: todas las pantallas muestran la vida CON el factor de clase: el perfil, la victoria, `/autohunt` (victoria y «muy herido»), `/heal` (y «recuperaste N», que es la diferencia de las dos cifras que se ven), `/use` fuera de combate, «Curarme del todo» de la taberna y la tarjeta de bienvenida (un Ninja nuevo arranca con 95/95). Es una sola cuenta: `PlayerCombatProfileCalculator.CombatPassives` arma el factor para pelear y para mostrar. La base guarda la vida real y todas las cuentas siguen igual: solo cambia el número que se imprime.
- **Curar y subir de nivel, en las mismas unidades (el dueño: «que haga la cuenta antes si es esa clase… que quede todo óptimo»)**: la comida que dice «cura 15 HP» cura 15 de la vida que ves, como ya pasaba dentro de una pelea. Fuera de pelea `/heal`, «Curarme del todo» y `/use` planean en esas unidades y le pasan a la base la vida real equivalente (15 = 16 reales para un Ninja, 13 para un Guerrero); «Curarme del todo» llena la barra entera (el redondeo no deja uno de menos). El aviso de subida de nivel dice «+14» para un Ninja y «+18» para un Guerrero (antes +15 para todos).
- **Efecto en la economía, a propósito**: el daño que te hacen es el mismo en vida que se ve para todas las clases, así que curar en esas unidades hace que rellenar cueste lo mismo para el mismo daño. Antes la comida fuera de pelea curaba vida REAL: un descuento oculto del 20 % para el Guerrero (×1,2) y del 15 % para el Arquero (×1,15), y un recargo del 5 % para el Ninja. Ahora Guerrero y Arquero comen un poco más para rellenar lo que perdieron. Para deshacerlo: volver `realHeal` (TavernModule) y el `ToReal` (UseModule) a la vida real.
- **Medido por la pregunta del dueño sobre el Ninja y el Mago (v0.25.3, `tools/balance2 -- L 5 <clase>`, zona 5 a nivel mín+3, equipo propio, con comida y habilidad)**: perder contra el jefe sin encantar / con Esquive V / con Filo V / con Guarda V: **Ninja 42,5 / 30,0 / 30,1 / 13,1 %**; Hechicero 42,5 / 26,5 / 25,8 / 12,9 %; Guerrero 57,3 / 32,3 / 28,2 / 16,9 %; Arquero 72,8 / 48,4 / 43,8 / 29,5 %. El Esquive V del Ninja vale lo mismo que el Filo V y es el segundo peor encantamiento de amuleto (la Guarda lo supera por mucho), no está roto. El Mago pierde casi igual que el Ninja contra el jefe, y el Guerrero y el Arquero pierden más; lo que lo distingue es la VIDA que le cuesta (61-73 % contra 85-96 % de los otros en la zona 5 a mín+3, y 2,6 % por cacería común contra 4-4,7 %) por el Sifón de Almas. En duelos (`stage12test`) el Mago gana en promedio 53 % (Guerrero 42, Arquero 48, Ninja 56): no es el mejor. Palanca si se lo quiere recortar: `LifestealChance` 0,30 → 0,25 en `ClassPassives` (la cura esperada baja de 15 % a 12,5 % del daño; mide antes con `-- L` y `stage12test`).
- **Pruebas**: `v0253test` (la cuenta pura por clase, el caso 340 → 323 / 257 → 244, el perfil, `/heal`, `/use`, `/autohunt`, la taberna, la tarjeta de bienvenida y el servicio de bonus, incluido que no tire si se cae).
- **Para la base viva**: nada (sin migración).

## El perfil, ordenado para el celu (v0.25.2, 2026-10-23)

- **Pedido del dueño** (viendo el perfil en el celu, ya con la v0.25.1 en Railway): la XP y la vida se cortaban y bajaban de renglón; sacar la habilidad («eso con `aa hab` ya se ve»), la marca de mascotas («ya queda incluido»), el título «Estado»; la zona arriba de la XP; una línea en blanco entre DEF y oro y entre banco y racha; «ATQ:» con dos puntos y «(sin arma)» / «(sin amuleto)» entre paréntesis; el ícono primero y después el nombre.
- **Las barras** pasan de 10 a **8 piezas** (`GameModule.ProfileBarSegments`): en el celu la miniatura le saca ancho al campo y 10 piezas no entraban. Sin forma de saber desde el bot si alguien mira en el celu, es el mismo largo para todos. Si todavía se parte en algún celu, bajar ese número (7 o 6).
- **El perfil queda así**: título «⚔️ Nombre - Título (Clase)»; descripción «FN · LV», la zona («🌲 Zona: 2 (máx. 2)») y, solo si corresponde, «Venció al Asador»; campos XP y Vida (barra de 8 + números) y un bloque **sin título** con tres grupos separados por una línea en blanco: «<arma> ATQ: 48», «<amuleto> DEF: 14 (sin amuleto)» / «<oro> Oro», «<banco> Banco», «<polvo> Polvo» / «<racha> Racha: día 15». Las marcas ⚡ 🍖 🔥 ✨ siguen detrás del número; la 🐾 de las mascotas ya no (el número de DEF las incluye).
- **El campo sin título** lleva un carácter de ancho cero como nombre (Discord exige uno): se dibuja como un renglón en blanco entre las barras y los números. No es el «campo vacío como separador» que dejaba un hueco enorme en la v0.9.1: este tiene contenido y solo le falta el título. Si en el celu se ve demasiado aire, la salida es pasar XP, Vida y los números a la descripción (sin campos).
- **Dejé sin tocar** a propósito: los renglones de XP y Vida siguen «1646 / 4687 <ícono>» (el ícono al final, como unidad) y el nombre de cada campo en texto plano (un emoji propio no se garantiza en el nombre de un campo).
- **Pruebas**: los arneses que leían el campo «Estado» (`pettest`, `stage2btest`, `stage13test`, `v90test`, `fogontest`, `v0220test`, `fntest`, `v0194test`) pasan por `tests/Shared/ProfileProbe.cs`, y `v0194test` suma las comprobaciones del formato nuevo (zona en la descripción, tres grupos, ícono primero, 8 piezas, «(sin arma)»).
- **Para la base viva**: nada (sin migración).

## La Guarda baja un poco (v0.25.1, 2026-10-22)

- **Pedido del dueño**: «bajala un poquito si te parece muy OP». Sí lo era: la Guarda V dejaba al jefe de la zona 5 (a nivel mín+3) en **8 % de derrota** contra ~32 % del Filo V y ~27 % de la Vida V, porque la defensa se resta fija de cada golpe.
- **Cambio**: la Guarda pasa de +4/8/13/19/26 % a **+3/6/9/13/18 %** de la DEF de la pieza. Antes copiaba la tabla del Filo; ahora tiene la suya (`Enchantments.StatBonusPercent`) y el Filo no cambia. Medido con `tools/balance2 -- L 5`: la Guarda V queda en **~18 %** de derrota en la zona 5 y 6 a 7 % en las zonas 3 y 4 (antes 2 %). Sigue siendo el mejor encantamiento del amuleto (~1,7 veces lo que da el Filo en su pieza), pero ya no es 4 veces.
- **Probado**: 2/4/6/9/13 (paridad con el Filo, 27 %), 3/5/8/12/16 (22 %), **3/6/9/13/18 (elegido, ~18 %)** y 4/7/11/15/20 (15 %). Para bajarla hasta la paridad, cambiar la fila de la Guarda en `KindTable`.
- **Qué pasa con lo ya encantado**: los amuletos con Guarda conservan su tier y rinden con la tabla nueva (un Soberano pasa de +26 % a +18 % de la DEF). El tier V es rarísimo (1 % por intento), así que casi nadie lo tiene.
- **Cambios de pantalla**: las listas de tiers de `/enchant` ya no ponen un solo «bonus +X %» (depende del efecto); el bonus de cada efecto sale de la lista de efectos de `/enchant info`.
- **Para la base viva**: nada (sin migración).

## Blackjack en el casino: `aa bj 10k` (v0.25.0, 2026-10-22)

- **Pedido del dueño**: «armate el bj entonces, `aa bj 10k` por ejemplo» (antes: «anota el blackjack para armar en `play`»).
- **Cómo se juega**: `/play game:blackjack apuesta:10k`, `aa bj 10k` (también `aa blackjack` y `aa 21`; la apuesta puede ser un número, `10k`, `1m` o `all`) o `aa play blackjack 10k`. Sale una mano con **Pedir / Plantarse / Doblar**: la banca muestra una carta y tapa la otra; se acerca a 21 sin pasarse. Por texto, `aa bj hit`, `aa bj stand`
  y `aa bj double` hacen lo mismo que los botones (para cuando un botón no responde).
- **Reglas**: mazo infinito; el **blackjack natural paga 3 a 2** (si los dos lo tienen, empate; si lo tiene solo la banca, perdés al instante); ganar paga 1 a 1; la banca pide hasta 17 y **se planta en todos los 17**; se puede **doblar** con las dos primeras cartas (una carta más y la apuesta se duplica); llegar a 21 pidiendo se planta solo. **Sin dividir ni seguro** (a propósito, para mantenerlo simple).
- **La casa no regala** (medido con 1.000.000 de manos y la estrategia básica de estas reglas): el jugador recupera **0,989** por cada unidad apostada, o sea la casa se queda con ~1,1 %; jugando mal (plantarse siempre desde 12) recupera 0,92. Siempre por debajo de 1, como las slots.
- **La plata**: la apuesta se cobra al repartir (sin oro no se juega) y el premio se paga al terminar; doblar cobra otra apuesta igual en el momento. Una mano por jugador; 6 «aa bj» a la vez reparten una sola mano y 10 clicks a la vez en un botón pagan una vez. **Una mano abandonada no pierde la apuesta**: a los 90 segundos sin jugar la banca te planta y se paga lo que salga.
  Un reinicio del bot sí corta las manos en juego (viven en memoria, como los combates): la apuesta ya cobrada se pierde. Tope de apuesta: 250.000.000.
- **Historial y logros**: cada mano deja `casino_win` / `casino_loss` con `detail = blackjack` (el empate no deja nada); `/history` tiene su renglón «Blackjack» y el logro Afortunado cuenta lo que se gana.
- **Para la base viva**: nada (sin migración). Arnés nuevo: `tests/v0250test` (52 arneses en total).

## El jefe mundial: `aa miniboss` (v0.24.0, 2026-10-22)

- **Pedido del dueño**: «`aa miniboss` y que todos peleen contra él, o worldboss; mientras más gente, mejor; que anotarse gaste el cooldown del jefe; que dé entre 30 y 50 cacerías de esa zona». El motivo: `aa boss` pide nivel 5 y quería algo para hacer con ese cooldown antes de llegar.
- **Cómo funciona**: un jefe por día, el mismo para todos (7 jefes que rotan por la fecha, sin tabla). Durante el día te anotás con `/miniboss` o `aa miniboss` (también `worldboss`, `wb`, `jefemundial`): **no pide nivel y gasta el cooldown del jefe de 5 horas** (el mismo de `/boss`, con la misma guarda; con el cooldown corriendo
  no te anotás y te dice cuánto falta). A las 00:00 de Uruguay, el mismo reloj de la Arena lo pelea entre todos los anotados y lo cuenta en el canal (3 mensajes: el jefe y los anotados, la batalla con los golpes de los que más le pegaron y la barra de vida, y el resultado con el premio y el MVP).
- **El premio** (el mismo para todos los anotados, en cacerías de oro de SU zona): **50 si el jefe cae; si no, 30 más 20 por la parte de la vida que le sacaron entre todos**. Cada uno aporta según cuánto pega comparado con lo que se espera en su zona (así un nivel 1 en la Zona 1 y un veterano en la Zona 5 aportan del mismo orden), con una forma del día de ±30 %. Medido: 1 jugador normal ~32 cacerías, 5 ~40, con 10 el jefe cae la mitad de las veces y con 12 o más casi siempre (50): **mientras más gente, mejor para todos**.
- **Economía**: 50 cacerías al día son ~15 a 20 % de una sesión perfecta de 2 horas en cada zona (el `/daily` paga 6 a 20). El oro puede viajar por `/give`: el mismo riesgo de cuentas alternativas que ya tiene el diario. Las perillas son `MinHunts`, `MaxHunts`, `FullCrowd` y `FormSwing` (`WorldBossRules`).
- **No se hizo**: una línea en `/cd`, un jefe por servidor (cuando haya mucha gente conviene, como la Arena), premios que no sean oro.
- **Para la base viva**: **correr `Database/add_worldboss.sql` con `psql` ANTES del binario v0.24.0** (2 tablas nuevas, re-ejecutable). Sin ellas el reloj registra un error cada 30 segundos y `/miniboss` no anda. Opcional: `WorldBoss__ChannelId` para fijar el canal del anuncio. Arnés nuevo: `tests/v0240test` (51 arneses en total).

## La Arena con azar y contada pelea por pelea (v0.23.2, 2026-10-22)

- **Pedido del dueño**: «la Arena está buena pero la gana siempre el de más nivel… quiero que pueda ganarla alguien de nivel 1 contra el del 20, si no llega un punto en que se hace imposible… random con textos mientras pasan los duelos».
- **Qué se midió** (mismo motor y misma clase): sin azar, con +1 nivel el de arriba gana 65 a 90 %, con +5 niveles 89 a 100 %, y un nivel 1 sin equipo contra un nivel 20 con equipo de zona 2 no ganaba NUNCA (la defensa se resta fija y le deja el daño mínimo). Un «poco más de azar» no alcanzaba: ni siquiera con ±40 % de suerte el nivel 1 ganaba.
- **Tres capas, solo en la Arena** (`GameData/ArenaTuning.cs`; `/fight` sigue con el poder real): poder comprimido (daño, defensa y vida de la pareja se acercan a su media geométrica, exponente 0,5), forma del día (±30 % de daño y vida por pelea) y milagro (en 30 de cada 100 peleas los dos pelean con el poder igualado).
- **Resultado medido** (el de más nivel gana): parejos 50 %, +1 nivel ~55 %, +5 ~64 %, +10 ~71 %, +19 ~78 %, y **nivel 1 sin equipo contra nivel 20 con equipo de zona 2 o 4: gana ~14 a 15 de cada 100**. Subir de nivel y de equipo sigue sirviendo. Las tres constantes son el único lugar donde se retoca; para más azar, subir `MiracleChance`.
- **La pelea contada**: cada pelea guarda su apertura (el milagro, o quién llegó muy bien o muy mal, con el porcentaje) y hasta 8 golpes (el último, el primero, el más fuerte, habilidades, críticos, esquives), con los textos del duelo amistoso. El anuncio cuenta las primeras rondas una pelea por renglón y **las semifinales y la final golpe por golpe**, cada una en su mensaje; la apertura del torneo avisa que tiene azar. `/info tema:arena` lo explica con los números reales. Los relatos viven solo en memoria: la llave guardada y `/arena results` son los de siempre.
- **Para la base viva**: nada (sin migración). Arnés nuevo: `tests/v0232test` (50 arneses en total); `stage12test` verifica que el torneo real deje su relato.

## Atajos de texto y montos con k y m (v0.23.1, 2026-10-22)

- **Pedido del dueño**: «que se pueda hacer `aa sell all`, `aa sell arma`, `aa sell amuleto`, sin pasar por shop; lo mismo con deposit y withdraw sin poner bank antes; en el play `aa cf h` o `t` y un número; y que exista la forma de poner 10k por 10000, y m de millones».
- **Montos**: `AmountParser` lee `10k` = 10.000, `1m` = 1.000.000 y `1,5m` = 1.500.000. Con k o m el punto y la coma son decimales (`1.5k` = 1.500); sin ellas siguen siendo separadores de miles (`1.500` = 1.500, como siempre). Vale en todos lados donde se escribe un monto: `/play`, `/give`, `/bank` y los atajos nuevos.
- **Atajos** (solo por texto): `aa sell all` (todo lo vendible de la mochila), `aa sell arma` y `aa sell amuleto` (lo que llevás puesto, sin pedir confirmación porque lo escribiste vos), `aa sell <ítem>`, `aa sell <cantidad> <ítem>` y `aa sell all <ítem>`; `aa deposit` y `aa withdraw` (alias `dep`, `wd`, `retirar`, `sacar`);
  `aa cf h 100` / `aa cf t 10k` / `aa cf 100 h` (h = heads, t = tails, también cara y cruz; el lado y la apuesta en cualquier orden); y de yapa `aa slots 100` (`sl`). Los comandos largos siguen andando.
- **Qué no se hizo a propósito**: `aa sell arma` vende al instante la pieza puesta (la forja cuesta mucho más que lo que pagan); la taberna sigue pidiendo confirmación porque ahí se toca un menú sin querer. Si hace falta el mismo cuidado por texto, es un botón de confirmación más.
- **Para la base viva**: nada (sin migración). Arnés nuevo: `tests/v0231test` (49 arneses en total).

## Títulos (v0.23.0, 2026-10-22)

- **Pedido del dueño**: «creá muchísimos, como 50 mínimo, que estén en páginas en `aa titles` y que se puedan equipar»; lo corrigió dos veces: **no van en el inventario** (idea mía), se equipan al **perfil**, que ahora muestra **«Nombre - Título (Clase)»**, y «dale sencillo». «Título nuevo me gusta, que busque cuál es.»
- **113 títulos en 9 páginas** (Combate 18, Oficios 12, Economía 12, Constancia 12, Mascotas y Fogón 15, Jefes y niveles 15, Maestros de oficio 9, PvP y comunidad 12, Curiosos 8), todos cosméticos. 69 salen de los 23 logros (3 por logro, con la meta del propio tramo: si se retoca un logro, su título lo sigue) y 44 son especiales: el primer triunfo sobre cada jefe
  de la escalera, el nivel más alto alcanzado, los niveles de la Brasa de Color, maestro de un oficio (nivel 25 / 50 / 100), duelos, Arena, votos, trueques, derrotas, hallazgos (Corteza, Meteorito, encantamiento tier V), el Inframundo y «Recién Llegado» (el inicial). La lista completa está en `docs/titulos.md`.
- **Qué se guarda**: solo cuál tenés puesto (`users.equipped_title`). Qué tenés se calcula de contadores y eventos que sobreviven al Fuego Nuevo (nunca de la zona más alta vencida ni del nivel de la cuenta, que se reinician). Los títulos de la Brasa de Color dejan de ser una línea fija del perfil: ahora se desbloquean con el nivel de la bendición y se ponen con `/titles`
  (el color del perfil sigue igual); quien ya la tenía ve «Recién Llegado» hasta que se ponga uno.
- **Pantallas**: `/titles` y `aa titles [página]` (✅ los que tenés, 🔒 los que faltan con cómo conseguirlos y cuánto llevás; desplegable de páginas, desplegable para equipar con los que tenés de esa página y botón «Quitar título»), y `aa title <nombre>` / `aa title quitar` por texto.
- **«¡Título nuevo!»**: el aviso de un logro ya dice cuál título trae; los especiales que se pueden saber con el evento (un contador que llega a su meta, subir de nivel, la primera victoria sobre un jefe de la escalera) avisan solos. La Brasa de Color y los tres hallazgos únicos no avisan: aparecen en `/titles`.
- **Membresía**: el título especial de miembro queda para cuando se construya (una regla nueva con la comprobación de la suscripción, en una página «Especiales»). No se hizo nada de eso.
- **Para la base viva**: **correr `Database/add_titles.sql` con `psql` ANTES del binario v0.23.0** (agrega `users.equipped_title`; re-ejecutable). Arnés nuevo: `tests/v0230test` (48 arneses en total); se actualizaron `v0194test` y `fntest` (el encabezado del perfil y la Brasa de Color).
- Para tunear: las metas de los 69 de logros se mueven en `AchievementCatalog`; los niveles de maestro (25/50/100) y los demás títulos especiales, en `TitleCatalog`.

## Emoji propio del ítem en los desplegables (v0.22.2, 2026-10-22)

- **Pedido del dueño**: «agregar los emojis a los trades, al 3 por 1 y a los ítems de los combos desplegables, el forje y todo; donde se pueda suma mucho».
- **Dónde SÍ**: cada opción de un desplegable tiene un campo de emoji aparte del texto y ahí Discord sí dibuja los emojis de la aplicación. Lo llevan los cinco menús de la taberna (comer, comprar comida, comprar caja, vender —la pieza puesta muestra su emoji y deja el 🗡️/📿— y el 3 por 1 con su segundo paso), el menú de recetas del herrero (el ✅/❌/🔁/🟢 sigue en el texto), el menú de curación de `/travel` y `/boss`, el de comida del jefe en equipo y la tienda del Inframundo.
- **Dónde NO se puede**: las listas que aparecen al escribir `/trade`, `/exchange`, `/forge make`, `/shop buy`, `/use` u `/open` son autocompletado y Discord las dibuja como texto plano (límite de la plataforma). Lo que sí se ve con emoji es el mensaje de la oferta de intercambio y el resultado del 3 por 1, que son texto de embed. **No hecho, a decidir**: cambiar `/trade` y `/exchange` por una escena con desplegables como la taberna (un flujo nuevo) para tener emoji también al elegir.
- **El riesgo y su guardia**: si Discord no conoce el emoji rechaza el mensaje ENTERO, y en el menú de curación de un combate eso impediría empezar la pelea. Una opción solo lleva emoji si el ítem lo tiene Y el id está en la lista de emojis que la aplicación tiene ahora (la misma que `UiEmojiRefresher` lee de Discord al arrancar y cada 10 minutos; `UiEmojis.IsKnownEmoji`). Sin la lista cargada, con un emoji de otra aplicación o con uno común, la opción sale exactamente como antes. **Límite conocido**: si se borra un emoji del portal, el bot lo sigue ofreciendo hasta 10 minutos (el próximo refresco): al cambiar el arte de un ítem, subir el nuevo, correr `apply_emojis.sql` y recién ahí borrar el viejo.
- **Corrección de una regla mía**: `CLAUDE.md` decía que ningún desplegable podía llevar emojis personalizados; eso valía para el TEXTO de la opción, no para su campo de emoji.
- Código: `Modules/ItemMenuExtensions.cs` (`ItemEmote`, `AddItemOption`), `ItemEmojiRegistry.MenuCodeOf`, `UiEmojis.IsKnownEmoji`; tocan `TabernaModule`, `BlacksmithModule`, `AdventureModule`, `RaidModule` y `DeepShopModule`. Arnés nuevo: `tests/v0222test` (47 arneses en total).
- **Para la base viva**: nada (sin migración). Para que se vean hace falta que `apply_emojis.sql` haya corrido en la base de producción y que los emojis estén subidos en el portal de la aplicación.

## El tier V del encantamiento, casi imposible; el Encantador ayuda (v0.22.1, 2026-10-20)

- **Origen**: un jugador pasó al jefe de la zona 5 sin equipo de la zona 5, con comida buena y los dos encantados. Medí con el simulador (parte **M**, `tools/balance2 -- M 4`): contra el equipo de la zona 4, el jefe pierde 99 / 95 / 64 / 6 % a nivel 23 / 28 / 35 / 45 sin encantar; con Filo V + Guarda V + banquete, 46 / 15 / 0,7 / 0 %. O sea: pasa el jefe quien está unos 15 niveles arriba de la zona, no hay un hueco. **Subir vida y daño del jefe a todos NO sirve** (con +15 % el jugador normal con equipo propio sin encantar a nivel 23 pasa de perder el 53 % al 95 %). Propuse escalar al jefe con el nivel por encima de mín+8 (+2 % vida y +1 % daño por nivel, tope +50/+25 %); **el dueño dijo que no hacía falta** y pidió otra cosa: que el tier V sea muy difícil. El jefe NO se tocó.
- **Tiers**: Tibio 42 % · Al Rojo 30 % · Ardiente 18 % · Incandescente 9 % · **Soberano 1 %** (antes 40/30/18/9/3). El Soberano pasa de ~33 intentos en promedio a ~100 (tiers acumulativos: nunca baja). Los que ya tienen un Soberano lo conservan.
- **El oficio Encantador ayuda**: +0,1 % por nivel de chance de que el tier sorteado suba uno (+10 % al 100, tope en el V; también en cada tirada del modo avanzado). Con el Encantador al 100 el V sale ~1,9 % por tirada (1 % + 9 % × 10 %) y ~3,8 % con las dos del avanzado. Cada intento ya sumaba XP al oficio: ahora el oficio también mejora el tier, que es lo que pidió. El pie de `/enchant` avisa «⬆️ ¡tu oficio mejoró el tier!».
- Tests: `v90test`, `v0220test` (los valores de `FixedRandom` ahora van sobre 1000) y `oficiostest` (la ayuda del oficio) se actualizaron.
- Simulador: parte **M** (el jefe contra jugadores sin el equipo de la zona, con encantamientos y banquete; acepta factores de vida y daño del jefe).
- **Para la base viva**: nada (sin migración).

## Encantamientos con efecto: Crítico, Sangre, Vida y Esquive (v0.22.0, 2026-10-20)

- **Pedido del dueño**: «efectos nuevos de encantamiento» (los cuatro de mi propuesta: **arma**, Crítico y Sangre; **amuleto**, Vida y Esquive, además de los de siempre, Filo y Guarda). Me dejó decidir entre elegir y sortear: **el jugador elige el efecto y el tier se sortea**.
- **Cómo funciona**: `/enchant arma efecto:Crítico` (o `aa enchant arma crítico`). Sin elegir queda el efecto de la pieza; el mismo efecto solo sube el tier (como siempre); **otro efecto sobre una pieza que ya tiene tier la REEMPLAZA** con el tier que salga (puede ser menor), así que primero pregunta con dos botones y no cobra hasta que confirmás. Sobre una pieza sin encantar cualquier efecto se elige directo. Vender la pieza, forjar otra encima o un Fuego Nuevo devuelven el efecto a Filo / Guarda.
- **Solo contra monstruos**: Crítico, Sangre, Vida y Esquive no cuentan en duelos ni en la Arena (como las bendiciones de poder); Filo y Guarda siguen valiendo en todos lados. Una pieza con un efecto de combate no conserva el bonus de stat del Filo/Guarda.
- **Magnitudes medidas** (`tools/balance2 -- L`, jefes de las zonas 2 a 5 con equipo propio, comida y habilidad, y cacerías comunes; promedio de las 4 clases): Crítico +3/6/10/15/20 puntos de crítico · Sangre cura 1/2/3/4/5 % del daño de cada golpe · Vida +4/8/13/18/24 % de vida · Esquive +3/6/9/12/15 puntos. **Jefe de la zona 5 a mín+3** (% de perder): sin encantar 54 · Filo V 32 · **Crítico V 34 · Sangre V 33 · Vida V 26 · Esquive V 34** · **Guarda V 8**. Los cuatro nuevos quedan a la par del Filo en los tiers 3 y 5.
- **Hallazgo que hay que decidir**: **la Guarda V es 3 a 4 veces más fuerte que cualquier otro efecto** porque la defensa se resta fija de cada golpe (+26 % de un amuleto de 120 son ~31 puntos menos por golpe del jefe ≈ +50 % de vida efectiva). Es el estado de siempre y NO se tocó, pero deja a Vida y Esquive como la opción débil del amuleto. Opciones: dejarlo, o bajar la Guarda a la altura del Filo (p. ej. +2/4/6/9/13 %), que le quitaría poder a todos los amuletos ya encantados.
- Cambios de código: `Enchantments` (tabla de efectos), `PlayerCombatProfileCalculator.Resolve` (efectos de combate), `ClassPassiveProfile.LeechRatio` y `CombatTurnResolver` (la cura de Sangre; el log separa «🔮 Sifón de Almas» de «🩸 Sangre»), `DustModule`/`DustRepository` (elegir, confirmar y reemplazar de forma atómica), el perfil (marca el efecto), la vista previa del Fuego Nuevo y la ayuda. Simulador: parte **L**.
- Arnés nuevo: `tests/v0220test`. 46 arneses en total.
- **Para la base viva**: **correr `Database/add_enchant_kinds.sql` ANTES del binario v0.22.0** (agrega las dos columnas con Filo/Guarda por defecto: nadie pierde nada; re-ejecutable).

## El banco da hasta +5 % de oro en las peleas (v0.21.0, 2026-10-20)

- **Pedido del dueño**: «un minibuff al banco para tener plata en el banco un mínimo al menos… como una curva exponencial hasta el 5 %: tener más siempre te da más (100K → 4,90, 300K → 4,95, 1M → 4,97)». Eligió la opción B (un buff por saldo) y aprobó que sea **oro de las peleas** y que se mida **por zona**.
- **La curva**: `bonus = 5 % × (1 − e^(−saldo/τ))` con τ = 25.600 de oro en la zona 5 (así 100K da 4,90 %). El saldo se mide en cacerías de oro de la zona del jugador, así que el mismo esfuerzo vale lo mismo en cualquier zona: 3.700 de oro en la zona 1 ≈ 100.000 en la 5. Zona 5: 1K 0,19 % · 10K 1,62 % · 25K 3,12 % · 50K 4,29 % · 100K 4,90 % · 200K 5,00 %. Arriba de ~100K la curva es casi plana (sus 4,95 y 4,97 para 300K y 1M caen en el 5,00 de la curva exponencial: la diferencia es de centésimas).
- **Dónde suma**: cacería, viaje, jefe, jefe en equipo y `/autohunt` (todo lo que pasa por `PlayerBonuses.GoldMultiplier`), multiplicando con mascotas y Bolsillo Hondo. No cuenta en duelos ni en la Arena. Se ve en `/bank view` («Lo que rinde») y en `/info tema:bank`.
- **Ojo**: premia acumular oro. Si más adelante se quiere frenar, las perillas son `BankRules.MaxGoldBonusPercent` y `BankRules.GoldBonusHunts`.
- Arnés nuevo: `tests/v0210test` (la curva y su escala por zona, `PlayerBonuses`, el servicio contra la base real con el servicio roto y sin dependencias, la recompensa de una pelea, las pantallas y la inyección de dependencias). 45 arneses en total.
- **Para la base viva**: nada (sin migración).

## Perfil: clase, FN y nivel arriba; la zona, sin nombre, en el bloque Estado (v0.20.1, 2026-10-20)

- **Pedido del dueño**: «clase, FN, LVL abajo; XP, vida y zona luego; no poner el nombre de la zona, "Zona 1 (máx. 3)" y el ícono». Quedó: el título es la clase; la descripción es «FN 3 · LV 41» (el FN solo si es mayor a 0; debajo, solo cuando corresponde, «Venció al Asador» y el título de la Brasa de Color); los campos son **XP**, **Vida**, **Estado** y **Habilidad**. El bloque **Estado** lleva una línea cada uno: `Zona 1 (máx. 3) <ícono de la zona>`, `ATQ`, `DEF`, `Oro`, `Banco`, y `Polvo` y `Racha` cuando hay. En El Fogón dice `Zona 0 🔥 (tu zona: N)`.
- El bloque se llamaba «Equipo y bolsa» en la v0.19.5; los arneses que lo leen (`fntest`, `pettest`, `stage13test`, `stage2btest`, `v90test`, `v0194test`, `fogontest`) se actualizaron a propósito.
- **Para la base viva**: nada.

## La XP de los oficios depende de la rareza del material (v0.20.0, 2026-10-20)

- **Pedido del dueño**: «que el profession escale pero a nivel de XP por tier del material: normal 2xp, raro 7, épico 15, legendario 30, mítico 100». Aprobó mis tres ajustes: **por tirada (no por unidad)**, **ritmo igual** y **nadie baja de nivel**.
- **Cómo queda**: cada tirada de `/chop` o `/mine` suma la XP de la rareza que salió (Común 2 · Raro 7 · Épico 15 · Legendario 30 · Mítico 100) a un contador propio (`chop_xp` / `mine_xp`). Promedio real con las chances del juego: **5,43 XP por tirada** (antes 10 fijos), así que la unidad de la curva baja de 10 a 5 y el Encantador de 50 a 25 por intento: llegar al nivel 100 son ~750 tiradas (eran ~810) y ~160 intentos de encantar, o sea **el mismo ritmo promedio**, con la suerte pesando (un Mítico = 100 XP ≈ 18 Comunes).
- **Nadie baja de nivel**: `Database/add_profession_xp.sql` siembra `chop_xp = chop × 5` y `mine_xp = mine × 5` (la XP vieja en la escala nueva; el redondeo de la curva puede mover a alguien un nivel como mucho). **CORRER ANTES de arrancar el binario v0.20.0** (re-ejecutable, pero si el binario ya anduvo, quien talló o minó en el medio ya tiene su fila y el script no la toca).
- El pie de `/chop` y `/mine` muestra lo que dio la tirada («🪓 Leñador nivel 12 · 340/412 XP (+7)»); `/professions` y `/info tema:professions` imprimen la tabla desde las constantes. La tala avanzada (4 tiradas) suma la XP de cada una.
- Probado: `oficiostest` reescrito para la escala nueva (la XP por rareza, el evento `chop_xp` con las rarezas, la avanzada, el Fuego Nuevo sin tocar los contadores); la migración corrió dos veces contra la base local (3 jugadores sembrados, sin cambios la segunda vez).
- Sin hacer (no se pidió): XP del Encantador por tier de encantamiento.

## Perfil compacto y emojis provisorios (v0.19.5, 2026-10-20)

- **Pedido del dueño** (no tenía el arte todavía): el perfil con `ATQ: número y el arma (solo el emoji, sin el nombre)`, abajo lo mismo con `DEF`, abajo el oro y luego el banco. Ahora el perfil son cuatro campos: **XP**, **Vida**, un bloque **«Equipo y bolsa»** (una línea cada uno: ATQ, DEF, Oro, Banco, y Polvo y Racha cuando hay) y **Habilidad**. El ícono propio va al final de cada línea y las marcas (⚡ sinergia, 🍖 banquete, 🔥 Fuego Nuevo o bendición, 🐾 mascotas, ✨+N % encantamiento) solo cuando corresponden. Los nombres de campo van en texto plano: un emoji propio no se garantiza en el nombre de un campo.
- **Sumé cuatro íconos al juego de la v0.19.4** (`ui_oro`, `ui_banco`, `ui_polvo`, `ui_racha`: ahora son 25 nombres) y un **juego completo provisorio** en `docs/ui-emojis/` (se regenera con `node tools/ui-art/make-ui-emojis.js`): se arrastran al portal y todo se ve andando; con arte propio se reemplaza por nombre.
- Arnés: `v0194test` cubre el bloque con íconos; `stage13test`, `v90test`, `fntest`, `pettest` y `stage2btest` se actualizaron a propósito a la forma nueva.
- **Para la base viva**: nada.

## Íconos propios de la interfaz: FN, LV, XP y barras de emojis (v0.19.4, 2026-10-20)

- **Pedido del dueño**: el FN con un ícono propio (no la llama), el LV como un emoji azul, «XP» en vez de «Experiencia» y barras nuevas; además dijo que hay «mucho emoji de los de siempre, medios feos» y que quiere más de los propios.
- **Cómo funciona**: el bot lee los emojis de su aplicación por NOMBRE (`ui_fn`, `ui_lv`, `ui_xp`, `bar_{hp|xp|pesar}_{l|m|r}_{f|e}`; 21 en total, ver `docs/ui-iconos.md`), sin copiar ids y sin SQL; si falta uno cada pantalla sigue como antes. Se pueden subir de a tandas y los GIF funcionan.
- **Dónde se ven**: perfil (nivel y FN en la primera línea de la descripción, campo «XP»), ranking, subida de nivel, vida del jefe en equipo, barra del pesar del Asador. **Las barras de emojis solo van en campos de ancho completo**; el combate en dos columnas, las misiones, los logros, los oficios y el plantel del jefe en equipo siguen con la barra de texto. Llevarlas ahí pide apilar los campos (decisión pendiente).
- **Auditoría de emojis de siempre** (recuento de las fuentes, 2026-10-20): 108 de 120 títulos de embed y 96 de 115 nombres de campo llevan un emoji unicode; los más repetidos son 💰 ×12, ❤️ ×8, 📋 ×8, 🔥 ×7, ✨ ×6, 🎁 ×5, 📊 ×4. Propuesta (esperando su OK): un set propio de unos 12 íconos de estadística (oro, vida, ataque, defensa, XP, LV, FN, polvo, banco, racha...) y sacar el emoji de los títulos y de los nombres de campo que no aportan.
- **Arnés nuevo**: `tests/v0194test`. 44 arneses en total. Los arneses que esperaban «📊 Experiencia» o el FN en el título se actualizaron a propósito.
- **Para la base viva**: nada. Subir los emojis al portal con los nombres de `docs/ui-iconos.md` (se ven en ~10 minutos).

## Emoji propio en todas las pantallas y ranking por Fuego Nuevo (v0.19.3, 2026-10-20)

- **Emojis** (el dueño: «hay comandos que muestran en vez del emoji que es de ellos muestran otra cosa, por ejemplo el cofre de escoria en `aa drops`»): `/drops` ya dibujaba el emoji que trae la base (el arnés lo confirma con la base local, que tiene los 7 cofres, los 6 huevos y la comida de mascotas con arte), así que **lo que se ve en producción depende de que `Database/apply_emojis.sql` se haya corrido en Railway**; si el Cofre de Escoria sale con otra cosa en `aa drops`, es eso (datos, sin redesplegar). Lo que sí era código: las pantallas que solo tienen el NOMBRE del ítem escribían un 📦, 🥚 o 🦴 común (premios de misiones/logros/Arena, la caja del daily y del voto, el huevo de la victoria del jefe, `/pet`, la Alforja del Fuego Nuevo, el premio del mini-evento). Ahora usan `GameData/ItemEmojiRegistry` (nombre → emoji), que `Services/ItemEmojiRefresher` carga al arrancar y cada 10 minutos. No pueden llevar emoji propio: las opciones de menús, el autocompletado y los pies de los embeds (Discord no los dibuja ahí).
- **Ranking**: `/leaderboard` y `aa leaderboard` ordenan primero por Fuego Nuevo, después por nivel y después por XP, y cada línea muestra «🔥 FN N · Nivel N (clase)».
- **Cofres de Oro y los logros** (el dueño pidió pensarlo): los 10 logros con premio de caja (Cazador, Viajero, Matajefes, Recolector, Herrero, Comerciante, Generoso, Abridor, Constante, Racha) pagan en el tramo II la caja `prize` y en el III la `prize_top` de la zona del jugador AL RECLAMAR; en las zonas 4 y 5 las dos son Cofre de Oro (en la 3, solo la del tramo III), o sea hasta ~19 Cofres de Oro (+ el Arca del tramo III de Matajefes) por jugador, UNA sola vez en la vida (los logros no se reinician con el Fuego Nuevo), más los recurrentes (la caja grande de las misiones semanales y el día 7 de la racha del daily). La palanca, si se quiere bajar, son las filas `prize`/`prize_top` de la zona 4 en `zone_boxes` (datos, con el bot andando, 5 minutos de caché). **No se tocó nada.**
- **Arnés nuevo**: `tests/v0193test` (el registro, los textos de premios, el refresco contra la base, `/drops` y el orden del ranking). 43 arneses en total.
- **Para la base viva**: nada de migración. Correr `Database/apply_emojis.sql` si todavía no se corrió (es lo que arregla `aa drops`).

## Los banquetes curan 150 y 200 HP (v0.19.2, 2026-10-20)

- **Decisión del dueño** (viendo la tabla de abajo): «acomodemos el tema de la comida: 150 y 15 % de ataque, y 200 y 15 % de ataque». **Asado Completo del Domingo en Familia: 250 → 150 HP; Mate Dulce de la Abuela: 300 → 200 HP**; los dos siguen con +15 % de ataque por 30 minutos y los precios NO cambian (2100 / 2800). Lo asigné por precio (el más barato, 150; el más caro, 200): si los quería al revés es un `UPDATE`.
- **Cómo se aplica**: `Database/rebalance_banquets.sql` en la base viva (datos nada más, con el bot andando, re-ejecutable, se verifica sola; **el dueño tiene que correrlo en Railway**), y el final está en `seed_consumables_and_base_swords.sql` (la comparación contra una instalación nueva en una base de prueba dio idéntica en todos los consumibles). Vale también para el jefe solo y para `/use` afuera. La prueba `stage2btest` se actualizó (el Cordero y el Asado Completo curan lo mismo: desempata el precio).
- **Pendiente que salió de la medición**: de la zona 4 en adelante el golpe del jefe (~120-270 de la zona 3 a la 5) es más grande que cualquier comida común (la mayor cura 150), así que comer una comida común contra el jefe ahí no sirve; los banquetes de 150/200 tampoco alcanzan a compensar un golpe. Si se quiere que curarse tenga sentido en esas zonas hace falta una comida más grande para las zonas altas (idea, no pedida).

## Los banquetes en el jefe en equipo (v0.19.1, 2026-10-20) y la referencia del jefe de la Zona 2 de a dos

- **El problema** (lo reportó el dueño en producción, con capturas): en el jefe en equipo no dejaba comer el Asado Completo del Domingo en Familia ni el Mate Dulce de la Abuela (los «banquetes»). Era a propósito de mi parte en la v0.19.0 («su gracia es el buff de ataque, se comen antes de la pelea») y estaba mal: él los usa en plena pelea. **Arreglado**: ahora curan Y dan su +15 % de ataque en el acto (se recalcula el ataque sin apilar), ocupan la comida de la pelea, valen con la vida llena y el buff queda guardado unos minutos como en el jefe solo. El menú privado de 🍖 Comer los lista (con la vida llena, solo ellos).
- **¿Cuánto tienen que curar los banquetes en la pelea? Medido (2026-10-20, `tools/balance2 -- K 3,8 300 1.0 <modo>`, 2 jugadores, equipo propio, % de victoria a nivel de llegada, zonas 2 / 3 / 4 / 5)**: el dueño dudó si 250-300 HP era demasiado («¿con la de 150 ya basta? ¿200 en vez de 300?»). Hoy: Asado Completo 250 HP y Mate Dulce 300 HP, los dos +15 % ATQ 30 min; la comida común más grande (Cordero) cura 150. Resultados: **sin banquete (Cordero)** 67 / 61 / 45 / 29 %; **banquete comido ANTES de la pelea + Cordero en la pelea** 87 / 83 / 61 / 52 %; **banquete en la pelea que cura 150** 83 / 73 / 67 / 65 %; **200** 81 / 87 / 82 / 75 %; **250** 80 / 93 / 92 / 80 %; **300** 82 / 90 / 91 / 90 %; **banquete «solo buff» comido en la pelea (no cura)** 23 / 27 / 26 / 32 % (una trampa: peor que no comer). Lecturas: (1) el +15 % ATQ por sí solo vale +15-35 puntos (150 con buff contra Cordero) y comerlo antes de la pelea ya es gratis hoy; (2) el tamaño de la curación casi no importa en las zonas 2 y 3 (82 contra 83 con 150) pero SÍ desde la 4, porque el golpe del jefe (~120-270 de la zona 3 a la 5) es más grande que los 150 del Cordero y ahí comer una comida común no sirve; con 300 todas las zonas quedan en ~90 % y pierden el filo; (3) con 200 queda ~75-87 % en todas, que es lo que se quiere («justito»). **Propuesta mía (esperando su OK)**: bajar los banquetes a 200 HP (Asado) y 250 HP (Mate Dulce) o los dos a 200; es un `UPDATE` de datos en `items.stat_value` (más el seed), vale también para el jefe solo y para `/use` afuera.
- **Referencia que le gusta al dueño (anotada a pedido: «el boss zona 2 de a 2 está complicado, anotá las stats que cuesta ese boss, me gusta»)**: **Lobisón Alfa** (Zona 2, Bosque de Cenizas). Guardado en la base: vida 624-794 (promedio ~709), daño 71-96 (promedio ~83; incluye el +17 del amuleto nuevo). En el jefe EN EQUIPO (zona 2 = posición 2: vida ×1,2 y daño ×1,1, más +50 % de vida por jugador extra, `RaidDifficulty`): **2 jugadores = vida 1123-1429 (promedio ~1276), daño ~78-106**; 3 jugadores ~1700 de vida; 4 jugadores ~2126. Medido con `tools/balance2 -- K` (equipo propio de la zona 2: arma de clase +20 con sinergia ×1,5 y el amuleto de 35): a nivel de llegada (nivel 8) 2 jugadores ganan ~21 % sin comida y ~71 % con una comida por jugador; a nivel 13 (mín+8) ~94 % sin comida y ~100 % con una. Su experiencia real: llevó a un amigo de la zona 2 a la 3 (nivel ~13-14, equipo de la zona 2) y ganó «a duras penas», y eso es lo que le gusta: de a dos con el equipo de la zona se siente difícil. **Usar de objetivo al calibrar el jefe de las otras zonas**: que un dúo con el equipo propio gane «justito», no con holgura. (Los números de las otras zonas, con la misma vida: ver las tablas de abajo.)

## El jefe con compañeros (v0.19.0, 2026-10-19): `/boss @amigo` reemplaza al `/raid`

- **Qué es** (el dueño: «en vez de raid podemos dejar que boss sea solo o con un parámetro… que se respeten los turnos, pega uno y el boss le pega a él, luego con el otro lo mismo… tiene que haber un rechazo si la zona máxima del jugador es diferente que la del otro»): `/boss` sigue siendo la pelea de siempre, pero nombrando de 1 a 5 compañeros (`companero` … `companero5`; por texto `aa boss @a @b`) se arma un grupo que pega **por turnos**. El grupo se arma por **invitación** (solo los nombrados aceptan, con botones Aceptar / Rechazar; quien lo armó puede Empezar ya o Cancelar), arranca solo cuando contestaron todos o a los 120 s con los que aceptaron, y se cancela sin gastar el cooldown de nadie si no llegan a dos.
- **El rechazo por zona máxima**: antes de crear nada se mira la zona máxima desbloqueada (`ZoneRanking.MaxUnlockedRank`, la «máx. Zona N» del perfil) de cada compañero contra la de quien arma el grupo; si no coincide, se rechaza todo el pedido diciendo quién y las dos zonas. También se vuelve a mirar al apretar Aceptar. Junto con eso se rechaza: sin cuenta (nunca se le crea una a un invitado), otro combate en curso, cooldown del jefe corriendo, sin vida, sin el nivel que pide el jefe, en El Fogón (el Asador se pelea solo), bots y uno mismo; todos los problemas se listan juntos.
- **Turnos estrictos**: el orden es el de las menciones (primero quien arma el grupo). Un clic fuera de turno no cambia nada y dice a quién le toca; al jugar, el jefe le responde a ESE jugador y pasa el siguiente en pie (los caídos y los que se fueron se saltean). La habilidad en enfriamiento se rechaza sin gastar el turno; huir se puede siempre.
- **Reloj de turnos (60 s)**: el primero vencido hace perder el turno (el jefe no pega), el segundo seguido retira al jugador como si huyera; jugar lo perdona. Cierra el viejo hueco «un raid activo no tiene tiempo máximo de inactividad». Si queda uno solo en pie y se va, el combate termina como abandonado.
- **Arreglo de paso**: cuando el último en pie huye (o se retira por el reloj), los caídos ahora pagan la penalidad y guardan su vida (antes no pagaban nada en ese caso).
- **Cambios de nombre**: el slash `/raid` desapareció (`aa raid` queda solo para avisar el cambio); `/cd` y el recordatorio dicen «Jefe» en vez de «Jefe / Raid»; la ayuda (`/info tema:boss`) explica el grupo. Internamente sigue llamándose `RaidModule`/`RaidSession` (ids de botones `raid_*`, evento `raid_win`).
- **Dificultad: SIN el ×0,75 (decisión del dueño, 2026-10-19)**: «no quiero que baje la dificultad… dejá sin el 0,75, igual que antes con 50 % más por jugador, y que suba el % de victoria por jugador extra: es promover el boss en equipo». Se deshizo el cambio de la v0.18.6 antes de que saliera a producción (los multiplicadores de vida vuelven a 1,76 y 1,2). Medido (`tools/balance2 -- K 3,8 400 1.3333`, equipo propio, SIN comida porque el jefe en equipo no deja comer, zonas 2 a 5): a nivel de llegada **1 jugador 0-1 %, 2 jugadores 21-29 %, 3 jugadores 40-55 %, 4 jugadores 45-59 %**; a mín+8, 2 jugadores 74-94 %, 3 jugadores 90-100 %, 4 jugadores 96-100 % (la victoria sube con cada jugador, como se quería). **Pero ojo**: el dueño dijo «pensá con comida» (solo se gana con una comida, 2-3 turnos más) y el jefe EN EQUIPO NO deja comer (la curación a mitad de pelea no existe en el grupo): el jefe solo gana ~79 / 79 / 70 / 45 % a nivel de llegada, así que un dúo sin comida (21-29 %) es peor apuesta que ir solo y un cuarteto (45-59 %) apenas lo iguala. **Medido con UNA comida por jugador** (`… 1.3333 heal`, no implementado): llegada 2 jugadores 30-71 %, 3 jugadores 51-89 %, 4 jugadores 57-92 % (el trío supera al jefe solo en las zonas 2, 3 y 5); mín+8, 2 jugadores 72-100 %, 3 jugadores 90-100 %. **HECHO en la v0.19.0 (a pedido del dueño: «bloquear /use si estás en boss con compañeros y poner el usar comida 1 por turno por jugador»)**: en el jefe en equipo se puede COMER una vez por jugador (`RaidSettings.MealsPerFight`), con el botón 🍖 Comer (abre en privado la lista de TU mochila; sin banquetes) o con `/use` / `aa use`, que ahí hace lo mismo; comer cura la vida de la pelea, **gasta el turno** (el jefe contraataca) y cuenta para el enfriamiento de la habilidad. Mientras el grupo está PELEANDO, `/heal` y la posada (comer) quedan bloqueados, como en el combate solo; en la sala de espera de las invitaciones todo anda normal. Antes `/use` ahí gastaba la comida y curaba la vida de la base, no la de la pelea. Interpreté «1 por turno por jugador» como una por pelea, igual que `/travel` y `/boss` («es 1 vez sola por pelea», v0.16.1) y como se midió; si se quiere más, es la constante `MealsPerFight`. Otras palancas para «promover» que NO se hicieron: un bono de oro/EXP por compañero.
- **¿De a 4 es muy fácil? Medido con el ×0,75 de la v0.18.6 (que después se deshizo; 2026-10-19, `tools/balance2 -- K`, equipo propio, SIN comida, 300 por celda; +50 % de vida por jugador extra)**: a nivel de llegada (mín+3) en las zonas 2–5 ganan 2 jugadores 56–75 %, 3 jugadores 84–91 % y 4 jugadores 90–96 %; desde mín+8, 2 jugadores ya ganan 98–100 % y 3 o más 100 %. O sea, de a 4 se pierde 4–10 % a nivel de llegada y casi nunca después (pero el jefe SOLO, con comida, ya da 73–100 % desde mín+8: la diferencia grande está en el nivel de llegada). Porque la vida del jefe crece menos (×2,5 con 4) de lo que crece el daño del grupo (×4). **Subirla más pega primero a los dúos**: pasar a +75 % por jugador extra (factores ×1,17 / ×1,25 / ×1,3 para 2 / 3 / 4) deja a 2 jugadores en 33–51 % a nivel de llegada (peor que el jefe solo con comida, ~45–79 %: rompe «de a 2 más fácil que solo») y a 4 en 58–83 %; desde mín+8, 4 jugadores seguirían en 99–100 %. **Recomendación: dejarlo** (recompensa a quien trae gente; el tope real es el cooldown de 5 h compartido, así que cada jugador cobra a lo sumo un jefe cada 5 h solo o en grupo). Si igual se quiere frenar, la palanca fina es escalar distinto desde el 3.º jugador (por ejemplo +50 % hasta 2 y +75 % del 3.º en adelante), dos constantes en `RaidDifficulty.BossHp`. **Ojo con lo que sí es un hueco**: pegarle UN golpe al jefe alcanza para cobrar y, la primera vez, abrir la zona siguiente, así que un grupo bueno puede «llevar» a un amigo sin equipo por la puerta de la zona (pasaba igual con el raid viejo); si molesta, el arreglo es pedir un daño mínimo (un % de la vida del jefe) para contar como «contribuyó».
- **Probado**: `tests/partybosstest` (rechazos con su texto, botones, turnos, reloj con tiempos cortos, victoria, registro real de comandos). **Sin migración.**
- **Aviso de turno (también v0.19.0, a pedido del dueño)**: cada vez que cambia el turno el bot manda un mensaje corto con la mención («🎯 @fulano ¡es tu turno!») y borra el anterior, así en el canal queda uno solo; al terminar el combate se borra el último; el único que queda en pie no se avisa a sí mismo. Es un mensaje aparte porque editar el embed no notifica a nadie (`ICombatReplyTarget.SendPingAsync`). Nunca rompe el combate si falla.
- **No construido (por si lo piden)**: que la invitación de un grupo ya armado admita sumar a alguien a mitad de pelea, y una zona máxima flexible (por ejemplo ±1) si quieren juntar a un amigo que va un paso adelante.

## Informe de salud de la base viva: RESULTADO (corrido el 2026-10-20 en Railway, el dueño pegó la salida)

6 jugadores en total (3 activos en 24 h, los 6 en 7 días, 0 inactivos): es el grupo de prueba, así que sirve de pulso, no de estadística. Niveles 1-4 / 5-9 / 10-14 / 15-19 (×2, los dos con Fuego Nuevo 2) / 20-24; ninguno venció al Asador en esta vuelta; el que más llegó venció al jefe de la zona 4.

- **EXP ×0,5 (v0.18.1)**: se queda. Las subidas de nivel por jugador activo por día se mantuvieron en ~6 (6,5 antes del 16/10, 5,5-7,5 después) mientras las cacerías por jugador activo subieron de ~37 a ~60-80 al día: la gente juega más y el ritmo de nivel no cambió, y el jugador de punta (nivel 20-24, zona 4) no se adelantó al equipo. Nada que retocar.
- **Derrotas**: jefe 8,8 % (52 victorias, 5 derrotas; el diseño decía 2-13 %), cacería 0,2 %, viaje 0 %, raid 18 % (9 y 2: muestra chica y era el raid viejo). Sano.
- **Daily**: todos los activos reclaman casi todos los días (2-5 al día), dos jugadores con racha de 8-13 y uno con 3-5. El oro en circulación es poco (77 k en billeteras + 188 k en bancos, mediana 8,4 k, máximo 34 k): el daily no está inflando nada. Se queda. **A mirar**: el logro «Racha» marca 0 en el tramo I y mejor racha 3 aunque dos jugadores tienen 8-13: es lo esperado (el contador empezó con la v0.18.3, el 19/10, después de que reclamaran ese día); cuando esos dos reclamen de nuevo tendrían que desbloquear el tramo I (7 días): si no pasa, es un bug.
- **Voto**: 3 personas votan cada 12 h (3-6 votos cobrados al día) y 2 de cada 3 veces el voto reseteaba un jefe que estaba en cooldown, así que los jefes ganados por día (4-10) vienen en buena parte de ahí. **Es lo que se había previsto (duplica los cofres gratis)** y con 3 votantes no preocupa; con el bot público conviene revisarlo (palancas: cobrar el reseteo una vez por día o cambiar el cofre).
- **Cajas**: 243 cajas abiertas en 7 días (Arcón de Hierro 78, Baúl de Roble 75, Cajón de Pino 55, Cofre de Oro 29, Cofre de Escoria 2, Brasero 2, Arca del Soberano 2): unas 7 por jugador activo por día, más de lo que se estimó. No se sabe de dónde salen los 29 Cofres de Oro (sospecha: los primeros cofres de cada vuelta del Fuego Nuevo y los premios de logros/semanales); si se quiere saber hace falta una consulta que las atribuya por origen. No se tocó nada.
- **Un detalle del informe**: en PowerShell las tildes salen rotas en pantalla (`├║`); no es un problema de los datos. Antes de correrlo: `[Console]::OutputEncoding = [Text.Encoding]::UTF8`.

## Informe de salud de la base viva (2026-10-19)

`Database/report_live_health.sql` (solo lectura, todo agregado: sin ids ni nombres, se puede pegar en un chat): jugadores y actividad, niveles, hasta qué zona llegaron, el ritmo de los últimos 8 días (peleas, subidas de nivel, derrotas), % de derrotas por tipo, el daily (reclamos por día, rachas actuales, el logro Racha), los votos, las cajas abiertas, el oro y el Polvo en circulación y las vueltas de Fuego Nuevo. Sirve para decidir con datos si hay que retocar la EXP a la mitad (v0.18.1) o el daily nuevo (v0.18.2) en vez de a ojo; correrlo cada tanto en Railway y comparar. Se probó contra la base local (las 12 consultas andan).

## Los raids, medidos (2026-10-19): a nivel de llegada eran MÁS duros que pelear solo — el ×0,75 de la v0.18.6 se DESHIZO en la v0.19.0 (ver arriba)

- **Aplicado** (el dueño: «bajale a 75, la idea del raid es que de a 2 sea más fácil que solo»): los multiplicadores de VIDA de `GameData/RaidDifficulty.cs` pasaron de 1,76 y 1,2 a **1,32 y 0,9** (el daño no se tocó). Con el código nuevo, medido: a nivel de llegada **2 jugadores 66-72 %** (zona 1: 99 %), **3 jugadores 84-92 %**, **4 jugadores 87-98 %**, y a mín+8 todo ≥ 98 % con 2 o más. **Ojo con la intención**: con ×0,75 los 2 jugadores igualan o superan al jefe solo (con comida y habilidad) en las zonas 4 y 5 pero quedan un poco debajo en las 2 y 3 (66-70 % contra 79 %); con ×0,65 quedarían por encima en las cuatro (76 / 89 / 90 / 92 %). Si se quiere «siempre más fácil de a dos», son las mismas dos constantes (y `tools/balance2 -- K 3 400 0.65` para probar). Arneses `bosshptest` y `leaktest` actualizados a los números nuevos.

Pendiente desde la v0.15.1 («los raids no se re-midieron después de los amuletos nuevos»). Se midió con el combate real (`tools/balance2`, parte nueva **K**: `dotnet run --project tools/balance2 -- K`, o `-- K 3,8 400 0.75` para probar otra vida del jefe), equipo propio de la zona, **sin comida** (el raid no tiene curación), grupos de Guerrero, Hechicero, Arquero y Ninja, 400 raids por celda. Lo que corría hasta la v0.18.5 daba lo mismo que en la v0.17.1, o sea nada se había movido (tabla de ANTES del ×0,75):

| % de victoria | nivel mín+3 (llegada): 1 / 2 / 3 / 4 jugadores | nivel mín+8: 1 / 2 / 3 / 4 |
|---|---|---|
| Zona 2 (Lobisón Alfa) | 2 / 18 / 42 / 44 | 56 / 94 / 99 / 100 |
| Zona 3 (Capataz de Hierro) | 2 / 23 / 48 / 55 | 41 / 85 / 97 / 100 |
| Zona 4 (Señor del Volcán) | 1 / 26 / 50 / 62 | 27 / 81 / 96 / 98 |
| Zona 5 (Soberano de la Escoria) | 0 / 28 / 49 / 59 | 14 / 73 / 91 / 97 |
| Zona 1 (Rey Jabalí) | 54 / 83 / 94 / 97 | 100 en todo |

- **El hallazgo**: a nivel de llegada (mín+3), el jefe SOLO (con una comida y la habilidad) se gana ~79 / 79 / 70 / 45 % en las zonas 2 a 5, y el raid de **2 jugadores solo 18-28 %** y el de 4 apenas 44-62 %. Es decir, **en ese nivel el raid es la forma más difícil de pelear el jefe**, y recién a partir de mín+5 o 6 se vuelve la más fácil (a mín+8 con 2 jugadores gana 73-94 %). Los dos premian igual (cada participante cobra el jefe entero), y un raid perdido cuesta la penalidad por morir a CADA uno, así que nadie tiene motivos para elegirlo al llegar. En v0.17.1 el dueño pidió explícitamente NO endurecer los raids con el +25 % de vida (se compensó dividiendo los multiplicadores); no pidió ablandarlos.
- **Opción medida** (si se quiere que 2 jugadores equivalgan más o menos a pelear solo y que sumar gente se note): multiplicar la vida del jefe del raid por **0,75** (los multiplicadores de `GameData/RaidDifficulty.cs` pasarían de 1,76 y 1,2 a ~1,32 y 0,9). Da a nivel de llegada **2 jugadores 66-71 %, 3 jugadores 83-92 %, 4 jugadores 85-98 %** (un jugador solo en un raid, que por defecto no se puede, ~20 %), y a mín+8 todo ≥ 99 % con 2 o más. Con **0,6** ya es demasiado fácil (2 jugadores 88-95 % en la llegada). Fue lo que se aplicó (ver arriba).
- **Para hacerlo**: tocar solo los dos multiplicadores de vida de `RaidDifficulty.cs` (daño igual), volver a correr la parte K y los arneses `raidtest`/`bosshptest`, y anotarlo acá y en CLAUDE.md. Sin migración.

## El arte que faltaba: cofres, huevos, comida de mascotas y el Tabernero (v0.18.5, 2026-10-19)

- El dueño subió al portal todo lo que faltaba y pasó los códigos: **Cofre de Escoria** (`cofre_escoria`), **Brasero del Fogón** (`brasero`), los **6 huevos** (`huevo_1` a `huevo_6`), la **Comida para Mascotas** (`comida_pets`) y la foto del **Tabernero del Inframundo** (`tabernero_deep`). Los 9 ítems están en `Database/update_item_emojis.sql` (por nombre, re-ejecutable); con eso **todo ítem que se puede conseguir tiene arte** (`apply_emojis.sql` ya no lista nada, y los monstruos ya tenían cara).
- **Ojo, una suposición**: los huevos están asignados en el orden de las zonas (`huevo_1` = Pradera, 2 = Bosque de Cenizas, 3 = Minas, 4 = Cordillera, 5 = Cráter, 6 = Fogón), que es el orden en que se armaron los prompts; si alguno quedó cruzado, se cambia el nombre del ítem en ese `UPDATE` y se vuelve a correr el script.
- **El Tabernero del Inframundo** usa su emoji como foto: `NpcImages.DeepShopkeeper` (variable → archivo → el CDN del emoji `tabernero_deep`). Por eso ya no hace falta alojar un `deepshop.jpg`.
- **Para la base viva**: correr `Database/apply_emojis.sql` (solo datos, re-ejecutable, sin redesplegar; algunas pantallas tardan hasta 5 minutos por la caché). La foto del Tabernero sí necesita el binario nuevo.

## La raid cancelada ya no gasta el cooldown del jefe y /cd muestra las mascotas (v0.18.4, 2026-10-19)

- **Raid cancelada** (el bug que reportó Adiofh en #sugerencias, ver abajo): `RaidModule.TryActivateAsync` reclama el cooldown del jefe de cada participante ANTES de saber si el raid va a arrancar y no lo soltaba si el raid se cancelaba (no se juntaban los mínimos porque alguien tenía el cooldown ocupado) ni si la activación explotaba. Ahora `IAdventureRepository.ReleaseCooldownAsync(discordId, commandName, duration)` (un `DELETE` que solo borra la fila si el cooldown SIGUE corriendo: uno ya vencido no se toca, y soltar dos veces no hace nada) se llama para los que esa activación reclamó, **antes** de editar el mensaje (si la edición falla, igual quedan libres) tanto al cancelar por falta de gente como en el `catch`. Si soltar falla queda en el log y se sigue con el resto. El mensaje de cancelación dice «No se gastó el cooldown de Jefe de nadie.» Un raid que SÍ arranca no cambia (los que pelean tienen la espera corta de 30 min). Probado con el servicio de sesiones y el repositorio reales: el caso del reporte (2 anotados, uno ocupado → el que armó queda libre y puede pelear solo en el acto), tres anotados con uno ocupado (arranca con los dos libres), activación que explota, y soltar que falla.
- **`/cd` con las mascotas** (sugerencia de Adiofh): una línea «🐾 **Mascotas**: **¡Listo!** ✅ — 2 pueden comer (/pet feed)» o «🐾 **Mascotas**: 23m 10s restantes (la primera)», con la espera propia del jugador (la del Buen Pienso, `PlayerBonuses.PetFeedCooldown`), antes de la línea del Diario. No sale si no tenés mascotas o todas están al nivel máximo, ni si leerlas falla (`/cd` nunca se rompe por esto); la cuenta es pura (`CooldownModule.PetLine`) y la usan `/cd` y `aa cd`. **Ojo con la descripción del comando**: al nombrar las mascotas llegó a 103 caracteres (el tope de Discord es 100, y pasarse rompe el registro de TODOS los comandos); quedó en 97 sacando «raid» (jefe y raid comparten el cooldown y la línea dice «Jefe / Raid») — lo cazó el arnés. Sin migración.
- **Arnés nuevo**: `tests/v0184test` (la raid y el `/cd`; sin el arreglo fallan 4 chequeos). 41 arneses en total.
- **Para la base viva**: nada (sin migración, solo el binario).

## Sugerencias de los jugadores que prueban el bot (canal #sugerencias, anotadas el 2026-10-19)

Cuatro publicaciones del foro (Adiofh, Picopro100 y PolarFrost). Orden: lo que conviene hacer primero, y lo grande que necesita diseño antes. **Las dos primeras se hicieron en la v0.18.4 (arriba); la 3 y la 4 siguen siendo diseño.**

1. **[HECHO en la v0.18.4] Una raid cancelada deja el cooldown del jefe ocupado — BUG REAL, confirmado en el código** (Adiofh: «intenté hacer una raid con Picopro100, él no pudo entrar y la raid se canceló. Sin embargo, al intentar hacer la boss fight yo solo, noté que tenía el cooldown activo de la raid»).
   - **Causa**: `RaidModule.TryActivateAsync` reclama el cooldown del jefe de CADA participante (`TryClaimCooldownAsync`, que deja la espera corta de 30 min de una derrota) ANTES de saber si el raid va a arrancar. Si después `confirmed.Count < MinParticipantsToStart` (alguien tenía el cooldown ocupado y salió, o no se juntaron los mínimos) el raid se cancela con «No se juntaron los N jugadores mínimos», pero esos cooldowns ya reclamados **nunca se sueltan**. Lo mismo si la activación explota a mitad de camino (el `catch` cierra el raid y suelta a los jugadores del índice, pero no sus cooldowns). Resultado: quien armó el raid pierde 30 minutos de `/boss` y `/raid` por un raid que nunca peleó.
   - **Arreglo propuesto**: un `IAdventureRepository.ReleaseCooldownAsync(discordId, commandName)` (un `DELETE` guardado de la fila que se acaba de reclamar) y llamarlo para los `confirmed` en la rama de cancelación y para todos los reclamados en el `catch`; con una prueba que arme un raid de 2 donde uno tiene el cooldown ocupado y verifique que el otro queda LIBRE (`raidtest`/arnés nuevo). Ojo: un `DELETE` es seguro porque la fila reclamada es el único estado vigente (un jefe en curso no puede reclamar otra vez), pero el cooldown que tenía ANTES (ya vencido) tampoco hace falta restaurarlo.
2. **[HECHO en la v0.18.4] Mostrar en `/cd` cuándo se puede alimentar a las mascotas** (Adiofh: hoy hay que abrir `/pet view`). Ya existe todo: `ReminderCatalog.PetDue(owned, cooldown, now)` calcula cuándo puede comer la PRIMERA mascota que todavía puede subir de nivel (con el Buen Pienso propio del jugador, `PlayerBonuses.PetFeedCooldown`) y el aviso de recordatorios ya lo usa. Falta una línea en `CooldownModule.BuildStatusEmbedAsync` («🐾 **Mascotas**: ¡Listas! ✅» o «la primera en 23m 10s»), que solo salga si el jugador tiene mascotas que aún pueden subir (como las versiones avanzadas de `/chop` y `/mine`). Sin migración; hay que inyectar `IPetRepository` en `CooldownModule` y pasarlo por `aa cd` también (mismo método estático) y probarlo en un arnés.
3. **Aprender o mejorar habilidades (y «nerfear las iniciales») — viable pero GRANDE: hace falta diseñarlo con el dueño antes** (Picopro100, apoyado por PolarFrost: «y si hay alguna forma de aprender nuevas habilidades para tu clase o mejorar la que tenés o evolucionarla? con esto yo diría también nerfear las iniciales para que no sea tan OP»).
   - A favor: le da algo que hacer al juego largo (hoy cada clase tiene UNA habilidad fija, `GameData/ClassAbilities.cs`, con todos los números en `AbilityTuning`) y encaja con las bendiciones y el Fuego Nuevo.
   - Cuidado: (a) **las habilidades están calibradas** (`abilitytest` mira las distribuciones, `PveTuning` y `PvpTuning` se midieron CON ellas): subir o bajar una cambia el balance entre clases y los jefes (volver a medir con `tools/balance2` y el doble bucle de `DuelEngine.Simulate`); (b) **no tocar «las iniciales» a ciegas**: «OP» depende de la clase (en PvE el Arquero es la débil y el Guerrero la mejor con el equipo de la escalera, medido el 2026-10-09; en PvP ya hay factores por clase); habría que preguntarles a esos jugadores CUÁL clase les parece pasada antes de nerfear nada; (c) **no debe contar en duelos ni en la Arena** (el principio de pets, bendiciones y oficios: nada permanente pesa contra otros jugadores).
   - Una forma que cuadraría con lo que ya hay: rangos de la habilidad (I a III) que se compran con Polvo o se eligen como una bendición de las del Fuego Nuevo (+x % de efecto, −1 turno de cooldown al máximo), solo contra monstruos, con la clase de siempre. Hacer un diseño corto (brainstorming) y recién después una versión.
4. **Una clase nueva, el Monje — ya contestada por el dueño en el foro («Listed. For now I don't wanna implement more champs but for the future it's a really good idea»); guardada como idea de hoja de ruta, NO es para ahora** (PolarFrost: el Monje usa la defensa como daño pero solo en su habilidad y, si lleva arma, no puede usarla; la especial devuelve la fuerza del enemigo más la defensa del Monje; pasiva: sin armas puede usar la especial y sus ataques normales pegan 10 % más).
   - Por qué es cara: el juego es una escalera de EQUIPO (30 recetas: 4 armas de clase por zona, una por familia, +1 general + 1 amuleto) y el Monje no usaría arma → hace falta una 5.ª familia de arma (guantes/puños) con sus recetas en las 5 zonas y el Fogón, o una regla nueva de «sin arma» que no lo deje sin progresión (los amuletos dan 35-165 de DEF, contra armas de hasta 128 de ATQ: con la defensa como daño el amuleto pasaría a ser su pieza principal y se rompe la calibración de la escalera).
   - «Devolver el daño del enemigo» (reflejo) no existe en el resolutor único de turnos (`CombatTurnResolver`, que también usan los duelos y `/autohunt`): hay que sumarlo ahí y no copiarlo en un módulo. Además: `PveTuning` y `PvpTuning` (un factor de HP más), la elección de clase del Fuego Nuevo, la historia y la ayuda (`/info tema:classes`), y volver a medir los 5 jefes y los duelos. Una versión entera de trabajo.
   - Si algún día se hace: empezar por medir con `tools/balance2` si la defensa-como-daño de la especial alcanza para ser competitiva sin arma, y recién ahí decidir la regla de arma.

Para ordenar el trabajo: 1 y 2 entran juntos en una versión chica (los dos sin migración); 3 y 4 son diseño.

## Las pruebas pasan al repo y el logro «Racha» (v0.18.3, 2026-10-16)

- **Las pruebas viven en el repo** (el dueño: «dale, vamos 1 y 2»): los ~40 arneses de verificación que se citan por nombre en todo `CLAUDE.md` estaban en una carpeta temporal fuera del repo, y «cero tests» figuraba como deuda desde siempre. Ahora son **`tests/<nombre>/`** (un `csproj` mínimo + `Program.cs`, que usa el bot real por `ProjectReference`, sin Discord), con lo común en `tests/Directory.Build.props` y la cadena de conexión / `RepoRoot` en `tests/Shared/TestEnv.cs` (sin rutas absolutas), y el simulador de calibración como **`tools/balance2`** (partes A–J, incluida la J: cada jefe contra el equipo de la zona anterior). Se corren con **`tests/run-all.ps1`** (o `tests/run-all.sh`; `-Only nombre1,nombre2` para algunos) y dejan `tests/.logs/` (ignorado por git). Detalle y cómo sumar uno: `tests/README.md`.
- **Se niegan a correr contra una base que no sea local** (`TestEnv`, salvo `HARNESS_ALLOW_REMOTE=1`): escriben y borran datos y algunos tocan un instante el catálogo o `cooldowns`. El csproj del bot excluye `tests/**` y `tools/**` de su compilación, `.dockerignore` los saca de la imagen, y `dotnet publish` del bot no cambió (se comprobó). Hubo que ajustar dos arneses (`stage6test`, `stage10test`) porque ahora las fotos de `Assets/npc` llegan a su carpeta de salida y ellos prueban primero el caso SIN foto, y tres (`choicetest`, `leaktest`, `raidtest`) por la ruta del dll. Sigue sin haber CI (necesitan una base local con la instalación completa).
- **El logro «Racha»** (página Constancia): cuenta la racha **más larga** del `/daily`, con tramos en **7, 30 y 100 días** y los premios de los logros estándar (oro y EXP; el tramo II y el III dan caja de tu zona). Los contadores solo suman, así que al reclamar se registra la **diferencia** con el récord (`GameEventExtensions.RecordDailyStreakAsync`: nada si no lo superó, así que perder la racha no baja nada ni reclamar dos veces no suma). Quien ya traía una racha larga la cobra completa en su próximo `/daily` (un jugador en el día 9 desbloquea el tramo I). Sin migración. Total: 23 logros.
- **Para la base viva**: nada (sin migración, solo el binario).

## El daily: escala con la zona y el nivel, hitos de racha y cajas (v0.18.2, 2026-10-16)

- **Qué pasaba** (el dueño, en FN 2 y día 8 de racha: «el daily es muy poco, hay que mejorar mucho»): pagaba un monto fijo, 100 de oro y 50 de EXP por día de racha (tope en el día 10: 1.000 y 500), sin mirar la zona ni el nivel. En la zona 5 una cacería sola paga 375, así que el tope eran ~2 cacerías (1-2 minutos de juego), y 500 de EXP es ~1 % de un nivel a nivel 60. (El dueño leyó 800 de EXP el día 8: el código daba 400.)
- **Qué es ahora**: el premio del día es un `RewardSpec` (la misma unidad que las misiones y los logros): **oro en «cacerías de oro» de tu zona** y **EXP como % del nivel que estás subiendo** (así no lo toca la EXP de peleas partida a la mitad). Del día 1 al 7: **oro 6 / 8 / 10 / 12 / 14 / 16 / 20 cacerías** y **EXP 2 / 2 / 3 / 3 / 4 / 4 / 6 %**; del día 8 en adelante queda el valor del día 7. En la zona 5 el día 8 son 7.500 de oro (antes 800); a nivel 60 el día 7 son ~2.800 de EXP (antes 500).
- **Hitos de la racha** (lo pidió el dueño): el **día 3**, +15 cacerías de oro. El **día 6** y **todos los días después del 7** que no caigan en múltiplo de 7: la **caja diaria** de tu zona (Cajón de Pino / Baúl de Roble / Baúl de Roble / Arcón de Hierro / Arcón de Hierro en las zonas 1 a 5; «el de hierro»). El **día 7 y cada 7 días en racha** (14, 21, 28…): la **caja grande** de tu zona (Baúl de Roble / Arcón de Hierro / Arcón de Hierro / Cofre de Oro / Cofre de Oro; «el de oro») y +30 cacerías de oro. Del día 1 al 5 no hay caja. Las reglas de la racha no cambian (24 h de espera, 48 h para no perderla, el Fuego Nuevo no la borra). Las cajas salen de la tabla `zone_boxes` (roles `daily` y `weekly`, las mismas de las misiones), así que se retocan por datos.
- **Todo en UNA transacción** (`RewardPayer`, el mismo de misiones y logros): oro, EXP con subida de nivel, caja y racha; si la caja del día no existe se revierte TODO, racha incluida; 12 `/daily` a la vez pagan una sola vez. La zona que cuenta es la actual del jugador (como en las misiones).
- **Economía, medida**: en régimen, quien reclama todos los días suma por semana 6 cajas diarias + 1 grande + 170 cacerías de oro = **~15k / 102k / 112k / 506k / 533k** de oro-equivalente en las zonas 1 a 5, o sea **~2× lo gratis en cajas que ya había** (13k / 100k / 100k / 512k / 512k), y vale 4,5 / 7,3 / 4,0 / 9,8 / 5,9 sesiones perfectas de 2 h por semana. El dueño lo eligió sabiendo eso (se le propuso la caja diaria solo la primera semana y la grande cada 7 días). **Palancas si resulta mucho**: sacar la caja diaria de después del día 7 (`DailyRewardCalculator.BoxFor`), bajar las unidades de oro, o cambiar las filas `daily`/`weekly` de `zone_boxes`.
- **Logros de racha**: se hicieron en la v0.18.3 (el logro «Racha», arriba); los hitos de cada día siguen viviendo en el propio reclamo y se ven en el mensaje.
- **Para la base viva**: nada (sin migración, solo el binario).


## La EXP de las peleas, a la mitad (v0.18.1, 2026-10-16)

- **Qué pasó** (el dueño: «el boss de la zona 3 para pasar a la 4 está muy fácil con el equipo de zona 2, capaz subirle la vida o el daño»; después: «puede ser que se suba de nivel muy rápido»; al ver los números: «0,33 es una banda, dejémoslo en 0,5»): se midió con el combate real (balance2, parte J, promedio de las 4 clases, con habilidad y una comida) el jefe de la zona 3 (Capataz de Hierro) con el equipo de la zona 2 (arma de clase + amuleto) y **a nivel parejo es una pared**: pierde el 99 % a nivel 10, 95 % a nivel 13, 78 % a 16, 39 % a 20, 4 % a 25 y 0 % desde el 30 (a nivel 60 le cuesta el 8 % de la vida). No es más blando que los otros jefes (con 15 niveles de más, el de la zona 2 pierde 1 % y el de la zona 3, 3 %): el problema es que **se llega con 15 niveles de más**.
- **El nivel le gana al equipo**: en juego perfecto (cacería cada minuto, viaje cada 30 min ×30, jefe cada 5 h ×15, siempre en la zona más alta que deja el nivel; cota optimista, el mismo supuesto de las 31 h del equipo de las cinco zonas) el nivel era **22 a las 6 h, 36 a las 12 h, 49 a las 20 h y 60 a las 31 h**, y la escalera de zonas pide los niveles 1/5/10/15/20 (el Fogón, 25). El nivel dejaba de frenar a las 6-7 h y cualquier jefe se hacía a fuerza de nivel con el equipo viejo.
- **Qué se hizo**: `CombatRewardCalculator.FightXpScale = 0.5`: la EXP de TODA pelea contra un monstruo (`/hunt`, `/travel`, `/boss`, `/raid`, `/autohunt`) vale la mitad, antes de los bonus del jugador (mascotas, Fuego Nuevo, bendiciones). Con el mismo modelo: nivel **13 / 22 / 33 / 43 a las 6 / 12 / 20 / 31 h** (con 0,33 serían 9 / 15 / 25 / 34; con 0,5 a las 6 h, cuando se tiene el equipo de zona 2, se está en nivel 13 y el jefe de la zona 3 pierde el 95 %: hace falta el equipo de la zona 3).
- **Lo que NO cambia**: el oro, los drops y los cofres; los niveles ya ganados (nadie baja: solo la EXP nueva rinde menos); las misiones, los logros, `/daily` y el premio de la Arena, que pagan su EXP aparte sin escalar (pesan relativamente más). **Lo que sí**: llegar al nivel 25 del Fogón pasa de ~7 h a ~14 h de juego perfecto (el equipo del Fogón igual tarda más), y la penalidad por morir (la EXP de TODO el nivel) vale el doble de cacerías (~40 en vez de ~20).
- **Descartado, con números**: subirle vida/daño al jefe. No molesta al que sube de más (con vida ×1,25 y daño ×1,15 sigue perdiendo 0 % desde el nivel 40) y castiga al que lo hizo bien (zona 3 con su equipo propio a nivel 13: de 23 % a 87 % de derrota). **Ofrecido y sin hacer**: que los jefes escalen con el nivel que le sobra al jugador sobre el mínimo de la zona (la palanca real para alguien de nivel 60, pero cambia todos los jefes y los raids).
- **Para la base viva**: nada (sin migración, solo el binario). Las pruebas que miraban cantidades de EXP de una pelea se ajustaron.


## Votar en top.gg: `/vote` (v0.18.0, 2026-10-16)

- **Qué es** (el dueño: «que poniendo `aa vote` te dé un mensajito de lo que te da el vote, que es una caja (Baúl de Roble), y te resetea el boss»): `/vote` (`aa vote`, también `aa votar` y `aa voto`) cuenta qué da votar en top.gg, da el link y, si ya hay un voto vigente sin cobrar, lo **cobra**: **1 Baúl de Roble** y **el cooldown del jefe/raid se borra** (si corría). Se vota cada 12 h; aparece en `/cd` y tiene su aviso en `/reminders`.
- **Cómo sabe que votaste, sin webhook ni puerto**: le pregunta a top.gg `GET /projects/@me/votes/{id}?source=discord` (token del proyecto en `TopGg__Token`; 200 con `created_at`/`expires_at`/`weight`, 404 si nunca votó o venció). Un voto se cobra UNA vez: la fila de cooldown `vote` guarda el `created_at` del último voto cobrado y el cobro es `INSERT … ON CONFLICT DO UPDATE … WHERE last_executed_at < nuevo` en UNA transacción con la caja y el borrado del cooldown del jefe (20 cobros a la vez del mismo voto → exactamente uno paga; si falta la caja en el catálogo se revierte todo). Un voto sin cobrar se pierde cuando vence (~12 h).
- **Dormido hasta que haya un proyecto aprobado en top.gg**: sin `TopGg__Token` (y mientras el proyecto esté «en revisión» no hay página pública donde votar) `/vote` solo explica el premio y `/cd` no muestra la línea. Se prende solo con cargar el token en Railway y reiniciar, sin tocar código.
- **Bot o servidor, el mismo código** (el dueño, con el bot todavía en revisión pero el servidor ya publicado: «¿podemos poner para votar el server de mientras?»): la API v1 de top.gg llama «proyecto» tanto al bot como al servidor (`type`: `bot`, `server` o `game`; la ruta `/projects/@me/votes/{id}` y el token de la sección *Integrations & API* son los mismos), así que alcanza con cargar el token y el link del PROYECTO DEL SERVIDOR; cuando aprueben el bot se cambian esas dos variables. Los textos del comando ya no dicen «el bot» (dicen «top.gg»). **Sin verificar**: la documentación no dice explícitamente que un proyecto de servidor tenga token ni que la consulta de votos ande para ese tipo; se comprueba en un minuto con `GET /projects/@me` (debe contestar `"type": "server"`) y, tras votar, con `/vote`. Ojo: cada proyecto lleva sus propios votos (votar el servidor no cuenta para el bot).
- **Ojo con la economía** (medido con `Database/report_economy.sql`): un Baúl de Roble vale ~8.200 de oro-equivalente; con voto cada 12 h son ~115k a la semana, que **duplica o más lo gratis de las zonas 1 a 3** (13k / 100k / 100k por semana hoy). El reseteo del jefe vale más que la caja en las zonas altas: es una victoria extra, o sea la caja de repetición de la zona (en la 5 un Arcón de Hierro, ~43k); con voto cada 12 h el combo más o menos duplica lo gratis en todas las zonas, y baja el piso de 20,8 h hasta el jefe de la zona 5 a ~11 h (el equipo sigue limitando la vuelta, ~31 h). Si resulta mucho: que el reseteo solo valga una vez por día, o cambiar `VoteRules.RewardBoxName` (un solo lugar).
- **Para la base viva**: nada (sin migración, solo el binario). `/cd` y los avisos usan la tabla `cooldowns`, que ya existe.
- **`/info` achicado** (el dueño: «tenemos que achicarlo, capaz sacar varios comandos y poner lo más importante»): la lista de comandos pasó de 14 campos con una explicación por comando (~5.000 caracteres, se salía de la pantalla) a 8 campos con solo los NOMBRES, una línea por grupo (~1.200). Qué hace cada comando ya lo muestra Discord al elegirlo y el detalle está en `/info tema:<tema>`. Quedaron fuera de la lista (siguen andando) `/shop`, `/zonas`, `/history`, `/duels` y `/blessings`; si se quiere alguno de vuelta es un nombre en el grupo que corresponda (`HelpModule.BuildInfoEmbed`).
- **Sin hacer**: el link de voto lo trae `TopGg__VoteUrl` (sin él el mensaje manda a buscar el bot en top.gg); no se usa el `weight` (los fines de semana top.gg cuenta doble: hoy no cambia el premio); no hay webhook (si algún día se quiere cobrar el voto al instante, sin que escriban `/vote`, hace falta un puerto público en Railway).

## Más vida a los jefes (v0.17.1, 2026-10-14)

- **Qué pasó** (el dueño, a nivel 60: «aumentemos la dificultad de TODOS los jefes un poco, dale más vida a todos. Daño dejalo igual»): los seis jefes (Rey Jabalí, Lobisón Alfa, Capataz de Hierro, Señor del Volcán, Soberano de la Escoria y El Asador Eterno) tienen **+25 % de vida** y el mismo daño. Los monstruos comunes y de viaje no se tocaron.
- **Cuánto cambia el examen** (combate real, 4 clases, equipo propio, habilidad y una comida; % de PERDER): a nivel mínimo+3 pasa de 0 / 5 / 7 / 11 / 24 % (zonas 1 a 5) y 11 % (Asador) a **0 / 22 / 24 / 31 / 55 %** y **35 %**; a mínimo+8, donde suele estar la gente cuando ya tiene el equipo, de 0 / 0 / 0 / 1 / 5 % a 0 / 0 / 2 / 5 / 21 % y 9 % en el Asador; a mínimo+15 todo queda ≤ 1 %.
- **Lo que NO arregla**: con el daño igual, alguien MUY por encima del nivel de la zona no corre riesgo (a nivel 60 el jefe de la zona 5 le cuesta 15 % de la vida en vez de 11 %, el Asador 16 % en vez de 13 %; con el doble de vida, 25 %). Más vida solo alarga la pelea (de ~6 a ~7-8 turnos). Para que un jugador sobrado sienta algo hace falta que el jefe escale con SU nivel, o daño; quedó hablado, no hecho.
- **El raid no se tocó a propósito**: sus multiplicadores de vida se dividieron por 1,25 (2,2 → 1,76 y 1,5 → 1,2), así que el jefe del raid tiene la misma vida que ya estaba medida (con +25 % encima, tres jugadores en las zonas 2 a 5 pasaban de ganar ~49 % a ~16 %). Si el dueño quiere raids más duros, es cambiar esos dos números.
- **Para la base viva**: correr `Database/rebalance_boss_hp.sql` (solo vida, re-ejecutable, se verifica sola). No es previo al binario: el binario nuevo solo cambia el multiplicador de los raids; si uno de los dos va primero, los raids quedan unos minutos más fáciles o más difíciles, nada se rompe. Una instalación nueva ya trae los valores.
## El Tabernero del Inframundo: los Míticos por Polvo (v0.17.0, 2026-10-14)

- **Por qué** (el dueño, tras una vuelta con 5 Meteoritos y 0 Cortezas): el Trinche y la Brasa del Fogón piden UNA Corteza del Árbol de Vida y UN Fragmento de Meteorito cada vuelta (el Fuego Nuevo borra la mochila) y salen al 0,5 % por `/chop` y `/mine`: juntar los dos tarda de media **25 h** de recolección sin parar (mediana 20 h, una de cada diez vueltas pasa de 49 h, una de cada cien de 88 h) y no baja con las vueltas, mientras que el resto de la vuelta sí (~31 h → ~15 h en el Fuego Nuevo 10). Revisé el código y el catálogo: no hay error ni sesgo, un Mítico por tipo y la misma chance (en mi base local salió 0,87 % y 0,68 % con ~300 usos); con ~500 usos de cada uno, 0 árboles pasa el 8 % de las veces y 5 o más meteoritos el 11 %. **No tengo acceso a la base de Railway**; `Database/report_mythic_finds.sql` (solo lectura) deja ver las chances reales por jugador.
- **Qué es**: `/deepshop` (`aa deepshop`, también `aa inframundo`): una escena como la taberna con **El Tabernero del Inframundo**, que vende la **Corteza** y el **Meteorito** a **3.000 de Polvo cada uno** (el dueño: «todo lo que farmeo me da para mucho más; luego vemos si lo dejamos más caro»). Desmantelar un Mítico da 500: nadie puede desmantelar y recomprar en círculo. Lista desplegable (de a una) o `/deepshop item:… cantidad:…` (hasta 10); el Polvo sale de `/dismantle` (que ahora no tiene tope).
- **Cuándo se conoce**: al vencer al jefe de la **última zona** (la misma regla que abre El Fogón); con un Fuego Nuevo hecho, **para siempre**. No pide el equipo del Fogón (ese equipo necesita justo lo que vende). Antes de eso solo dice que no sabés de qué hablan.
- **Su foto** (v0.18.5): es el emoji de la aplicación `tabernero_deep`, y la escena usa la URL de su CDN como miniatura (ya no hay que alojar nada); una variable `Images__DeepShopkeeper` o un `Assets/npc/deepshop.jpg` de 256x256 ganan si existen.
- **Para la base viva**: nada (sin migración, solo el binario; incluye también el `/dismantle` sin tope de la v0.16.2).
- **Pendiente / hablado y no hecho**: dos jefes (Árbol y Meteoro) en la Zona 0 con el drop asegurado la primera vez por vuelta (hace falta cambiar la entrada y darles un cooldown propio: el global de 5 h sumaría 10 h por vuelta); que la chance Mítica suba con el Fuego Nuevo. **Dos cosas que dijo y quedan por aclarar**: los jefes le resultan muy fáciles a nivel 60 (las zonas están calibradas con nivel parecido al mínimo de la zona: a nivel 60 la defensa es 60 + el amuleto, y el Asador pega 233–289), y que los monstruos «siempre pegan 1 de daño mínimo» (`CombatMath.ResolveMonsterHit` nunca baja de 1, ni con la defensa por encima del golpe).
## Desmantelar sin tope (v0.16.2, 2026-10-14)

- **Qué pasaba** (el dueño: «que se pueda desmantelar sin máximo de 100»): `/dismantle` aceptaba de 1 a 100 por comando (`Dismantling.MaxPerCommand`; eran 5 hasta la v0.9.4). Con cientos de Piedra o Pino en la mochila eran decenas de comandos.
- **Qué cambió**: no hay tope. `cantidad` ahora es **texto**: un número (`30`, `1.000`) o **`todo`** (también `all`/`max`) = todas las unidades que tenés de ese ítem. En texto: `aa desmantelar 30 Piedra`, `aa desmantelar todo Piedra` o `aa desmantelar Madera de Pino` (una). La lectura del pedido es pura (`GameData/DismantleRequest.Parse`). El único límite es la mochila: la resta sigue guardada (`WHERE quantity >= n`), «todo» nunca deja la mochila negativa ni paga dos veces (probado con dos pedidos a la vez), y una cantidad absurda no desborda el Polvo (la cuenta es en `long` y se rechaza antes de tocar la base).
- **Ojo**: «todo» es explícito pero no pide confirmación. Los ingredientes de las recetas (sobre todo los del Fogón) se pueden desmantelar igual que antes; `/forge-special` ya avisa que no se hagan.
- **Para la base viva**: nada (sin migración, solo el binario).
## Una sola curación por pelea, también en `/hunt` (v0.16.1, 2026-10-09)

- **Qué pasaba** (lo vio el dueño: «el use comida en plenas peleas ilimitadamente… acordate que es 1 vez sola por pelea»): el límite de una curación valía en `/travel` y `/boss`, pero en `/hunt` el `/use` escrito en plena pelea seguía **sin límite**. Fue decisión mía de la v0.8 (la anoté acá como «decidí sin preguntar»: el pedido era que el desplegable no existiera en `/hunt`, no que se prohibiera curarse) y estaba mal tomada: con una cacería de un minuto de cooldown, comer hasta ganar saltaba el costo de las peleas.
- **Qué cambió**: `GameData/CombatHeal.cs` separa dos cosas que antes eran una (`IsLimited`): `HasMenu` (el desplegable «Curar»: solo `/travel` y `/boss`) e `IsLimited` (el límite de UNA curación por pelea: ahora `/hunt`, `/travel` y `/boss`). `/use` escrito (`aa use`) y el desplegable comparten el mismo límite; la segunda curación se rechaza («Ya te curaste en esta pelea: solo se puede **una vez** por combate») sin gastar comida ni turno, con cualquier comida. La pelea siguiente arranca limpia. El encuentro de `/hunt` lleva el pie «🍖 Podés curarte UNA vez en esta pelea con /use». `/info tema:hunt` lo dice.
- **Sin tocar**: fuera de combate `/use` y `/heal` siguen sin límite (es el ciclo de recursos entre peleas); `/autohunt` no se cura (no tiene turnos del jugador); el raid no tiene curación en la pelea (un `/use` dentro de un raid no toca la vida de la pelea, que vive en memoria).
- **Para la base viva**: nada (sin migración). Se despliega con el binario.
## Recordatorios de cooldown (v0.16.0, 2026-10-09)

- **Qué es** (idea del dueño: «que el bot mismo te lea… y te diga luego de `/hunt` al minuto que la cacería está lista; lo mismo con travel, chop, mine, boss/raid, caja, pet feed»): cuando termina una espera, el bot manda un mensaje en **el canal donde usaste el comando** y te menciona. Diez tipos: cacería (1 min), viaje (30 min), talar (5 min), minar (5 min), jefe/raid (5 h; 30 min si perdiste), comprar caja (2 h), tala y minería avanzadas (1 h), comida de las mascotas (1 h) y el diario (24 h). Los de **cacería, talar y minar se borran solos a los 2 minutos** (si no, un jugador activo llena el canal); los demás se quedan.
- **Cómo decide** (`GameData/Reminders.cs`, todo puro): después de cada comando, botón o `aa ...` se vuelve a leer el estado real (los `cooldowns` del jugador, las mascotas solo si el comando es de mascotas, el diario solo si es el diario) y la cola `reminders` queda con UNA fila por jugador y tipo. No hay un segundo reloj que se pueda desfasar del juego: la espera sale de `CooldownCatalog`, `PetRules` y `DailyRewardCalculator`. Un reloj de 5 segundos (`Services/ReminderScheduler.cs`) saca lo vencido (un `DELETE ... RETURNING`, atómico) y manda UN mensaje por jugador y canal (varios vencidos juntos = un encabezado y una línea cada uno).
- **Cuándo NO avisa**: los cortos (cacería, talar, minar) si estás peleando o usaste un comando hace menos de 10 s (ya estás jugando); cualquier aviso que llegue muy tarde porque el bot estuvo apagado (corto > 5 min, largo > 6 h: no se manda una ráfaga al volver); lo que apagaste. **En una raid** el aviso del jefe se arma para todos los participantes al arrancar y al resolverse (solo uno tiene interacción propia al final).
- **Ajustes**: `/reminders` (`aa recordatorios`, `aa avisos`) — una lista donde marcás los que querés recibir y botones «Recibir todos» / «Silenciar todos»; apagar uno borra el pendiente. `/info tema:recordatorios` lo explica con los números del juego.
- **Membresía** (el dueño: «si sos un usuario sin membresía, que va a haber después»): hoy avisa a **todos**. Cuando exista, el único lugar para decidir quién recibe es `ReminderPolicy.IsEligible` en `GameData/Reminders.cs` (el reloj y el servicio lo consultan). Qué le toca a cada lado (¿los miembros reciben más tipos, o el aviso es lo que ganan?) lo define él.
- **Para la base viva**: correr **`Database/add_reminders.sql`** ANTES del binario v0.16.0 (crea `reminders` y `reminder_settings`; el código las lee y escribe después de cada comando). Re-ejecutable; una instalación nueva ya las trae en `schema.sql`.
- **Pendiente / no construido**: avisar por mensaje privado si el canal ya no existe o el bot perdió el permiso (hoy se registra y se descarta); un canal fijo por tipo; horas de silencio; avisar otros tipos (la Arena de hoy, el Fuego Nuevo listo) — cada uno es una fila en `ReminderCatalog` más su cuenta de cuándo. Las raids no se re-midieron tras los amuletos de la v0.15.1 (el daño de los jefes subió ~3–4 por golpe).
## Crafteo más corto y cajas sin rareza (v0.15.1, 2026-10-09)

- **Por qué**: el dueño sentía el crafteo «bravo». Revisión de balance completa (simulación de recorridos enteros con el combate real, los drops, la recolección, las recetas y el cooldown de jefe): el set de armas de clase + amuleto de las cinco zonas llevaba **~40 h de juego perfecto** (rango 34–48), el cuello de botella de TODA receta de las zonas 2 a 5 era el drop de `/travel` (40 % cada 30 min = 75 min por unidad; las recetas se calibraron con el 60 % y al bajarlo en la v0.10.2 nadie las retocó) y el arma de clase de la Zona 5 pedía un material **Mítico de recolección** (0,5 % por acción: ~1000 min de mediana y una cola de ~40 h).
- **Qué cambió** (solo recetas, `Database/rebalance_crafting.sql` para la base viva y `rework_drops_and_recipes.sql` para instalaciones nuevas): cada arma de clase de las zonas 2 a 5 pide **1 drop de viaje menos** (3 → 2: ~150 min, el diseño original); cada amuleto, 1 menos (4 → 3: ~225 min); y las cuatro armas de clase de la Zona 5 **ya no piden Fragmento de Meteorito / Corteza del Árbol de Vida**: las de mineral piden Gema de Zafiro ×1 y las de madera Madera de Nogal ×3 (como la Zona 4). Medido: set de las cinco zonas **~31 h** (p90 ~35 h); la Zona 1 y las armas generales no cambian. Corteza y Meteorito siguen cayendo (0,5 %) y ahora se usan en el **equipo del Fogón Eterno** (el dueño: «tienen que usarse para algo»): el Trinche pide Corteza del Árbol de Vida ×1 y la Brasa pide Fragmento de Meteorito ×1 (se juntan mientras se hace el resto: ~71 % ya los tiene cuando termina el set de las 5 zonas; mediana ~20 h desde el inicio de la vuelta, p90 ~50 h). **Ojo:** cada Fuego Nuevo borra la mochila, así que cada vuelta necesita el par de nuevo y el bonus de cantidad del Fuego Nuevo no baja la cantidad de hallazgos; si en las vueltas altas eso frena, la salida es que esos dos ítems sobrevivan al reinicio. Sin usar, valen 500 de Polvo.
- **Amuletos más fuertes** (el dueño: «no se siente el amuleto; con el puesto deberías poder hacer autohunt sin perder vida casi»): la defensa se resta del golpe 1 a 1, y con el amuleto de la zona una cacería común costaba 11–20 % de la vida. Ahora **Zonas 2 a 5: 18/30/46/75 → 35/60/85/120** (Zona 1 sigue en 10) y la **Brasa del Fogón 120 → 165**: una común con el amuleto de la zona cuesta **~3 %** (un élite de `/travel`, 4–6 %). Para que el examen no se pierda, **cada jefe pega más en lo mismo que sube su amuleto** (Lobisón Alfa +17, Capataz de Hierro +30, Señor del Volcán +39, Soberano de la Escoria +45, Asador +45): con el equipo propio se pierde igual que antes (2–13 %) y con el de la zona anterior más (~50–84 %, lo que decía el diseño). El **PvP no cambia** (sigue con los DEF viejos por nombre de amuleto: `PvpTuning.Amulet`). Migración: `Database/rebalance_amulets.sql` (re-ejecutable). Los raids no se re-midieron (~3–4 más de daño por golpe). Los precios de venta de esos cuatro amuletos suben con el stat.
- **Apertura de cajas sin rareza**: `/open` ya no escribe «(Raro)», «(Mítico)»… al lado de cada ítem (el drop de monstruo no tiene rareza: cae igual en la Zona 1 que en la 5). El ✨ de lo Épico o mejor y el orden quedan.
- **Polvo de los drops de monstruo por su origen** (el dueño: «el drop de monstruo no tiene rareza»): `/dismantle` pagaba por la etiqueta de rareza, y un drop de cacería de la Zona 5 (Mítico, 6 %) daba 500 de Polvo = ~30 por cacería, 60 veces el ritmo calibrado (0,5 por minuto). Ahora los 15 drops de monstruo traen su Polvo en `items.dust_value`: **17** los de cacería y **38** los de viaje (0,5 Polvo por minuto de farmeo en los dos). La madera y el mineral siguen por rareza. La lista de `/dismantle` y la de `/shop sell` ya no escriben la rareza de un drop. **Migración `Database/rebalance_dust.sql` (agrega la columna: correrla ANTES del binario).**
- **Para la base viva**: correr `Database/rebalance_crafting.sql` (re-ejecutable, se verifica sola). No hace falta antes del binario: el código no lee estas cantidades de forma especial.
- **Lo que quedó sin tocar, con números** (la revisión completa): clases (con habilidad y comida, jefe con equipo propio: Guerrero 8 %, Ninja 8 %, Hechicero 11 %, Arquero 13 % de perder; con el de la zona anterior 53 / 42 / 55 / 63 %), oro (sobra desde la Zona 2: las recetas cuestan <10 min de ingresos), XP (nivel 25 en ~12 h, mucho antes que el equipo; sin equipo hacen falta +10 niveles para el jefe de la Zona 2 y +14 para el de la 3). El piso del cooldown de jefe (5 h global) sigue en 20,8 h hasta vencer al jefe 5.

## Las Crónicas del Fogón y ajuste de clases contra monstruos (v0.15.0, 2026-10-08)

- **La historia** (`/story`, `aa historia`): el Tabernero cuenta, junto al fuego, por qué el mundo es de cenizas. **Acto I** (prólogo + un capítulo por jefe de zona + el del Asador, abiertos la PRIMERA vez que se vence a cada uno), **Acto II** (cada Fuego Nuevo le saca 1 % de pesar al Asador y en 12 vueltas se abre una escena —13 escenas, dos de ellas en la vuelta 1—: 1, 2, 3, 5, 10, 15, 25, 35, 50, 75, 90, 99) y **Acto III** (Fuego Nuevo 100, final con puertas abiertas). Lo sellado no muestra ni el título. La historia **no da poder** (ni stats, ni oro, ni drops). Diseño y razones: `docs/lore/cronicas-del-fogon.md`; los textos están en `GameData/Lore.cs`.
- **La historia salta mientras jugás**: aviso privado «📖 Capítulo desbloqueado» / «Escena nueva del fuego» al terminar el comando (mismo camino que los logros: evento `story_chapter` y el contador de `fuego_nuevo` → `ProgressNotifier`), una frase del Tabernero al abrir tu primera mascota y otra del Herrero al forjar tu primera pieza, la escena de la primera victoria sobre cada jefe en su pantalla, y el pesar del Asador en `/fuegonuevo`. **Desde la vuelta 1** el Herrero y el Tabernero saludan distinto y **el Asador cambia sus frases** en la pelea (escalones 10, 25, 50 y 90).
- **Logros nuevos** (22 en total, solo oro y XP): **Compañero del Asador** (Fuego Nuevo 10 / 25 / 50) y **El que volvió** (75 / 90 / 100), en la página Constancia.
- **Clases contra monstruos** (`GameData/PveTuning.cs`): el dueño notó que el Ninja pasaba la escalera con el equipo atrás. Medido con el resolver real: el Ninja perdona la falta de equipo mejor que nadie y el Arquero es la clase floja de verdad. Ahora, **solo contra monstruos** (el PvP queda como estaba medido): Ninja **×0,95** de vida y Arquero **×1,15** (el dueño pidió no pasarse con el Arquero: «el crítico pega mucho según cómo se juegue»; ×1,25 lo dejaba entre el Guerrero y el Hechicero, ×1,15 lo deja a la par del Hechicero con equipo propio). Se aplica en `PlayerCombatProfileCalculator.Resolve` solo cuando hay bonificaciones, y la vida de combate se destraduce a vida real como la del Guerrero.
- **Pendiente / abierto a propósito**: qué cambia concretamente en el juego al llegar al Fuego Nuevo 100 (título, nombre del mundo, el Asador como personaje del campamento), la causa real de la caída de la Fragua, volver a encender la Gran Fragua y quién es de verdad el Tabernero. Con el ritmo actual, 100 vueltas son más de mil horas: el peso emocional está en las primeras 25.
- Sin cambios de base de datos.

## Primera hora más amable (v0.14.6, 2026-10-08)

- **Kit inicial**: `/start` ahora regala **5 Mate Amargo** (una vez por cuenta; la bienvenida lo avisa y apunta a `/heal`). Medido con las reglas reales: en nivel 1 una pelea cuesta 30–53 % de la vida, la vida no se recupera sola y curarla con comida costaba más oro del que la pelea paga; sin curarse, la 2.ª pelea seguida se perdía entre 8 % (Ninja) y 28 % (Arquero).
- **Textos corregidos**: `/travel` y `aa travel` decían «x10, 10 minutos» (son x30 y 30 minutos); la bienvenida pasó a voseo («Preparate… Acá»); el inventario vacío sugiere `/hunt`, `/chop` o `/mine`.
- **Lo que el dueño NO quiso** (no insistir): consejo de vida baja al ganar y descripciones de clase que adviertan fragilidad.
- Sin cambios de base de datos.


## Jugar el combate escribiendo y 60 segundos por turno (v0.14.5, 2026-10-08)

- **Por qué**: a veces el botón del combate «no respondió» o carga un rato: Discord le da al bot 3 segundos para contestar un clic. Un comando de texto no tiene ese límite.
- **Qué hay**: `aa attack` (`atk`, `atacar`), `aa ability` (`skill`, `hab`, `habilidad`) y `aa flee` (`huir`) para `/hunt`, `/travel`, `/boss` y `/raid`. Juegan el MISMO turno que el botón (es un solo método compartido) y el resultado sale como **un mensaje nuevo con botones nuevos**; al mensaje viejo se le sacan los botones. Está explicado en `/info tema:hunt`.
- **60 segundos por turno** (eran 30): pasado ese tiempo sin actuar el combate se abandona. Está en un solo número (`CombatSessionService.TurnTimeout`).
- **Límite que hay que saber**: solo sirve si el combate sigue en la memoria del bot. Un reinicio (cada subida a `main`) corta todos los combates, con botones o sin ellos.
- **Sin cambios de base de datos.**


## Trofeos de las cajas retirados (v0.14.4, 2026-10-07)

- **Qué es**: 17 materiales que solo salían de las cajas y no servían para nada (ni recetas ni drops de monstruos): Collar de Cuero Viejo, Garra Maldita, Hueso Añejo, Corona de Escoria Viva... Se borraron del catálogo y de los botines. El peso que ocupaban (3 a 6 % de cada caja) pasó a la recolección de la misma caja, así que las cajas dan un poco más de material útil.
- **Consecuencia que hay que saber**: el logro **Coleccionista** (juntar los 17 trofeos) desaparece, porque sin trofeos no se puede completar. El **Arca del Soberano** ahora sale solo de **Matajefes III** (20 jefes vencidos).
- **Para correr en una base que ya existe**: `Database/retire_box_trophies.sql` (re-ejecutable, una transacción, **reembolsa en oro al precio de venta** a quien tenga alguno en la mochila). Hacer backup antes. Una instalación nueva ya lo trae. El código nuevo funciona con la base vieja y al revés, pero conviene correr primero el script.
- **Idea sin construir**: «luego vemos de poner en otro lado eso» (el dueño). El mecanismo de colección (`player_collection`, `trophy_found`) quedó latente por si vuelve algo parecido.


## Perfil más limpio (v0.14.3, 2026-10-07)

- **`/profile` ya no muestra** las mascotas, los porcentajes del Fuego Nuevo, las bendiciones ni los oficios (pedido del dueño: «dejalas aparte»). Se ven con `aa pet` (o `aa pets`), `aa fn`, `aa bendiciones` y `aa oficios`. El perfil sigue con lo básico, el número que pelea (el ataque y la defensa siguen mostrando sus extras 🔥/🐾), «FN N» en el título y el color y título cosméticos de Brasa de Color.
- **`aa fn`** ahora muestra «Lo que te da hoy (Fuego Nuevo ×N)» también cuando ya podés volver a reiniciar (antes solo mostraba lo que ganarías con el próximo) y lista tus bendiciones.
- Sin cambios de base de datos.


## Arma general más barata y mascotas sin spoilers (v0.14.2, 2026-10-07)

- **Arma general (zonas 2 a 5)**: pide 2 de cada ingrediente en vez de 3 o 4 (~67 min de juego perfecto en lugar de ~100). El arma de clase (×1,5, ~225 min) no se tocó. Motivo: «nunca nadie haría la general». Elegí abaratar la general y no encarecer la de clase porque esa ya era 1,5 veces más lenta que lo diseñado desde que el drop de `/travel` bajó al 40 % y subirla alargaría las ~52 h del camino de las 5 zonas; además no perjudica a nadie que ya esté juntando materiales. Zona 1 igual (su general ya era la más barata, ~48 min). **Si preferís el otro camino (encarecer la de clase)**, es un `UPDATE` sobre `recipe_ingredients`; el reporte para medir es `report_recipe_pacing.sql`.
- **Para correr en una base que ya existe**: `Database/rebalance_general_weapons.sql` (re-ejecutable, se verifica solo, no hace falta reiniciar el bot). Una instalación nueva ya lo trae en `rework_drops_and_recipes.sql`.
- **`/pet view` ya no lista las mascotas que faltan** («por descubrir»): solo las que tenés, más un aviso si guardás un huevo sin abrir. Sin spoilers también en la ayuda (`/info tema:pets` y `tema:boxes`): ya no dicen cuántas hay, cuál es la del Fogón ni que existen cajas con nombre propio.
- **Qué mirar jugando**: si con la general barata la gente la forja de verdad o la saltea; si pasa lo segundo, el siguiente paso sería encarecer la de clase.


## Slots con 6 símbolos y forge especial (v0.14.1, 2026-10-07)

- **Slots**: sexto símbolo (🍷), el par paga ×1,3 y el trío ×5 (pedido del dueño). Chances: trío 2,78 %, par 41,67 %, nada 55,56 %; vuelve ×0,681 de lo apostado (la casa se queda con el 32 %, medido con 2 millones de tiradas). Antes, con 5 símbolos y el par en ×1,1, volvía ×0,728. El sexto símbolo es lo que permite pagar más el par sin volver a regalar oro. `/play` sigue sin cooldown ni tope.
- **`/forge-special`** (`aa forgespecial`): el Trinche del Asador Eterno y la Brasa del Fogón Eterno **siempre a la vista, desde el nivel 1**, con lo que tenés de cada ingrediente (✅/❌) y un aviso de no desmantelar ni vender esos materiales. Solo para mirar: se forjan en `/forge` cuando la puerta ya está abierta. Sin cambios de base de datos.
- **Idea sin construir**: que `/dismantle` avise cuando el ítem que vas a desmantelar es un ingrediente del equipo del Fogón.


## Cofres parejos (v0.14.0, 2026-10-07)

- **Qué cambió**: el dueño notó que el Cofre de Oro «sale muy poco para lo que da» y que la Zona 0 no tenía una recompensa pensada para los reinicios. Medido con `Database/report_box_economy.sql`: un Cofre de Oro vale ~211k «minutos de farmeo», más que 2 horas perfectas de juego en la Zona 5, y se regalaba por misiones, logros y como 40 % de cada repetición del jefe. Ahora las cajas gratis salen de la tabla `zone_boxes` (una fila por zona y rol: `repeat`, `daily`, `weekly`, `prize`, `prize_top`, con su chance) y el Cofre de Oro solo aparece desde la Zona 3. Diseño y números: `docs/superpowers/specs/2026-10-07-cofres-parejos-design.md`.
- **Cajas nuevas** (no se compran ni se venden): **Cofre de Escoria** (el cofre del jefe de la Zona 5, 35–65 ítems) y **Brasero del Fogón** (el del Asador Eterno: lo que sobrevive al Fuego Nuevo, o sea oro y comida de mascotas). **Sexta mascota**: Chispa del Asador (+10 % de cantidad en `/chop` y `/mine`), su huevo llega con la primera victoria sobre el Asador de cada vuelta si no la tenés. El Asador paga ahora 3000 de oro y 300 de XP (el XP se borra con el reinicio, el oro no).
- **Hay que correr `Database/add_zone_boxes.sql` ANTES del binario v0.14.0** (ya está aplicado en la base de desarrollo). Detalle en `DEPLOY.md`.
- **Para confirmar con el dueño**: `Database/report_economy.sql` muestra que lo gratis sigue desparejo entre zonas (3,5 a 9,9 sesiones de 2 h por semana; la Zona 4 es la más generosa por su caja diaria de Arcón de Hierro). Es dato, se corrige con un `UPDATE` en `zone_boxes`. Los tres ítems nuevos (Cofre de Escoria, Brasero del Fogón, Huevo del Fogón) **no tienen emoji propio todavía**: se ven con un emoji común hasta que se suba el pixel art al portal y se agregue su `UPDATE` a `update_item_emojis.sql`. Los topes de `Domador` y `Criador` subieron a 6 y 150 (hay 6 mascotas).
- **Pendiente / ideas**: `GoldPerHunt`, `DustPerAttempt` y `Enchantments` siguen siendo arreglos de 5 elementos en código: una zona nueva necesita que los extiendan además de sus filas en la base. La idea del dueño de espaciar las zonas a ~10 niveles (con más XP por zona y monstruos reajustados) es un proyecto aparte: obliga a volver a medir TODOS los monstruos, jefes, raids y el Asador con el `CombatTurnResolver` real.


## Oficios: Leñador, Minero y Encantador (v0.13.0, 2026-10-06)

- **Qué es**: `/professions` (`aa professions`, `aa oficios`). Tres oficios de nivel 0 a 100 que suben solos al usar `/chop`, `/mine` y `/enchant` (10, 10 y 50 de XP por uso). Diseño y presupuesto medido: `docs/superpowers/specs/2026-10-06-oficios-design.md`.
- **Sin migración**: la XP sale de los contadores que ya existen (`player_stats`: `chop`, `mine`, `enchant`), así que **lo que cada jugador ya hizo cuenta**: quien talló 100 veces arranca en el nivel ~37. Fuego Nuevo no los toca.
- **Lo que da cada nivel**: Leñador/Minero +0,2 % de cantidad y +0,1 % de chance de subir un escalón la rareza (el Mítico nunca se mejora); Encantador −0,25 % de Polvo por intento. Al 100: **tala/minería avanzada** (`/chop modo:Avanzada`: 4 sorteos juntos, cooldown propio de 1 h) y **encantamiento avanzado** (`/enchant modo:Avanzado`: 2 tiradas, se queda con la mejor, 1,5× el costo).
- **Decisiones mías que hay que confirmar con el dueño** (están todas como constantes en `GameData/ProfessionRules.cs`): la curva (≈ 810 usos de tala o minería, ≈ 160 intentos de encantar, para llegar al 100); los porcentajes por nivel; que la avanzada de recolección sean 4 sorteos por hora (+33 % de ritmo); que la del encantador sea «dos tiradas, la mejor, a 1,5×»; y que cada uso avanzado cuente como 4 (o 2) usos para la XP y para las misiones.
- **Presupuesto**: el camino de recetas de las 5 zonas está trabado por los drops de los monstruos, así que los oficios de recolección lo bajan solo ~12 % con todo al 100 (el Fuego Nuevo 30 con oficios queda en el ~30 % de la vuelta 1, justo en el piso del spec). Donde sí se nota es en el Polvo y en lo que cuesta encantar.
- **Sin cambios de base de datos**: nada que correr antes de arrancar. Para desplegar alcanza con reiniciar el bot.
- **Falta / ideas**: más oficios (Cazador, Herrero, Mascotero…: una fila en `ProfessionCatalog`), logros por nivel de oficio, un aviso público al llegar al 100, y que el tema `/info tema:gather` y el de `enchant` lo mencionen más.

## Las slots: el par baja a ×1,1 (v0.12.1, 2026-10-06)

- El dueño y un amigo juntaron mucho oro jugando slots y pidió bajar el premio de **dos iguales** a **×1,1** (solo las slots; el coinflip no se toca). Los tres iguales siguen en ×5.
- Efecto medido con las chances exactas (5 símbolos: tres iguales 4 %, par 48 %, tres distintos 48 %): el retorno pasa de ×0,92 a **×0,728** por cada 1 apostado, o sea que la casa se queda con el **27 %** (antes el 8 %). Con la apuesta mínima (10) el par da 11: sigue sumando +1.
- El par se calcula en `decimal` (1,1 no es exacto en binario): da exactamente `apuesta + apuesta/10` para cualquier apuesta (lo prueba `casinotest` con 200.000 apuestas). `/info tema:play` lee las constantes, así que ya muestra los números nuevos.
- **Sin límite todavía**: `/play` sigue sin cooldown ni tope de apuesta; lo único que frena es la ventaja de la casa. Si vuelven a «romper» el casino (por cantidad de jugadas, no por las chances), el siguiente paso sería un cooldown corto o un tope por jugada.

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

## Brecha vs. el diseño original (revisada el 2026-10-22: casi todo se resolvió)

- ✅ **Resuelto:** en vez de restringir qué arma puede equiparse cada clase, `items.weapon_family` (Espadas/Dagas/Arcos/Grimorios) y `GameData/ClassWeaponSynergy.cs`: cualquier clase puede equipar cualquier arma, pero el daño del arma se
  multiplica ×1,5 solo si la familia coincide con la de la clase. Las cuatro familias están cargadas en las cinco zonas (4 armas de clase + 1 general + 1 amuleto por zona).
- 🟡 **El herrero solo craftea ítems fijos, no mejora los que ya tenés.** `/forge` es siempre «pagá X, recibís un ítem predefinido». Lo más parecido a mejorar una pieza hoy son los encantamientos (`/enchant`, v0.9.0 y v0.22.0). Sigue
  abierto si el herrero debería poder subir de nivel una pieza.
- ✅ **Resuelto:** el amuleto suma defensa (`PlayerCombatProfileCalculator.AmuletDefense`, ladder 10/35/60/85/120/165 desde la v0.15.1) y se encanta (Guarda, Vida, Esquive).

## Deuda técnica

- ✅ **Resuelto:** `current_weapon_id` se eliminó del modelo, `UserSql` y `schema.sql`.
- 🟡 **Sigue abierto: no hay registro de qué migraciones SQL ya corrieron en cada base.** Cada versión con cambio de esquema trae su `Database/add_*.sql` y hay que acordarse de correrlo con `psql` ANTES del binario (la lista de
  cuáles, en `CLAUDE.md`). Una tabla `schema_migrations` simple (o una herramienta tipo DbUp) lo resolvería, y de paso permitiría que el bot avise al arrancar «te falta correr tal script» en vez de caerse con un error de columna.
- ✅ **Resuelto en la v0.18.3:** los arneses de verificación viven en `tests/` (47 hoy) y el simulador de calibración en `tools/balance2`; se corren con `tests/run-all.ps1` o `tests/run-all.sh`. Sigue sin haber CI (necesitan una base
  local con la instalación completa) ni un framework de pruebas, a propósito: son consolas que se leen de corrido.
- 🟢 **Logging:** `Services/BotLog.cs` ya distingue `Error` y `Warn` y pone archivo y método; la salida sigue siendo la consola, que en Railway se guarda en los logs del servicio. Alcanza; si algún día hace falta buscar por jugador o
  por fecha, ahí habría que sumar algo persistente.

## Mejoras de UX / comandos

- ✅ **Resuelto:** los comandos que piden un ítem tienen lista desplegable con autocompletado (`/shop buy|sell`, `/forge make`, `/use`, `/open`, `/zona`, `/trade`, `/exchange`, `/heal`); `/equip` ya no existe (el equipo se forja
  directo al casillero). Las listas de autocompletado no pueden llevar emojis propios (límite de Discord); los desplegables de las escenas sí, desde la v0.22.2.
- ✅ **Resuelto:** `/info` lista los comandos por grupo (corta a propósito) y `/info tema:<tema>` explica cómo funciona cada sistema (26 temas).
- 🟢 **Decisión de diseño, sin tocar:** no hay regeneración pasiva de HP; se cura con comida, `/heal` o la taberna. Vale seguir confirmándolo a propósito, porque es lo que da valor a la comida.

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
- ✅ ~~Sistema de armadura/defensa~~: hecho (el amuleto resta defensa de cada golpe, v0.15.1).
- Comandos de administrador (dar oro/ítems, resetear cooldowns) para moderar el server de pruebas.
- ✅ PvP hecho (`/fight`, la Arena diaria). Siguen sin existir los eventos temporizados de servidor (un jefe compartido por todos, etc.).
