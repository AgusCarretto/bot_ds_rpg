# Zonas 6 a 10 y mazmorras — plan de construcción

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** zonas 6 a 10 que se abren con el Fuego Nuevo, cada una con un monstruo, un drop, su equipo y su Reliquia; salir de cada una es una mazmorra-acertijo (solo, sin comida); la mazmorra de tu zona tope te mete directo contra un Asador escalado.

**Architecture:** una columna `zones.min_fuego_nuevo` y UNA función, `ZoneRanking.ForPlayer(zones, fuegoNuevo)`, que filtra la escalera por jugador; todo lo que hoy pasa una lista de zonas «del jugador» la pasa filtrada, así `IsGateOpen`, `MaxUnlockedRank` y `PendingGatekeeperZone` siguen siendo las mismas reglas. Las mazmorras son motores puros (`GameData/Dungeons/*.cs`) que reciben acciones y devuelven estado; una sesión en memoria (`Services/DungeonService.cs`, mismo modelo de lock que el blackjack) y un módulo de botones (`Modules/DungeonModule.cs`). La pelea del Asador después de la mazmorra es un combate solitario normal (`CombatState`) armado por el starter sin cobrar cooldown, con stats por zona tope, sin comida y con un golpe especial al 20 %.

**Tech Stack:** C#/.NET 10, Discord.Net, Dapper/Npgsql, PostgreSQL; arneses de consola en `tests/`.

**Spec:** `docs/superpowers/specs/2026-11-01-zonas-6-a-10-mazmorras-design.md`

## Global Constraints

- Zona tope por FN: 6 en el FN 5, 7 en el 8, 8 en el 11, 9 en el 14, 10 en el 17 (`min_fuego_nuevo` 5/8/11/14/17).
- Del FN 0 al 4 no cambia nada (el jefe de la Zona 5 abre la Zona 0 con Trinche y Brasa).
- Un solo monstruo de caza y un solo drop por zona; el drop con la chance de siempre (`HuntDropChancePercent`); sin mascotas nuevas.
- Mazmorra: solo, sin comida, cada error saca vida, 0 de vida = perdida; perder = la Reliquia se pierde; se sortea cada corrida.
- Asador desde la mazmorra: sin curarse antes ni durante, con la vida que dejó la mazmorra; 20 % de golpe especial sin cooldown; sin Trinche/Brasa.
- Todo texto con `Loc` y su inglés en `Locales/en.json` (`tools/loc --check` en 0); números por `GameHistory.Number`.
- Cada ítem/receta/zona nueva en un script de `Database/` re-ejecutable, metido en `run_fresh_install.sql`; `schema.sql` con la columna nueva.
- Nada se sube a main ni se corre en Railway en esta tanda.

## Review Focus

1. Un jugador de FN 0-4 con la escalera completa cargada: el Fogón tiene que seguir abriéndose con el jefe de la Zona 5 (`IsGateOpen` con la lista SIN filtrar diría que nunca). Test en la Tarea 2.
2. Doble click / dos `aa boss` a la vez al entrar a la mazmorra: una sola sesión, una sola Reliquia gastada. Test en la Tarea 5.
3. Bot reiniciado o tiempo vencido en plena mazmorra: la Reliquia se descuenta solo al terminar (un reinicio no la gasta); un tiempo vencido cuenta como perdida. Test en la Tarea 5.
4. Un jugador de FN ≥ 5 parado en la Zona 0 de antes (in_gate) con Trinche y Brasa: `/boss` no tiene que pelear al Asador por el camino viejo. Test en la Tarea 7.
5. Comer en la pelea del Asador desde la mazmorra (`/use`, el desplegable, `/heal`): rechazado. Test en la Tarea 7.

---

### Task 1: La escalera por Fuego Nuevo (puro + columna)

**Files:** Modify `Models/Zone.cs`, `Repositories/ZoneSql.cs` (o donde esté `ZoneSql.SelectColumns`), `GameData/ZoneRanking.cs`, `Database/schema.sql`; Create `Database/add_zones_6_10.sql` (primera parte: la columna); Test `tests/v0470test/`.

**Interfaces — Produces:** `Zone.MinFuegoNuevo` (int); `ZoneRanking.ForPlayer(IReadOnlyList<Zone> zones, int fuegoNuevo) -> IReadOnlyList<Zone>` (filtra `MinFuegoNuevo <= fuegoNuevo` y ordena por dificultad); `ZoneRanking.TopZone(...)`; `ZoneRanking.IsDungeonZone(Zone) => MinFuegoNuevo > 0`; `ZoneRanking.NextLockedByFuegoNuevo(zones, fn) -> Zone?` (para el teaser de `/zonas`).

- [ ] Tests: ForPlayer con FN 0/4/5/7/8/17 da 5/5/6/6/7/10 zonas; IsDungeonZone falso para 1-5; `FogonRules.IsGateOpen(ForPlayer(all, 0), zona5)` verdadero con la lista completa de 10 zonas.
- [ ] Implementar, columna con `ADD COLUMN IF NOT EXISTS ... DEFAULT 0`, compilar, correr el arnés.

### Task 2: Pasar la escalera filtrada en todos lados

**Files:** los ~30 llamados a `IZoneRepository.GetAllAsync` que razonan «para un jugador» (GameModule, RaidModule, ToolsModule, BoxContextService, ZoneModule, ZoneAutocomplete, ForgeModule, ForgeAutocomplete, ForgeSpecialModule, FarmAdviceService, BlacksmithModule, DeepShopModule, StoryModule, TitlesModule, AdventureCombatStarter, PlayerBonusService, ProgressNotifier, MissionsModule, WorldBossService, ArenaService, MiniEventService, DropsModule); `Repositories/AdventureRepository.cs` (las dos subconsultas «última zona normal» pasan a «zona tope por `fuego_nuevo`»); `GameData/FogonRules.cs` (`GateNeedsKeyGear(topZone)`: solo si la zona tope no es de mazmorra); `Modules/ZoneModule.cs` (`/zona N` de una zona por encima del tope: «se abre con el Fuego Nuevo N»; `/zona 0` con tope de mazmorra: «se entra pasando la mazmorra»; `/zonas` muestra la próxima bloqueada).

- [ ] Tests (v0470test, base real con usuarios descartables): FN 0 le gana a la Zona 5 → `GateEvent.Opened`; FN 5 le gana a la Zona 5 → `None` y sube `highest_zone_cleared`; `/zona 6` con FN 4 rechazado con el texto del FN; con FN 5 y la 5 vencida, aceptado; Asador vencido con FN 5 te devuelve a la Zona 6, no a la 10.
- [ ] Implementar y correr el arnés + `stage11test fntest healtest v0254test`.

### Task 3: Los datos de las zonas 6 a 10

**Files:** `Database/add_zones_6_10.sql` (zonas con `OVERRIDING SYSTEM VALUE`, monstruos, ítems, recetas, `zone_boxes`, re-ejecutable y con verificación final), `Database/run_fresh_install.sql`, `Database/update_item_emojis.sql` (nada: sin arte todavía), `GameData/MissionRewards.cs` (`GoldPerHunt` a 10 valores), `GameData/WorldBoss.cs` (`WeaponLadder` a 10), `GameData/PvpTuning.cs` (filas de los 5 amuletos nuevos), `tools/balance2/PartN.cs` (calibración).

**Decisiones [mías]:** niveles 30/35/40/45/50; arma de clase ATQ 180/270/400/600/900, amuleto DEF 165/215/280/365/475; un monstruo de caza y uno de travel por zona; tipo de ítem nuevo `Reliquia` (no se vende, no se desmantela, no sobrevive al FN); la Reliquia pide 1 Cofre de Escoria + drops + muchos materiales; `zone_boxes` `travel` = Cofre de Escoria al 15 %, los demás roles copian los de la Zona 5; oro de una cacería ×1,5 por zona (≈560/840/1260/1890/2840).

- [ ] PartN: con el equipo de la zona anterior al nivel mínimo una pelea común cuesta ~40 % de la vida; con el de la zona, ~5-8 %; el travel es el común ×1,25 vida ×1,1 daño. Escribir los números en el script.
- [ ] Correr el script contra la base local dos veces (re-ejecutable), `report_recipe_pacing.sql` para las recetas nuevas, arnés con conteos.

### Task 4: Motor de mazmorras + El patrón del fuego (puro)

**Files:** Create `GameData/Dungeons/DungeonRules.cs`, `GameData/Dungeons/FirePattern.cs`; Test `tests/v0470test`.

**Interfaces — Produces:** `enum DungeonKind { FirePattern, SmokeMaze, Embers, Lock, Rhythm }`; `DungeonRules.ForZoneRank(int rank) -> DungeonKind?` (6..10); `DungeonRules.ErrorDamage(int maxHp, DungeonKind) -> int`; `interface IDungeonPuzzle { DungeonStep Apply(string action); bool Solved; string Render(); IReadOnlyList<DungeonButton> Buttons(); }`; `FirePattern(Random rng)` — 4 pisos, secuencia tipo Simón que se alarga de 3 a 6 con 🔥💧🪨; se memoriza (fase «mirar» con botón «Listo») y se contesta en orden con el contraataque (💧 apaga 🔥, 🪨 frena 💧, 🔥 derrite 🪨); un error saca vida y repite la secuencia del piso.

- [ ] Tests con `Random` fijo: secuencia crece con el piso y cada piso extiende el anterior; respuesta correcta avanza; error = `DungeonStep.Mistake`; 4 pisos = `Solved`.

### Task 5: La sesión de mazmorra (memoria + base)

**Files:** Create `Services/DungeonService.cs`, `Repositories/DungeonRepository.cs`; Modify `Services/FightGuard.cs` (una mazmorra cuenta como pelea), `Services/AdventureCombatStarter.cs` (no se arranca otra pelea en plena mazmorra), `Program.cs` (DI).

**Interfaces — Produces:** `IDungeonService.StartAsync(ulong id, ulong channelId) -> DungeonStartResult` (chequea zona de mazmorra, Reliquia, cooldown del jefe, vida, nivel; reclama el cooldown con su reintento de 30 min); `ActAsync(ulong id, string action) -> DungeonActResult`; `IsInDungeon(ulong id)`; `IDungeonRepository.FinishAsync(id, zoneId, won, hpDeltaReal, ...)`: UNA transacción que gasta la Reliquia (guardada), aplica la vida y, si ganó una zona intermedia, paga la victoria con `ApplyBossVictoryAsync`-equivalente (sube `highest_zone_cleared`, completa el cooldown, premio [mío]: 40 cacerías de oro de la zona + 10 % de nivel + la caja `repeat`).
- [ ] Tests: dos `StartAsync` a la vez → una sesión; sin Reliquia → rechazo sin cooldown; perder → Reliquia gastada, vida aplicada, la zona no sube; ganar intermedia → zona siguiente abierta, cooldown entero; tiempo vencido = perdida.

### Task 6: Pantalla y comandos de la mazmorra

**Files:** Create `Modules/DungeonModule.cs`, `Modules/TextCommandModule.Dungeon.cs`; Modify `Modules/AdventureModule.cs` y `Modules/TextCommandModule.Combat.cs` (`/boss` en zona de mazmorra la arranca), `Modules/RaidModule.cs` (`/boss @amigo` en zona de mazmorra: «la mazmorra se hace solo»), `GameData/FightLock.cs` (permitir `dungeon`).
- [ ] Botones `dg:{owner}:{action}`; texto `aa dg <acción>`; arnés con el `InteractionService` real para el ruteo.

### Task 7: El Asador desde la mazmorra

**Files:** Create `GameData/AsadorScaling.cs`; Modify `Services/AdventureCombatStarter.cs` (`PrepareAsadorFromDungeonAsync(ulong id, int topRank)`, sin cooldown, `BossZoneId = 0`, `NoHeal = true`, `MonsterSpecialChancePercent = 20`), `Services/ICombatSessionService.cs` (`CombatState.NoHeal`, `MonsterSpecialChancePercent`), `Modules/AdventureModule.cs` (la llamarada en el turno: el golpe de ese turno ×1,75, con su línea), `GameData/CombatHeal.cs` y `Modules/UseModule.cs` (sin comida con `NoHeal`), starter `PrepareBossAsync` (en el Fogón con tope de mazmorra: rechazo).
- [ ] PartN: Asador por zona tope con el equipo de esa zona, nivel mínimo + 3, 80 % de vida, sin comida, con habilidad: ~30 % de derrota.
- [ ] Tests de los puntos 4 y 5 del Review Focus.

### Task 8: Ayuda, traducciones, documentación (Zona 6)
- [ ] Campo nuevo en `/info tema:fogon` y `zone` (sin tema nuevo: ya hay 28), inglés, CLAUDE.md, MEJORAS, anuncio de top.gg; suite entera.

### Tasks 9-12: Laberinto de humo, Las brasas, El Candado del Fogón, El ritmo del Asador
Cada una: motor puro `GameData/Dungeons/<X>.cs` que implementa `IDungeonPuzzle`, tests con `Random` fijo (que siempre tenga solución, que el error saque vida, que se resuelva), su render y sus botones (máximo 5 filas de 5), y su fila en `DungeonRules.ForZoneRank`.
- Laberinto (Zona 7): 5×5 con niebla, generado con DFS (siempre hay camino), trampas, pasos contados.
- Brasas (Zona 8): 4×4 tipo «luces», desordenado desde todo prendido (siempre se puede), cada 3 toques el viento apaga una al azar y quema.
- Candado (Zona 9): clave de 4 de 5 símbolos, 8 intentos, «bien puestos / están pero en otro lugar».
- Ritmo (Zona 10): ciclo oculto de 4 golpes de 3 tipos; adivinar el próximo; 6 aciertos seguidos ganan.
