# Cajas v2, travel y jefe — diseño y plan (v0.8.0)

> Aprobado por el dueño el 2026-10-05 ("Si perfecto…") con tres ajustes: Mítico en cajas 0,1% por ítem, travel a 30 minutos, jefe/raid a 1 hora (perder o huir deja menos) con un poco más de recompensa. Se ejecuta inline en la misma sesión.

**Objetivo:** que comprar cajas no sea una pavada (precios muy altos, botín con las MISMAS chances que farmear, sin oro fácil), que cada caja diga cuántos ítems trae, que el botín y la compra lleguen solo hasta la zona más alta que el jugador tiene desbloqueada, y reordenar los cooldowns de travel y jefe/raid con su recompensa.

**Arquitectura:** el sorteo de la caja sigue siendo puro (`GameData/BoxLoot.cs`) pero ahora hace N tiradas (N entre `min_items` y `max_items`) y cada tirada es de un tipo: ítem fijo (comida, trofeo, caja de abajo), `gather` (un material de recolección sorteado con las chances de `/chop` y `/mine`), `zone_drop` (un drop de monstruo de una zona ≤ la caja y ≤ la máxima desbloqueada del jugador) u oro (bono raro, el peso es la chance por mil). Un servicio arma el contexto (qué materiales y drops hay, hasta qué zona llegó el jugador). Los datos (rangos, precios, botín) viven en un script final `Database/rework_boxes.sql`.

## Decisiones

**Cantidad de ítems** (el oro no cuenta): Cajón de Pino 1–10 · Baúl de Roble 5–20 · Arcón de Hierro 10–35 · Cofre de Oro 20–60 · Arca del Soberano (premio) 40–100. Se muestra en `/shop`, `/taberna`, `/open` y al abrir.
**Precios** (valor esperado en minutos de farmeo × oro por minuto de la zona, ×0,7 por la eficiencia real de un jugador): Cajón 1.000 · Baúl 10.000 · Arcón 35.000 · Cofre 110.000. **Venta de cajas: 0** (un cofre de jefe no puede venderse por oro).
**Composición por tirada**: recolección 79 / 72 / 70 / 68 % · drops de zona 12 / 15 / 17 / 19 % · comida 6–7 % · trofeos 3–4 % · caja de un escalón menos 0 / 2 / 2 / 2 %. **Oro**: solo un bono raro, 30 por mil (3%) por apertura, de una bolsa de ~10–30% del precio de la caja.
**Chances de recolección = las reales** (68 / 21 / 7 / 3,5 / Mítico) con una sola excepción pedida: **Mítico 0,2% en total (0,1% por ítem: Corteza y Meteorito)** en vez de 0,5%.
**Zona**: la caja es de la zona de su rareza (Común=1 … Legendario=4); los drops de monstruo salen solo de zonas ≤ min(zona de la caja, zona máxima desbloqueada del jugador), con proporción real hunt 3 : hunt 3 : travel 2. Una caja solo se COMPRA si ya desbloqueaste su zona (🔒 si no). "Desbloqueada" = la misma regla de `/zona` (nivel mínimo + jefe anterior derrotado).
**Arca** (solo logros): 40–100 ítems con los ítems fijos de siempre (Corteza 2, Meteorito 2, Zafiro, Ébano, banquetes, Cofre de Oro, 2 trofeos) + recolección y drops de zona + bono de oro 50% de 2.000–6.000.

**Travel**: cooldown 10 → **30 min**; para no frenar el ritmo calibrado de las recetas, la recompensa por pelea pasa de ×10 a **×30** y la chance de drop de 20% a **60%** (mismo oro, XP y drops por minuto).
**Jefe y raid**: cooldown 5 h → **1 h**; **perder, huir o abandonar por timeout deja solo 30 min** (se devuelven 30 al fallar); recompensa **×6 → ×15** (paridad con 1 h serían ×12; +25% como "un poco más").

## Foco de revisión

1. Una caja nunca da drops de una zona que el jugador no desbloqueó, ni de una zona mayor que la suya.
2. El rango dicho ("entre X y Y") es el real: 20000 aperturas nunca salen del rango y los extremos aparecen.
3. Las probabilidades de recolección dentro de una caja coinciden con las de `/chop` y `/mine` (y Mítico 0,1% por ítem).
4. Comprar una caja de una zona bloqueada no cobra nada ni gasta el cooldown de compra.
5. Un cofre de jefe no se puede vender (venta 0) y el cooldown fallido se devuelve UNA sola vez por pelea.
6. `/shop`, `/taberna` y los autocompletados siguen bajo los límites de Discord con los textos nuevos.

---

### Tarea 1: Cooldowns y recompensas (travel y jefe/raid)
**Archivos:** `GameData/CooldownCatalog.cs`, `GameData/CombatRewardCalculator.cs`, `Repositories/IAdventureRepository.cs` + `AdventureRepository.cs`, `Services/BossCooldownExtensions.cs` (nuevo), `Services/CombatSessionService.cs`, `Modules/AdventureModule.cs`, `Modules/UseModule.cs`, `Modules/RaidModule.cs`, `Database/report_recipe_pacing.sql`.
- [x] Arnés: `Travel.Duration == 30 min`, `Boss.Duration == 1 h`, `BossRetryAfterFailure == 30 min`, `TravelRewardMultiplier == 30`, `TravelDropChancePercent == 60`, `BossRewardMultiplier == 15`; la devolución de cooldown (`RefundCooldownAsync`) deja el restante en ≤ 30 min y no se aplica dos veces si ya expiró.
- [x] `CooldownCatalog`: Travel 30 min, Boss 1 h, `BossRetryAfterFailure = 30 min`, `BossFailureRefund = Boss.Duration - BossRetryAfterFailure`.
- [x] `CombatRewardCalculator`: ×30 / 60% / ×15 con la cuenta de paridad en el comentario.
- [x] `IAdventureRepository.RefundCooldownAsync(ulong discordId, string commandName, TimeSpan amount)`: `UPDATE cooldowns SET last_executed_at = last_executed_at - @Amount WHERE discord_id = @DiscordId AND command_name = @CommandName`.
- [x] `BossCooldownExtensions.RefundAfterFailedBossAsync(this IAdventureRepository, string commandName, ulong discordId)`: solo si `commandName == "boss"`, devuelve `CooldownCatalog.BossFailureRefund`. Se llama en: huida y derrota solitarias (`AdventureModule`), derrota por `/use` (`UseModule`), timeout (`CombatSessionService`), y en el raid al huir y al arrasar (`RaidModule`).
- [x] `report_recipe_pacing.sql`: el divisor del travel de 10 a 30 y los comentarios.
- [x] Build, arnés, commit.

### Tarea 2: Núcleo puro de las cajas
**Archivos:** `GameData/BoxLoot.cs`; arnés `scratchpad/reworktest`.
**Produce:** `LootKind { Gold, Item, Gather, ZoneDrop }`; `BoxLootEntry` igual; `BoxDefinition(int BoxItemId, string BoxName, int MinItems, int MaxItems, int TierRank, IReadOnlyList<BoxLootEntry> Entries)`; `GatherCandidate(ItemId, Name, Rarity, Emoji, Type)`; `ZoneDropCandidate(ItemId, Name, Rarity, Emoji, ZoneRank, IsTravel)`; `BoxRollContext(int MaxZoneRank, IReadOnlyList<GatherCandidate> GatherPool, IReadOnlyList<ZoneDropCandidate> ZoneDropPool)`; `BoxLootRoller.Roll(BoxDefinition box, BoxRollContext context, Random rng)`; `BoxLootRoller.GatherWeights` (680/210/70/35/2); `BoxCatalog` con `RangeText(int? min, int? max)` = "entre 1 y 10 ítems".
- [x] Arnés primero (compila-falla): rangos (20000 aperturas dentro de [min,max] y se ven los dos extremos), recolección con las chances reales (± tolerancia) y Mítico ~0,1% por ítem, drops de zona solo de zonas permitidas (`MaxZoneRank` 1 → nunca zona 2+), proporción hunt/travel 3:2, oro como bono por mil, caja sin entradas lanza.
- [x] Implementar el roller y pasar el arnés.

### Tarea 3: Repositorio, servicio y pantallas
**Archivos:** `Models/Item.cs`, `Repositories/ItemSql.cs`, `Repositories/IBoxRepository.cs`, `Repositories/BoxRepository.cs`, `Services/IBoxContextService.cs` + `BoxContextService.cs` (nuevos), `Program.cs` (registro), `Modules/BoxModule.cs`, `Modules/TextCommandModule.Boxes.cs`, `Modules/ShopModule.cs`, `Modules/ShopModule.View.cs`, `Modules/ShopModule` buy gate, `Modules/ItemAutocomplete.cs`, `Modules/TabernaModule.cs`, `GameData/ZoneRanking.cs`.
- [x] `Item.BoxMinItems/BoxMaxItems` (int?) cargados por subconsultas en `ItemSql.SelectColumns`.
- [x] `ZoneRanking.MaxUnlockedRank(zonesOrdered, level, highestZoneCleared)`: la zona más alta con `level >= MinLevel` y sin `PendingGatekeeperZone`.
- [x] `BoxRepository`: leer `min_items`, `max_items`, tier (rango de la rareza) y los tipos `gather` / `zone_drop`.
- [x] `BoxContextService.BuildAsync(discordId)`: pools de recolección (ítems `Madera` y `Mineral`), pool de drops de zona (monstruos hunt y travel con su rango de zona) y `MaxZoneRank` del jugador; cache de 5 min de lo estático.
- [x] `ExecuteOpenAsync` recibe el servicio, arma el contexto una vez y sortea con él; el embed dice "Abriste N× … (de A a B ítems cada una)".
- [x] Compra: `ExecuteBuyAsync` rechaza (sin cobrar ni gastar cooldown) una caja cuya zona no está desbloqueada, con el motivo; `/shop view` y el menú de la taberna muestran "entre X y Y ítems" y 🔒 en las bloqueadas; los autocompletados de comprar y de abrir también dicen el rango.
- [x] Build y arneses de caja/tienda/taberna.

### Tarea 4: Datos — `Database/rework_boxes.sql`
**Archivos:** crear `Database/rework_boxes.sql`; `Database/schema.sql` (columnas `min_items`, `max_items`; `kind` con `gather` y `zone_drop`); `Database/run_fresh_install.sql`; `Database/report_box_economy.sql`.
- [x] El script (idempotente, se verifica solo): agrega columnas y amplía el CHECK, fija rangos, precios (compra 1.000 / 10.000 / 35.000 / 110.000, venta 0), borra y recarga el botín de las 5 cajas con la composición de arriba, y falla en voz alta si algo no coincide (rangos, suma de pesos, cada caja con su `gather`, trofeos que nadie más da todavía obtenibles: los 17).
- [x] `run_fresh_install.sql`: después de `rework_drops_and_recipes.sql`.
- [x] `report_box_economy.sql` reescrito para el modelo nuevo (valor esperado en minutos de farmeo por apertura contra el precio).

### Tarea 5: Verificación, regresión, documentación y publicación
- [x] Copia de la base real + script ×2 (idempotente) y instalación limpia = base migrada (mismo resumen); aplicar a la base real con `pg_dump` antes.
- [x] Arneses de caja/tienda/taberna/misiones actualizados; regresión completa.
- [x] `CLAUDE.md` (Boxes, Shop, Travel/Boss, drops) y `MEJORAS.md`; versión 0.8.0; commit, `develop`, `main` + tag `v0.8.0`.
