# Auditoría visual de los mensajes del bot

Fecha: 2026-11-12. Rama: `review/fable-ui`. Parte 1: solo diagnóstico, no se tocó código.

**Alcance.** Solo diseño visual en Discord (embeds, botones, menús, emojis, lectura en celular). Nada de lógica,
balance, SQL ni rendimiento. Ninguna propuesta cambia números ni lo que hace un botón.

**Cómo se hizo.** Lectura del código de los builders, en cuatro grupos de pantallas. No se corrió el bot ni los
arneses: las maquetas son reconstrucciones a partir del código, no capturas. Los `archivo:línea` son de esta rama.
Las mejoras M1, M2, M4, M5 y M6 se releyeron línea por línea; el resto viene de una sola pasada de lectura.

**Orden.** De mayor a menor impacto por esfuerzo: primero lo que se ve roto y se arregla en pocas líneas.

**Lo que se dejó como está, porque lo pidió el dueño:** la línea «Ganaste: 💰 N Oro, 📊 N XP» de la victoria
(v0.26.2), el resumen por categoría del inventario (`🪵⛏️ 10 · 🩸 11 · …`), la lista corta de comandos de `/info`
(v0.18.0), el bloque sin título del perfil y el secreto de los premios de expedición antes de salir.

---

## M1 — Emojis propios en títulos y nombres de campo

- **Pantallas:** `/open`, recetario clásico, `/deepshop`, `/stats` → «Más stats».
- **Dónde:** [BoxModule.cs:155](../Modules/BoxModule.cs#L155), [ForgeModule.cs:254](../Modules/ForgeModule.cs#L254),
  [DeepShopModule.cs:142](../Modules/DeepShopModule.cs#L142), [StatsModule.cs:299](../Modules/StatsModule.cs#L299).
- **Problema:** el emoji del ítem (`<:nombre:id>`) va en el título del embed o en el nombre de un campo, donde
  Discord no lo dibuja. El jugador ve el código crudo o nada. En `/stats` pasa solo si el tipo de dragón tiene arte
  propio cargado (`UiEmojis.DragonKind`).
- **Propuesta:** el título y el nombre de campo llevan solo texto (o un unicode); el ícono propio baja a la primera
  línea del valor o de la descripción. En `/open` alcanza con sacar el ícono: la miniatura ya muestra la caja.
  En `/deepshop`, además, el precio (igual en todos los campos) se dice una vez en la descripción.
- **Maqueta** (recetario clásico):

```
ANTES                                   DESPUÉS
<:espada_madera:1234> Espada de Madera  Espada de Madera
+14 ATQ · arma de tu clase              🗡 +14 ATQ · arma de tu clase     (🗡 = ícono propio)
✅ 💰 100 oro (tenés 2500)              ✅ 💰 100 oro (tenés 2.500)
```

```
ANTES  título: 📦 ¡Abriste <:cofre_escoria:99> Cofre de Escoria!
DESPUÉS título: 📦 ¡Abriste Cofre de Escoria!
```

- **Riesgo:** bajo. Los textos de `/open` y `/deepshop` son plantillas `Loc.F` con el ítem como hueco: la plantilla
  no cambia si solo cambia lo que se pasa; si se mueve el precio, hay una plantilla nueva en `Locales/en.json`.
  Arneses que miran estas pantallas: `stage3test`, `stage7test`, `storytest` (open), `v0281test`, `v0530test`
  (recetario), `deepshoptest`, `v0230test` (deepshop), `dragontest`, `cooktest` (stats).

## M2 — Derrota: la tablita del combate se mezcla con las vidas

- **Pantalla:** derrota en pelea solo (`/hunt`, `/travel`, `/boss`).
- **Dónde:** [AdventureModule.cs:938-944](../Modules/AdventureModule.cs#L938-L944).
- **Problema:** las dos vidas son campos inline y enseguida viene la tablita, también inline. «⏱️ Turnos» ocupa el
  tercer lugar de la fila de las vidas y el resto de la tabla queda corrido. La huida y la derrota después de comer
  ya lo evitan con la vida a todo el ancho.
- **Propuesta:** cerrar la fila de las vidas con el tercer campo inline vacío (el mismo recurso del inventario)
  cuando `ShowFightSummary` está prendido. No es un separador: completa una fila de dos columnas.
- **Maqueta:**

```
ANTES                                    DESPUÉS
❤️ Tu HP   | 🐗 HP de Jabalí | ⏱️ Turnos  ❤️ Tu HP   | 🐗 HP de Jabalí |
🗡️ Daño    | 🩸 Recibido     |            ⏱️ Turnos  | 🗡️ Daño hecho   | 🩸 Recibido
```

- **Riesgo:** bajo, sin textos nuevos. En celular los inline se apilan, así que ahí no cambia nada. Miran la
  derrota: `v90test`, `loctest`, `portraittest`, `stage6test`, `v83test` (revisar si cuentan campos).

## M3 — Números sin separador de miles

- **Pantallas:** tienda y taberna, forja, perfil, logros, misiones, `/open`, victoria en equipo.
- **Dónde:** [ShopModule.View.cs:65](../Modules/ShopModule.View.cs#L65) y [:70](../Modules/ShopModule.View.cs#L70),
  [ShopModule.cs:182](../Modules/ShopModule.cs#L182), [TabernaModule.cs:439](../Modules/TabernaModule.cs#L439),
  [ForgeModule.cs:248](../Modules/ForgeModule.cs#L248) y [:350](../Modules/ForgeModule.cs#L350),
  [GameModule.cs:215](../Modules/GameModule.cs#L215), [AchievementsModule.cs:244](../Modules/AchievementsModule.cs#L244),
  [MissionsModule.cs:252](../Modules/MissionsModule.cs#L252), [BoxModule.cs:140](../Modules/BoxModule.cs#L140),
  [RaidModule.cs:1993](../Modules/RaidModule.cs#L1993).
- **Problema:** en la misma pantalla conviven `1234` y `1.234` (en la tienda, las cajas usan `GameHistory.Number`
  y la comida no). Los números grandes no se leen: una Hazaña muestra `0/1000000`.
- **Propuesta:** pasar todos por `GameHistory.Number`. De paso, `/open` dice «monedas» donde el resto dice «oro».
- **Maqueta:**

```
ANTES                         DESPUÉS
Tu oro: 1234567               Tu oro: 1.234.567
XP  1234567 / 2000000         XP  1.234.567 / 2.000.000
▰▰▱▱▱ 0/1000000               ▰▰▱▱▱ 0/1.000.000
```

- **Riesgo:** medio, por la cantidad de arneses que comparan números como texto. `tests/Shared/ProfileProbe.cs`
  lee la XP y la vida del perfil: hay que tocar ese archivo. También `stage4test` (compra), `stage10test`,
  `stage13test` (taberna), `v0410test` (logros), `loctest`. Las plantillas de `en.json` no cambian (el número es un
  hueco). Conviene hacerlo pantalla por pantalla.

## M4 — Recetario completo: cada receta es una fila de fichas

- **Pantalla:** recetario completo (botón del herrero con la opción de `/config`, `aa forge recipes`).
- **Dónde:** [ForgeModule.cs:215-224](../Modules/ForgeModule.cs#L215-L224) (`RecipeLine`); campo de corte «…» en
  [ForgeModule.cs:77](../Modules/ForgeModule.cs#L77).
- **Problema:** es el formato de fichas que el dueño descartó en la v0.8.4: resultado, estadística, tres o cuatro
  ingredientes y el oro en una sola línea, con los ingredientes solo como ícono. En celular se parte en 3 o 4
  renglones y los íconos quedan pegados al ítem equivocado.
- **Propuesta:** dos renglones por receta: el resultado con su estadística y el oro; debajo, los ingredientes con
  cantidad e ícono, sin flecha. El aviso de corte pasa al footer en vez de un campo llamado «…».
- **Maqueta** (🗡 🌲 ⛏ 🦷 🟤 = íconos propios):

```
ANTES
🗡 **Espada de Madera** `+14 ATQ` → 12 🌲 + 4 ⛏ + 2 🦷 + 1 🟤 · 💰 100

DESPUÉS
🗡 **Espada de Madera**: +14 ATQ · 💰 100
     12 🌲   4 ⛏   2 🦷
```

- **Riesgo:** medio. Duplica los renglones por zona, así que se llega antes al tope de 1024 por campo y al de 5.800
  que ya protege el builder: hay que medir con el catálogo entero (zonas 1 a 10). La segunda línea sigue siendo una
  fila de íconos; la alternativa sin ese resto es que el recetario completo abra la página por zona. Decisión del
  dueño. Arneses: `fogontest`, `v0281test`.

## M5 — `/stats`: todo en título grande y bonos en fichas

- **Pantallas:** `/stats` y «Más stats».
- **Dónde:** [StatsModule.cs:171-244](../Modules/StatsModule.cs#L171-L244) (encabezados),
  [:250](../Modules/StatsModule.cs#L250) (footer), [:307](../Modules/StatsModule.cs#L307) (dragón),
  [:324-326](../Modules/StatsModule.cs#L324-L326) (Fuego Nuevo y bendiciones).
- **Problema:** la primera pantalla tiene hasta 7 encabezados `#` y un `##`: en celular es una columna muy alta
  donde nada resalta porque todo es grande. El footer junta tres ideas en unos 190 caracteres. En «Más stats», los
  bonos del dragón, del Fuego Nuevo y las bendiciones van separados con « · » en una línea.
- **Propuesta:** encabezados `###` (o negrita) y solo el total de cada bloque en grande; footer de una frase; en
  «Más stats», un bono por línea.
- **Maqueta** (campo de Fuego Nuevo):

```
ANTES
Fuego Nuevo 3: EXP +30 % · drop de cacería +15 % · drop de viaje +15 % · recolección +10 %
🐾 Manada III · 🔥 Brasa de Color II

DESPUÉS
**Fuego Nuevo 3**
EXP: +30 %
Drop de cacería: +15 %
Drop de viaje: +15 %
Recolección: +10 %

🐾 Manada III
🔥 Brasa de Color II
```

- **Riesgo:** medio. La línea del Fuego Nuevo es una sola plantilla `Loc.F`: partirla cambia `en.json`. Arneses:
  `cooktest`, `v0530test`, `dragontest`.

## M6 — Tienda y taberna: tres columnas con dos renglones por ítem

- **Pantallas:** `/shop view` y la escena de la taberna.
- **Dónde:** [ShopModule.View.cs:70-73](../Modules/ShopModule.View.cs#L70-L73) y
  [:108-121](../Modules/ShopModule.View.cs#L108-L121) (`AddColumn`).
- **Problema:** tres columnas inline (Comida | Cajas | Mascotas), cada ítem en dos renglones más uno en blanco. En
  celular las columnas se apilan y queda una tira muy larga antes de llegar a los cinco menús. No sigue el formato
  `icono **Nombre**: precio`, y nada dice si el oro alcanza.
- **Propuesta:** un campo de ancho completo por categoría, un renglón por ítem con el precio al final; el detalle
  (cuánto cura, rango de la caja) en el mismo renglón, corto.
- **Maqueta:**

```
ANTES (cada columna)            DESPUÉS (campo «Comida», ancho completo)
**🥩 Chorizo**                   🥩 **Chorizo**: 30 oro · cura 40
Cura 40 HP · 30 oro              🍖 **Asado**: 120 oro · cura 150
                                 🍷 **Banquete**: 900 oro · cura 400, +15 % ATQ
**🍖 Asado**
Cura 150 HP · 120 oro
```

- **Riesgo:** alto en arneses y es un cambio de un diseño que el dueño pidió en dos columnas (v0.9.x): va con su
  visto bueno. `BuildViewEmbed` lo leen `cofrestest`, `pettest`, `reworktest`, `stage2atest`, `stage2btest`,
  `stage4test`, `stage6test`, `stage9test`, `v0193test`, `v0400test`, `loctest`. Cambian plantillas de `en.json`.

## M7 — Cortes a 1.023 caracteres que parten un emoji o una negrita

- **Pantallas:** tienda, recetario clásico, `/dragon`.
- **Dónde:** [ShopModule.View.cs:119](../Modules/ShopModule.View.cs#L119),
  [ForgeModule.cs:255](../Modules/ForgeModule.cs#L255), [DragonModule.cs:591](../Modules/DragonModule.cs#L591).
- **Problema:** el recorte de defensa corta por carácter. Si cae en medio de `<:emoji:id>` o de un `**`, el final del
  campo muestra el código roto o deja todo lo que sigue en negrita.
- **Propuesta:** cortar por renglón entero (como ya hace `GameModule.PackLines`) y cerrar con «…».
- **Maqueta:**

```
ANTES                              DESPUÉS
**🥩 Chorizo**                      **🥩 Chorizo**
Cura 40 HP · 30 oro                Cura 40 HP · 30 oro
**<:asado_d…                       …
```

- **Riesgo:** bajo, sin textos nuevos. Solo se nota con catálogos largos; hoy es una defensa que casi nunca corre.
  En la misma pasada: el menú del Carpintero no tiene tope de 25 opciones
  ([ToolsModule.cs:145](../Modules/ToolsModule.cs#L145)) y «Todo» del inventario no tiene tope de 6.000 por mensaje
  ([GameModule.cs:349](../Modules/GameModule.cs#L349); `v83test` lo mide sin los drops de las zonas 6 a 10). Ninguno
  de los dos se comprobó con datos reales.

## M8 — `/dragon`: pared de texto en «Criar» y en la invitación

- **Pantallas:** `/dragon` y la invitación a criar.
- **Dónde:** [DragonModule.cs:614-622](../Modules/DragonModule.cs#L614-L622),
  [:818](../Modules/DragonModule.cs#L818); tablón en [:446-450](../Modules/DragonModule.cs#L446-L450).
- **Problema:** «Criar» son tres párrafos (unos 600 caracteres) y «Tipo y comida» otro más: repiten `/info dragon`.
  La invitación le muestra al invitado la regla completa de tipo y comida. En el tablón, «En otros tiers» va en
  fichas (`Tier 2: 1 · Tier 3: 4`) y el footer repite el texto del menú.
- **Propuesta:** en la pantalla quedan los datos de hoy (chance, piedad, comando) y un «más en `/info dragon`»; en
  la invitación, una línea; en el tablón, un tier por renglón y sin footer.
- **Maqueta** (campo «Criar»):

```
ANTES                                        DESPUÉS
Criar                                        Criar
Con otro jugador: /dragon breed @amigo…      🎲 **Chance hoy**: 12 %
(párrafo de la chance y la piedad)           🙏 **Piedad**: +3 % por intento fallido
(párrafo de criar solo = un tercio)          👥 `/dragon breed @amigo`
Tipo y comida                                📖 Cómo funciona: `/info dragon`
(párrafo de la Comida de Dragón)
```

- **Riesgo:** medio. `dragontest` exige los nombres de campo exactos «Criar» y «Entrenar» y que no lleven emoji
  (hoy además no pasan por `Loc.T`). Textos nuevos en `en.json`. Antes de recortar hay que confirmar que
  `/info dragon` dice todo lo que se saca.

## M9 — Botín en fichas

- **Pantallas:** `/open`, regreso de expedición, victoria del jefe en equipo.
- **Dónde:** [BoxModule.cs:146](../Modules/BoxModule.cs#L146) y [:167](../Modules/BoxModule.cs#L167),
  [PetModule.Expeditions.cs:153](../Modules/PetModule.Expeditions.cs#L153) y
  [:182](../Modules/PetModule.Expeditions.cs#L182), [RaidModule.cs:1993](../Modules/RaidModule.cs#L1993).
- **Problema:** `/open` lista «• **3×** icono Nombre» (otro formato que el inventario) y junta los trofeos con
  comas; la expedición muestra lo que trajo con « · » y el hallazgo con comas; la victoria en equipo pone
  «💰 N oro · 📊 N XP» con unicode a mano en vez de `UiEmojis.Gold/Xp`.
- **Propuesta:** el mismo renglón en todos lados: `icono **Nombre**: 1.234`, uno por línea.
- **Maqueta** (`/open`):

```
ANTES                         DESPUÉS
• **3×** 🌲 Pino               🌲 **Pino**: 3
• **1×** ⛏ Hierro             ⛏ **Hierro**: 1
💰 250 monedas                 💰 **Oro**: 250
```

- **Riesgo:** medio. `MissionRewards.Describe` lo usan también misiones, logros y Arena: conviene una variante
  «en líneas» y no cambiar la existente. La victoria en equipo tiene un campo inline por jugador: con renglones
  propios crece en alto con 6 jugadores. Arneses: `stage3test`, `stage7test`, `storytest` (open), `v0390test`,
  `v0530test` (expediciones); la victoria en equipo no tiene arnés que mire el texto.

## M10 — Jefe en equipo: reglas arriba, footer cargado, fichas por jugador

- **Pantallas:** invitación y turno de `/boss @amigo`.
- **Dónde:** [RaidModule.cs:1891-1896](../Modules/RaidModule.cs#L1891-L1896),
  [:1942](../Modules/RaidModule.cs#L1942), [:1956](../Modules/RaidModule.cs#L1956),
  [:1964](../Modules/RaidModule.cs#L1964).
- **Problema:** la invitación abre con unos cinco renglones de reglas que empujan la lista del grupo hacia abajo.
  En el turno, cada jugador lleva «habilidad · 🍖 comida lista» y el footer junta tres reglas con « · ». El botón
  dice «Habilidad» y en la pelea solo lleva el nombre de la habilidad.
- **Propuesta:** una línea de reglas en la invitación y el resto en `/info boss`; estado del jugador con íconos
  (✨ habilidad lista, 🍖 comida disponible); footer de una regla; el mismo criterio de etiqueta que en solo.
- **Maqueta** (participantes):

```
ANTES                                   DESPUÉS
⚔️ Agus  ▰▰▰▰▱▱ 420/700                  ⚔️ Agus  ▰▰▰▰▱▱ 420/700  ✨ 🍖
Golpe Brutal lista · 🍖 comida lista     🏹 Polar ▰▰▱▱▱▱ 180/600  ✨
🏹 Polar ▰▰▱▱▱▱ 180/600
Lluvia de Flechas lista · comida usada
```

- **Riesgo:** medio. `partybosstest` afirma sobre «es tu turno» y «comida usada»; `portraittest` mira las
  pantallas. Textos nuevos en `en.json`. El botón con el nombre de la habilidad depende de quién tiene el turno:
  si complica, se deja como está.

## M11 — Menús del herrero y del Carpintero: opciones que se cortan

- **Pantallas:** `/forge` y `/tools`.
- **Dónde:** [ForgeAutocomplete.cs:79-98](../Modules/ForgeAutocomplete.cs#L79-L98),
  [ToolsModule.cs:143](../Modules/ToolsModule.cs#L143), [:159](../Modules/ToolsModule.cs#L159),
  [BlacksmithModule.cs:238](../Modules/BlacksmithModule.cs#L238).
- **Problema:** la etiqueta de cada opción junta estado, nombre, estadística y lo que falta («❌ Nombre (+14 ATQ) —
  falta: 2 Hierro, 30 oro»): se corta a los 100 caracteres y en celular antes. Esas etiquetas no pasan por `Loc`
  y arman «equipad» + «a/o». En `/tools`, un párrafo en cursiva mezcla explicación y leyenda. Los dos NPC tienen el
  footer «Lo que te conteste… sale abajo», que no aporta.
- **Propuesta:** etiqueta = estado + nombre + estadística; lo que falta va en la descripción de la opción (el
  segundo renglón gris). Leyenda corta (`✅ podés · ❌ te falta · 🔒 zona`) y la explicación a `/info forge`.
  Sacar los dos footers.
- **Maqueta** (una opción del menú):

```
ANTES
❌ Espada de Roble (+22 ATQ) — falta: 2 Hierro, 30 or…

DESPUÉS
❌ Espada de Roble (+22 ATQ)
   Te falta: 2 Hierro, 30 oro
```

- **Riesgo:** alto en arneses: `ForgeChoices` es compartido con el autocompletado de `aa forge make`, y
  `choicetest`, `fogontest`, `stage13test`, `v0530test` buscan «falta:»; además `stage11test`, `v0281test`,
  `toolstest`, `v0222test`. Hay que separar el texto del menú del texto del autocompletado. Textos nuevos en
  `en.json`, una oración entera por género.

## M12 — Mazmorras: mensaje alto y botones que no entran

- **Pantallas:** mazmorra en curso (laberinto de humo, candado, ritmo).
- **Dónde:** [DungeonModule.cs:158-161](../Modules/DungeonModule.cs#L158-L161),
  [SmokeMaze.cs:166-178](../GameData/Dungeons/SmokeMaze.cs#L166-L178), [Rhythm.cs:97](../GameData/Dungeons/Rhythm.cs#L97),
  [FireLock.cs:128](../GameData/Dungeons/FireLock.cs#L128).
- **Problema:** en el laberinto se suman la grilla de 17×11, la leyenda y un footer de más de dos oraciones. Las
  cuatro flechas van en una fila con texto («Izquierda», «Derecha») que en celular se corta. El historial del ritmo
  son 15 emojis en una línea. La vida usa barra de texto en un campo de ancho completo y se llama «Tu vida» (en las
  peleas, «Tu HP»).
- **Propuesta:** footer «⏱️ 3 min por jugada»; flechas solo con emoji; historial de 8; barra de emoji de 8 piezas
  como la del perfil y el mismo nombre de campo que en las peleas.
- **Maqueta** (fila de botones del laberinto):

```
ANTES                                         DESPUÉS
[⬆️ Arriba] [⬇️ Abajo] [⬅️ Izquie…] [➡️ Derec…]   [⬅️] [⬆️] [⬇️] [➡️]
```

- **A confirmar en Discord real:** el botón «Borrar» del candado usa `⌫` como emoji. No es un emoji de Discord y un
  emoji desconocido puede hacer rechazar el mensaje. La mazmorra está en producción, así que si nadie lo reportó
  probablemente pasa; se anota para probarlo antes de tocarlo.
- **Riesgo:** bajo-medio. `v0470test` usa `BuildRunEmbed` y `SmokeMaze`. Sacar el texto de las flechas no toca
  `en.json` (se borran entradas: `tools/loc --check` avisa las viejas).

## M13 — Logros, misiones y títulos: listas densas y paginación despareja

- **Pantallas:** `/achievements`, `/missions`, `/titles`.
- **Dónde:** [AchievementsModule.cs:187](../Modules/AchievementsModule.cs#L187),
  [MissionsModule.cs:222](../Modules/MissionsModule.cs#L222) y [:251](../Modules/MissionsModule.cs#L251),
  [TitlesModule.cs:208](../Modules/TitlesModule.cs#L208).
- **Problema:** en misiones, la barra, el `n/m` y el premio van en un renglón que en celular se parte. En logros
  hay un campo «Logros» debajo de un título que ya dice lo mismo (y no pasa por `Loc.T`, igual que «**Total:**» en
  misiones). Títulos no dice «Página X/Y» (logros sí, en el footer) y su footer es largo; la página Hazañas tiene
  24 títulos, unos 2.500 caracteres seguidos.
- **Propuesta:** el premio de cada misión en su renglón; las líneas de logros a la descripción; «Página X/Y ·
  Tenés N de M» en el footer de las dos pantallas paginadas; partir Hazañas en dos páginas.
- **Maqueta** (una misión):

```
ANTES
🗡️ Cazá 10 monstruos
▰▰▰▰▰▰▱▱▱▱ 6/10 · 💰 500 · 📊 200

DESPUÉS
🗡️ **Cazá 10 monstruos**
▰▰▰▰▰▰▱▱▱▱ 6/10
🎁 💰 500 · 📊 200
```

- **Riesgo:** medio. Partir Hazañas cambia la cantidad de páginas y los valores del menú de página: `v0230test`
  mira títulos; `stage3test`, `v0410test`, `v90test` miran logros; `loctest` busca «Tus misiones». Un renglón más
  por misión acerca el campo al tope de 1024 (medir con las semanales completas).

## M14 — Pelea solo: al comer, el mensaje cambia de forma

- **Pantallas:** turno después de usar comida, derrota después de comer, encuentro.
- **Dónde:** [UseModule.cs:251](../Modules/UseModule.cs#L251), [:270](../Modules/UseModule.cs#L270),
  [AdventureModule.cs:788-796](../Modules/AdventureModule.cs#L788-L796).
- **Problema:** el mensaje de pelea que se arma al comer no lleva el campo «✨ Habilidad»: desaparece ese turno y
  vuelve al siguiente, y en celular los botones saltan de lugar. La derrota después de comer no muestra la vida del
  monstruo y la otra derrota sí. El footer del encuentro repite la regla de comer una vez, que ya dice el menú.
- **Propuesta:** que el turno después de comer use el mismo armado de campos que el turno normal; las dos derrotas
  con los mismos campos; footer solo cuando no hay menú.
- **Maqueta** (turno después de comer):

```
ANTES                              DESPUÉS
❤️ Tu HP | 🐗 HP de Jabalí          ❤️ Tu HP | 🐗 HP de Jabalí
[Atacar] [Golpe Brutal] [Huir]     ✨ Habilidad: Golpe Brutal — lista
                                   [Atacar] [Golpe Brutal] [Huir]
```

- **Riesgo:** bajo, sin textos nuevos (se reusan). Arneses: `healtest`, `loctest`, `portraittest`, `stage6test`,
  `v83test`, `v0470test`.

## M15 — Consistencia chica entre pantallas

- **Pantallas:** `/cd`, recetario por texto, vender equipo, `/shop sellall`, resultado del Carpintero, `/info`.
- **Dónde:** [CooldownModule.cs:148](../Modules/CooldownModule.cs#L148) y
  [:401-430](../Modules/CooldownModule.cs#L401-L430), [ForgeModule.cs:42](../Modules/ForgeModule.cs#L42),
  [TabernaModule.cs:322-323](../Modules/TabernaModule.cs#L322-L323), [ShopModule.cs:293-296](../Modules/ShopModule.cs#L293-L296),
  [ToolsModule.cs:223](../Modules/ToolsModule.cs#L223), [HelpModule.cs:95](../Modules/HelpModule.cs#L95),
  [GameModule.cs:210](../Modules/GameModule.cs#L210).
- **Problema:** detalles sueltos que hacen que las pantallas no parezcan del mismo juego:
  - `/cd` es verde azulado y perfil, stats e inventario son naranja; `aa forge recipes` sale gris y sin la foto
    del herrero.
  - Las líneas de Arena y jefe mundial de `/cd` ocupan 2 o 3 renglones en celular.
  - «Sí, venderla / No, me la quedo» también para el amuleto.
  - Los botones de `sellall` no llevan emoji y los de la taberna sí; el resultado del Carpintero no lleva la
    miniatura del ítem y el de la forja sí.
  - `/info` termina con las 28 claves de tema separadas por comas.
  - En inglés, el título del perfil mezcla idiomas (clase y título sin `Loc.Tr`).
- **Propuesta:** un color por familia de pantalla (personaje = naranja, NPC = el suyo), líneas de `/cd` cortas,
  etiquetas por pieza, mismos emojis en botones equivalentes, miniatura en el Carpintero y `/info` cerrando con
  «mirá `/info tema:`» en vez de la lista.
- **Maqueta** (`/cd`):

```
ANTES
🏟️ **Arena**: ¡todavía no te anotaste! ⚠️ Se juega en 1h 35m: /arena join

DESPUÉS
🏟️ **Arena**: ⚠️ sin anotar · 1h 35m · `/arena join`
```

- **Riesgo:** bajo cada punto, pero toca muchos archivos: mejor en tandas. `cdtest` y `v0184test` miran `/cd`;
  `v90test` y `loctest` miran `/info`; `v0380test` y `v0530test`, `sellall`; `toolstest`, el Carpintero. Los
  nombres de clase y los títulos quedan en español por regla («nombres propios»): ese punto del perfil hay que
  confirmarlo con el dueño antes de tocarlo.

---

## Estado

- M1 — hecha (2026-11-12: el ícono pasó al valor en recetario clásico y `/deepshop`; `/open` y el dragón de «Más stats» quedan sin emoji propio en título/nombre. En `/deepshop` el precio sigue en el nombre del campo)
- M2 — hecha (2026-11-14, con el OK del dueño: en la derrota de pelea solo, con el resumen prendido, un tercer campo en línea vacío cierra la fila de las dos vidas y la tablita arranca en una fila nueva, igual que la derrota después de comer. Sin textos nuevos; ningún arnés contaba esos campos)
- M3 — hecha (2026-11-12: `GameHistory.Number` en perfil (XP y vida), tienda, taberna, forja y recetario clásico, logros, misiones, `/open` y victoria en equipo; arneses `loctest`, `stage9test` y `stage10test` actualizados porque fijaban el número sin separador. No se cambió «monedas» por «oro» en `/open`)
- M4 — no se hace (2026-11-14, decisión del dueño): el recetario completo es «UNA LÍNEA por receta agrupada por zona» desde la v0.27.0 (`docs/claude/ui-embeds-emojis-idioma.md`); pasarlo a dos renglones cambia ese diseño
- M5 — hecha en parte (2026-11-14, lo que aprobó el dueño: los encabezados `#` de `/stats` quedan como están, son pedido suyo. Hecho: el footer es una frase («Todo cuenta contra monstruos; en duelos y en la Arena, solo el nivel, el equipo y la clase.»; ya no dice que el perfil muestra los totales ni explica los ➕ del drop) y en «Más stats» va un bono por línea: el dragón («ATQ: +5 %», «Aliento final»…), el Fuego Nuevo (título en negrita y un renglón para EXP, drop de cacería, drop de viaje y recolección) y las bendiciones. El campo de mascotas no se tocó (no estaba en el pedido). Ningún arnés fijaba esos textos; `en.json` al día)
- M6 — hecha (2026-11-14, con el OK del dueño: `/shop view` y la escena de la taberna pasaron de tres columnas en línea a un campo de ancho completo por categoría (Comida, Cajas, Mascotas) con un renglón por ítem, `icono **Nombre**: precio · detalle corto`: «**Mate Amargo**: 8 oro · cura 15 HP», los banquetes «cura 150 HP, +15% ATQ 30 min», las cajas «2.300 oro · entre 1 y 5 ítems» con «🔒 Zona 2» al final si está cerrada, y «para tus mascotas» / «para tu dragón». Sin tocar: el texto de las opciones de los menús de la taberna y el autocompletado de `/shop buy`; no se agregó un aviso de si el oro alcanza. `stage9test`, `stage10test`, `reworktest` y `pettest` actualizados en los chequeos que fijaban las columnas y el orden viejo; `en.json` al día)
- M7 — hecha (2026-11-12: `GameData/FieldText.FitLines` corta por renglón en tienda, recetario clásico y `/dragon`; chequeo nuevo en `v0194test`. Sin hacer, porque cambian qué se muestra: el tope de 25 opciones del menú del Carpintero y el tope de 6.000 de «Todo» en el inventario)
- M8 — hecha (2026-11-12: en `/dragon`, «Criar» pasó a un dato por renglón (chance con amigo, chance solo, los dos contadores de fallos, «más en `/info tema:dragon`») y «Tipo y comida» a tres renglones con `icono **Comida de Dragón**: N`; la invitación dice la regla del tipo en una línea; en el tablón «En otros tiers» va un tier por renglón y se sacó el footer. «Criar» y «Entrenar» ahora pasan por `Loc.T`. Se dejó de mostrar en pantalla el promedio de intentos y el descanso de 12 h (están en `/info tema:dragon` y `/cd`). `dragontest` actualizado en 5 chequeos que fijaban el texto viejo; `en.json` al día)
- M9 — hecha (2026-11-12: `MissionRewards.DescribeLines` / `GoldLine` / `XpLine` / `ItemLine` dan el renglón `icono **Nombre**: 1.234`; lo usan `/open` (ahora «**Oro**: N» en vez de «+N monedas», ítems sin «•» ni «×», trofeos uno por línea), el regreso de expedición (lo que trajo y el hallazgo, un renglón por cosa) y la victoria en equipo (oro y XP en dos renglones con `UiEmojis.Gold/Xp`: el campo de cada jugador creció un renglón). `MissionRewards.Describe` no se tocó (misiones, logros, Arena). Arneses `storytest` y `v0390test` actualizados en un chequeo cada uno; `en.json` al día)
- M10 — hecha (2026-11-12: la invitación quedó con una línea de reglas y «Más en `/info tema:boss`»; en el turno, el estado de cada jugador va con íconos en su renglón de vida (✨ habilidad lista, ⏳ N turnos que le faltan, 🍖 todavía puede comer) y el footer es el reloj del turno más la leyenda de esos íconos. Ya no se ve en el roster el nombre de la habilidad ni los turnos que le quedan a su efecto. Sin tocar: el botón «Habilidad» genérico, que es diseño anotado en `docs/claude/combate.md` (el mensaje es compartido). `partybosstest` actualizado en el chequeo que fijaba «comida usada/lista»; `en.json` al día)
- M11 — hecha (2026-11-13: en `/forge` cada opción del menú es estado + nombre + estadística y lo que falta va en la descripción de la opción («Te falta: 2 Hierro, 30 oro», «Te cuesta N oro», «Primero vendé tu arma equipada», «Ya la llevás equipada»), con `ForgeChoices.MenuFor` nuevo y por `Loc` con una oración por género; leyenda corta «✅ podés · ❌ te falta» en el herrero y «✅ podés · ❌ te falta · 🔒 zona cerrada / Más en `/info tema:forge`» en el Carpintero (su menú ya tenía etiqueta + descripción); se sacaron los dos footers «Lo que te conteste… sale abajo». Sin tocar: `ForgeChoices.For`, el autocompletado de `aa forge make`, que sigue en una línea con «— falta:» y sin `Loc` (no tiene segundo renglón). `stage11test` actualizado en el chequeo que fijaba el footer; `en.json` al día)
- M12 — hecha (2026-11-13: en la mazmorra el footer quedó en «⏱️ 3 min por jugada · sin vida perdés la Reliquia» (uno solo para las dos variantes; el resto de las reglas está en `/info`); las flechas del laberinto van solo con emoji y en el orden ⬅️ ⬆️ ⬇️ ➡️; la vida se llama «❤️ Tu HP» como en las peleas y usa la barra de emoji de 8 piezas (`UiEmojis.BarHp`, con la de texto de respaldo) y los números con separador, también en los dos mensajes de final. No se hace (2026-11-14, decisión del dueño): el historial del ritmo sigue en 15 golpes, porque con un ciclo de 5 bajarlo a 8 le saca información al jugador para leer el patrón (es dificultad, no diseño: lo decide el dueño); el `⌫` del candado no se tocó (hay que probarlo en Discord real). `v0470test` pasó sin cambios; `en.json` al día)
- M13 — hecha (2026-11-13: en `/missions` el premio de cada misión en curso va en su renglón («🎁 …», debajo de la barra y el `n/m`) y el «**Total:**» del recibo pasa por `Loc.F`; en `/achievements` los logros pasaron del campo «Logros» a la descripción; en `/titles` el footer es «Página X/Y · Tenés N de M». El 2026-11-14, con el OK del dueño, la página Hazañas de `/titles` se partió en dos de 12 títulos: «Hazañas I» (clave `hazanas`: cazador, viajero, jefes, recolector) y «Hazañas II» (clave `hazanas2`: herrero, magnate, encantador, explorador); son 11 páginas y cambia la numeración de `aa titles <página>` (9 = Hazañas II, 10 = PvP y comunidad, 11 = Curiosos); `v0230test` y `v0410test` actualizados donde contaban páginas. El footer de logros ya tenía «Página X/Y» y se dejó con su aviso de reclamar. `stage3test` actualizado en 4 chequeos que miraban el campo «Logros»; `en.json` al día)
- M14 — hecha (2026-11-13: el turno después de comer lleva el campo «✨ Habilidad» igual que el turno normal (`AdventureModule.WithAbilityField`, ahora `internal`); la derrota después de comer muestra también la vida del monstruo, y con el resumen prendido cierra esa fila con un tercer campo en línea vacío para que la tablita siga arrancando en una fila nueva (lo fija `loctest`); el encuentro ya no lleva footer cuando hay desplegable de comida (el menú dice «1 vez por pelea»), y lo conserva en `/hunt` y al venir de la mazmorra. `healtest` actualizado en 2 chequeos que fijaban el footer del desplegable; `en.json` al día)
- M15 — hecha (2026-11-13 y 2026-11-14), salvo lo que el dueño dijo que no. Hecho: `/cd` pasó de verde azulado a naranja (como perfil, stats e inventario) y el recetario de `aa forge recipes` de gris a naranja (el color del herrero); la pregunta de vender equipo dice «Sí, venderlo / No, me lo quedo» para el amuleto; los botones de `sellall` llevan los mismos emojis que los de la taberna (🛡️ no, 💰 sí); el resultado del Carpintero lleva la miniatura de la herramienta (`WithItemThumbnail`, como la forja). Ningún arnés fijaba esos textos; `en.json` al día. El 2026-11-14, con el OK del dueño: (a) las líneas de Arena y jefe mundial de `/cd` sin anotarse son un renglón corto que conserva el ⚠️ («🏟️ **Arena**: ⚠️ sin anotar · 1h 35m · `/arena join`», «🌍 **Jefe mundial**: ⚠️ sin anotar · 1h 35m · `/miniboss`»; mirando a otro jugador, sin el comando; la línea de «anotado ✅» no cambió), con `v0240test`, `v90test` y `cdtest` actualizados; (d) `aa forge recipes` lleva la foto del herrero de miniatura (el adjunto viaja por `NpcImages.SendAsync`, como en `aa forge`). NO se hacen, por decisión del dueño: (b) sacar de `/info` la lista de claves de tema; (c) clase y título del perfil en inglés
