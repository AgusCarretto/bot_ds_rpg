# Asado y Acero RPG — Estado y mejoras pendientes

_Última revisión: 2026-10-01 (v0.5.0: listo para desplegar en cualquier hosting)_

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

- Sistema de armadura/defensa (hoy el daño recibido no depende de ningún stat defensivo).
- Comandos de administrador (dar oro/ítems, resetear cooldowns) para moderar el server de pruebas.
- PvP o eventos temporizados de servidor (boss compartido, etc.) — mencionado como "idle" en el
  brief original, esto sería la primera mecánica no-idle si se agrega.
