# Rework de drops, jefes y recetas — plan de implementación

> **Para quien lo ejecute:** usar superpowers:executing-plans (el dueño pidió "dale" y se ejecuta en esta misma sesión). Los pasos usan casillas `- [ ]`.

**Objetivo:** 20 mobs (2 hunt + 1 travel + 1 jefe por zona), 3 drops de material por zona, el jefe da un cofre (100% la primera vez, 40% después), 6 recetas por zona (4 de clase, 1 general sin boost, 1 amuleto con los 3 drops) y rendimiento de `/chop` y `/mine` escalado por rareza.

**Arquitectura:** casi todo es DATOS: un script final `Database/rework_drops_and_recipes.sql` (idempotente, se verifica solo, va al final de `run_fresh_install.sql`) lleva la base —nueva o viva— al estado final; el código cambia en 3 lugares chicos (chance del cofre del jefe, textos del cofre, escala de rendimiento). Se sigue la convención del repo de scripts que pisan a los anteriores (como `rework_food_catalog.sql`).

**Stack:** C# / .NET 10, Discord.Net, PostgreSQL (Dapper/Npgsql). Sin tests en el repo: se verifica con los arneses del scratchpad (consola contra la DLL y la base real) y con comparaciones de bases.

**Spec:** `docs/superpowers/specs/2026-10-05-drops-y-recetas-rework-design.md` (las 30 recetas con sus cantidades están ahí; las cantidades definitivas se generan del modelo de tiempos y se cargan tal cual en el SQL).

## Restricciones globales

- Texto del juego y comentarios en español; nada de comentarios de más (el repo comenta el porqué).
- `dotnet build` sin errores ni advertencias después de cada cambio de código.
- Boost de clase real: `ClassWeaponSynergy.Multiplier = 1.5` (el spec decía 1,6 por error: con +9 la Zona 1 queda en ~+14, que es lo calibrado).
- Chances de drop SIN tocar: hunt 6%, travel 20%. Boss: 100% la primera vez que el jugador lo derrota, 40% después.
- Rendimiento por rareza (en UN solo lugar, `GatheringYield.RangeFor`): Común 1–5, Raro 1–3, Épico 1–2, Legendario 1, Mítico 1; `RunMultiplier` queda en 1.
- Todo lo de la base viva es reemplazable (el dueño lo avisó): sin reembolsos; respaldo con `pg_dump` antes de aplicar.
- Después del rework NO se vuelven a correr solos `finalize_monster_roster.sql`, `seed_zone_bosses.sql`, `seed_recipes.sql` ni `seed_zoneN_gear_and_recipes.sql` (volverían al estado viejo); `rework_drops_and_recipes.sql` va siempre después.
- Precio de las piezas: venta = MENOR entre 6×stat y 60% del oro de la receta (nunca se gana forjando y vendiendo); compra = 1,3×venta redondeado.

## Foco de revisión (lo que el spec implica y ninguna tarea prueba "de por sí")

1. Un jugador con un amuleto retirado equipado queda sin amuleto y `/profile` sigue andando (la FK es `ON DELETE SET NULL`).
2. Correr el script dos veces seguidas, y sobre una instalación limpia, da la misma base (idempotencia).
3. Cada clase ve exactamente 3 recetas por zona (su arma de clase, la general y el amuleto) en `RecipeCatalog.ViewFor`.
4. `/drops` y `/forge recipes` siguen bajo los 6000 caracteres con los cofres y los nombres nuevos.
5. En un raid, la "primera vez" se evalúa POR participante (uno que ya lo había derrotado no recibe el 100%).
6. El cofre del jefe llega a la mochila como cualquier drop (`ApplyBossVictoryAsync`) y se abre con `/open`.

---

### Tarea 1: Rendimiento por rareza

**Archivos:** `GameData/GatheringYield.cs`, `Database/report_recipe_pacing.sql`; arnés nuevo `scratchpad/reworktest`.
**Produce:** `GatheringYield.RangeFor("Épico") == (1, 2)`; el script de ritmos usa 1,5 de promedio para Épico.

- [ ] **Paso 1 — arnés (falla primero):** crear `scratchpad/reworktest` (consola .NET 10 que referencia `bot_ds_rpg.dll` de `wipbuild`) con `Check("Común 1-5", RangeFor("Común") == (1,5))`, `Raro (1,3)`, `Épico (1,2)`, `Legendario (1,1)`, `Mítico (1,1)` y 2000 tiradas por rareza sin salirse del rango. Correr: falla en Épico.
- [ ] **Paso 2 — código:** en `GatheringYield.RangeFor`, `"Raro" => (1, 3)`, `"Épico" => (1, 2)`, resto `(1, 1)`; actualizar el comentario de arriba (tabla: Común 1–5 prom. 3, Raro 1–3 prom. 2, Épico 1–2 prom. 1,5, Legendario/Mítico 1) y la frase de "las cantidades de las recetas están calibradas".
- [ ] **Paso 3 — script de ritmos:** en `report_recipe_pacing.sql`, `weights`: `('Épico', :we, 1.5)`; actualizar el comentario "(Común 1-5, Raro/Épico 1-3, ...)".
- [ ] **Paso 4:** `dotnet build -o $SP/wipbuild`, correr el arnés (pasa) y commit `Rendimiento de /chop y /mine escalado por rareza`.

### Tarea 2: Cofre del jefe (código)

**Archivos:** `GameData/CombatRewardCalculator.cs`, `Modules/AdventureModule.cs`, `Modules/RaidModule.cs`, `GameData/DropsCatalog.cs`, `Modules/DropsModule.cs`.
**Produce:** `CombatRewardCalculator.RollBossReward(int playerLevel, int monsterGoldBonus, int monsterXpBonus, bool firstClear)`; constantes `BossChestFirstClearPercent = 100`, `BossChestRepeatPercent = 40`, `BossChestChancePercent(bool firstClear)`; se elimina `BossDropChancePercent`.

- [ ] **Paso 1 — arnés:** 3000 llamadas con `firstClear: true` → todas `DroppedSomething`; 20000 con `false` → entre 38% y 42%; `DropsCatalog.BuildZoneBlocks` con un jefe cuyo drop es "Cajón de Pino" → el título del bloque contiene "100%" y "40%", y el texto el nombre del cofre (con emoji si está en el diccionario).
- [ ] **Paso 2 — calculadora:** reemplazar `BossDropChancePercent` y `RollBossReward` por:

```csharp
    // El jefe de zona ya no suelta material sino un COFRE de su zona (monster_drops apunta a la caja): la primera vez que ese jugador lo
    // derrota SIEMPRE cae; las siguientes, 40%. Aplica al combate solitario y al raid (por participante).
    public const int BossChestFirstClearPercent = 100;
    public const int BossChestRepeatPercent = 40;

    public static int BossChestChancePercent(bool firstClear) => firstClear ? BossChestFirstClearPercent : BossChestRepeatPercent;

    public static CombatReward RollBossReward(int playerLevel, int monsterGoldBonus, int monsterXpBonus, bool firstClear) =>
        Roll(playerLevel, monsterGoldBonus, monsterXpBonus, BossRewardMultiplier, BossChestChancePercent(firstClear));
```

- [ ] **Paso 3 — combate solitario:** en `AdventureModule` (bloque `if (turn.MonsterDefeated)`), calcular `firstBossClear` ANTES de tirar la recompensa y pasarlo; borrar el cálculo duplicado de más abajo:

```csharp
                bool firstBossClear = state.CommandName == "boss" && state.BossZoneId is int clearedZone
                    && ((await userRepository.GetByDiscordIdAsync(Context.User.Id))?.HighestZoneCleared ?? 0) < clearedZone;
                var reward = state.CommandName switch
                {
                    "travel" => CombatRewardCalculator.RollTravelReward(state.PlayerLevel, state.MonsterGoldBonus, state.MonsterXpBonus),
                    "boss" => CombatRewardCalculator.RollBossReward(state.PlayerLevel, state.MonsterGoldBonus, state.MonsterXpBonus, firstBossClear),
                    _ => CombatRewardCalculator.RollHuntReward(state.PlayerLevel, state.MonsterGoldBonus, state.MonsterXpBonus),
                };
```
  y en `BuildVictoryEmbed`, el campo del drop: si `droppedItem.Type == "Caja"` el título es `"🎁 ¡Cofre del jefe!"` y el valor `"{ItemDisplay.Format(...)} — abrilo con `/open`"`; si no, el de siempre.
- [ ] **Paso 4 — raid:** en `RaidModule.ResolveVictoryAsync`, dentro del `foreach`, antes del `RollBossReward`:

```csharp
            bool firstClear = ((await userRepository.GetByDiscordIdAsync(participant.DiscordId))?.HighestZoneCleared ?? 0) < session.ZoneId;
            var reward = CombatRewardCalculator.RollBossReward(participant.Level, session.BossGoldBonus, session.BossXpBonus, firstClear);
```
  y en `BuildVictoryEmbed` (raid) agregar `" — abrilo con /open"` a `dropLine` cuando `drop.Type == "Caja"`.
- [ ] **Paso 5 — `/drops`:** en `DropsCatalog.AddBlock` recibir el texto de la chance ya armado: `"{chance}% al ganar"` para hunt y travel y `"cofre: {First}% la 1.ª vez, {Repeat}% después"` para el jefe; en `DropsModule.BuildDropsMessageAsync` el diccionario incluye `Material` **y** `Caja` (`(await GetAllByTypeAsync("Material")).Concat(await GetAllByTypeAsync("Caja"))`).
- [ ] **Paso 6:** build sin advertencias, el arnés pasa, commit `El jefe da un cofre de su zona (100% la primera vez, 40% después)`.

### Tarea 3: Script de datos `rework_drops_and_recipes.sql`

**Archivos:** crear `Database/rework_drops_and_recipes.sql`; modificar `Database/run_fresh_install.sql` (entre `seed_boxes.sql` y `update_item_emojis.sql`; actualizar el conteo "20" de scripts); el generador queda en el scratchpad (`gen_rework_sql.js`, lee el modelo de tiempos `newrecipes.js json`).
**Produce:** la base en el estado final del spec; falla en voz alta si algo no coincide.

Estructura (en este orden, todo en una transacción por bloque `DO`/sentencias re-ejecutables):
- [ ] **Paso 1 — encabezado y chequeo previo:** comentario con el porqué y el "no re-correr los seeds viejos solos"; `DO $$` que verifica que existan las 5 zonas, los 5 cofres (`boxes`) y los 30 ítems de resultado + todos los ingredientes.
- [ ] **Paso 2 — Zona 1:** `DELETE FROM monsters WHERE name = 'Perro Cimarrón' AND zone_id = (SELECT zone_id FROM zones WHERE name = 'Praderas del Mate');`.
- [ ] **Paso 3 — jefes → cofres:** tabla temporal `boss_chest(boss_name, chest_name)` = Rey Jabalí→Cajón de Pino, Lobisón Alfa→Baúl de Roble, Capataz de Hierro→Arcón de Hierro, Señor del Volcán→Cofre de Oro, Soberano de la Escoria→Cofre de Oro; `DELETE FROM monster_drops` de esos jefes y `INSERT` del cofre.
- [ ] **Paso 4 — piezas:** tabla temporal `gear(result_name, kind, stat, gold)` con las 30 (ATQ base 9/20/32/50/80, DEF 10/18/30/46/75, oro del spec); `UPDATE items` con `stat_value`, `sell_price = LEAST(6*stat, gold*6/10)` y `buy_price = ROUND(sell_price*1.3)`; `UPDATE items SET weapon_family = NULL` para Hoja de Acero Puro, Cuchilla de Cenizas, Pico de Minero Reforzado, Lanza de Magma y Martillo del Titán.
- [ ] **Paso 5 — recetas:** `INSERT ... ON CONFLICT (result_item_id) DO UPDATE` de las 30 (`gold_cost`, `zone_id`, `affinity`), `DELETE FROM recipes` de todo resultado que no sea de las 30, y reemplazo de los ingredientes (borrar los de esas 30 y cargar los nuevos con `ON CONFLICT (recipe_id, item_id) DO UPDATE`).
- [ ] **Paso 6 — retirar ítems:** `RAISE NOTICE` de cuántas unidades y cuántos jugadores los tienen/equipan, y `DELETE FROM items WHERE name IN (...)` de Colmillo de Cimarrón, Colmillo del Rey Jabalí, Pelaje Plateado del Alfa, Yunque del Capataz, Brasa Eterna, Corazón del Soberano, Amuleto del Levantador, Mate Tallado en Cenizas, Casco de Capataz, Coraza de Escamas Ígneas, Égida del Devorador (cascada: mochilas, recetas, drops; `users.weapon_id/amulet_id` quedan en NULL).
- [ ] **Paso 7 — verificación (`RAISE EXCEPTION` si falla):** 20 monstruos; cada zona con exactamente 2 hunt + 1 travel + 1 jefe; cada hunt/travel con 1 drop de tipo `Material` y cada jefe con 1 drop de tipo `Caja`; 30 recetas, 6 por zona (4 de afinidad, 1 general, 1 amuleto); cada receta con exactamente los ingredientes de la tabla; los 5 generales con `weapon_family` NULL y las 20 armas de clase con familia y `class_requirement`; ningún ítem retirado existe.
- [ ] **Paso 8:** agregar la línea `\ir rework_drops_and_recipes.sql` (con su comentario) a `run_fresh_install.sql`; commit `Script de datos del rework de drops y recetas`.

### Tarea 4: Probar el script sobre copias (sin tocar la base viva)

**Archivos:** ninguno nuevo (bases de práctica `asado_rework_live`, `asado_rework_fresh`).

- [ ] **Paso 1:** `pg_dump -Fc` de la base real → `pg_restore` a `asado_rework_live`; correr el script ahí **dos veces** (la segunda no cambia nada y no falla).
- [ ] **Paso 2:** crear `asado_rework_fresh` con `run_fresh_install.sql` (que ya trae el script al final).
- [ ] **Paso 3:** `fresh_compare.sql` contra las dos: tienen que dar **idéntico** (tablas, columnas, restricciones, ítems, zonas, monstruos, drops, recetas, cajas, buffs).
- [ ] **Paso 4:** `report_recipe_pacing.sql -v ph=0.06 -v pt=0.20` sobre la copia y compararlo con el modelo (`newrecipes.js`): mismos minutos (general ~100, clase ~150, amuleto ~200).
- [ ] **Paso 5:** consultas de foco: un jugador de prueba con `Amuleto del Levantador` equipado queda con `amulet_id` NULL; ningún `box_loot` quedó huérfano; `TrophyTotal` (17) igual a los materiales sin monstruo que aparecen en cajas.

### Tarea 5: Aplicar a la base real

- [ ] **Paso 1:** `pg_dump -Fc` de la base real a `scratchpad/backup_antes_rework.dump` (y se verifica que no está vacío).
- [ ] **Paso 2:** `psql -v ON_ERROR_STOP=1 -f rework_drops_and_recipes.sql` contra la real; leer los `NOTICE` (quién perdió qué).
- [ ] **Paso 3:** repetir la comparación con una instalación limpia: idénticas. Borrar las bases de práctica.

### Tarea 6: Arneses y regresión

**Archivos:** los de `scratchpad` que dependen de recetas, jefes o `RollBossReward` (`choicetest`, `stage6`, `stage7`, `stage13`, `stage2a`, `stage3`, `stage9`, pruebas de jefe y de raid, `healtest`, `embedtest`).

- [ ] **Paso 1:** `dotnet build` y correr todos; cada falla se clasifica: expectativa vieja (se actualiza el arnés) o bug real (se arregla el código).
- [ ] **Paso 2:** agregar al arnés `reworktest` los chequeos de foco: cada clase ve 3 recetas por zona (`RecipeCatalog.ViewFor` con las recetas de la base), `/drops` y `/forge recipes` < 6000 caracteres, un jefe derrotado por primera vez siempre da cofre y entra a la mochila, `/hunt` en Zona 1 sale de un pool de 2.
- [ ] **Paso 3:** todo verde y commit `Arneses al día con el rework`.

### Tarea 7: Emojis, documentación y versión

**Archivos:** `Database/update_item_emojis.sql`, `CLAUDE.md`, `MEJORAS.md`, `docs/superpowers/specs/2026-10-05-drops-y-recetas-rework-design.md`, `bot_ds_rpg.csproj`.

- [ ] **Paso 1:** sacar de `update_item_emojis.sql` las líneas de los 6 materiales y 5 amuletos retirados (y actualizar los conteos: Material 32/32, Amulet 7/11 con 5 que quedan) y correr el script contra la base real (los emojis siguen bien).
- [ ] **Paso 2:** `CLAUDE.md`: Zones (4 mobs por zona), Drops (3 por zona, jefe = cofre, chances), Forge recipes (6 por zona, 3 visibles, general sin boost, amuleto único con los 3 drops), Boxes (cofre del jefe), `/chop` y `/mine` (escala por rareza), lista de scripts de `Database setup` (+ `rework_drops_and_recipes.sql`, y la advertencia de no re-correr los viejos solos).
- [ ] **Paso 3:** `MEJORAS.md` (entrada del rework) y corregir el spec: boost ×1,5 y la escala de rendimiento.
- [ ] **Paso 4:** `<Version>` a `0.7.0`; build; commit `v0.7.0: rework de drops, jefes y recetas`.

### Tarea 8: Publicar

- [ ] **Paso 1:** worktree `wip` → `develop` (fast-forward) → push.
- [ ] **Paso 2:** el repo ya despliega `main`: llevar `develop` a `main` con el tag `v0.7.0` y push (el dueño pidió hacer el rework antes del deploy en Railway).
- [ ] **Paso 3:** avisar qué reiniciar, qué pierden los jugadores y que sigue el deploy en Railway.
