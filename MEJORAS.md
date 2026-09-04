# Asado y Acero RPG — Estado y mejoras pendientes

_Última revisión: 2026-09-03 (loot separado por fuente + equipo por clase)_

## Pendiente de acción tuya (2026-09-03, tarde)

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

- **`items.emoji`** (columna nueva, `Database/add_item_emoji.sql`): emoji personalizado de Discord
  por ítem ("<:nombre:id>"), mostrado en todo lugar que muestre nombres de ítems (`/profile`,
  `/inventory`, `/shop`, `/forge recipes`/`make`, `/equip`, `/use`, drops de `/hunt`/`/travel`/
  `/autohunt`/`/chop`/`/mine`) vía `GameData/ItemDisplay.Format`. Cargá los códigos por nombre
  (`Database/update_item_emojis_batch1_materiales.sql` ya tiene los 4 de Madera reales, el resto
  en placeholder) — no por `item_id`, que puede variar entre instalaciones.
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
