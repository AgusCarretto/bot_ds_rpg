# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

"Asado y Acero RPG" — a text-based RPG Discord bot (Spanish, Argentine-themed). C#/.NET 10,
Discord.Net (slash commands via `InteractionService` + legacy text commands via `CommandService`),
PostgreSQL via Dapper/Npgsql (no EF Core). Code comments and all user-facing text are in Spanish.

## Commands

```
dotnet build              # compile — do this after any change, nothing else validates the code
dotnet run                # start the bot (needs .env or User Secrets configured, see below)
```

There are no automated tests in this repo (see `MEJORAS.md` — acknowledged gap, not an oversight
to silently fix). `dotnet build` succeeding is the only mechanical check available.

### Configuration

Token/connection string load order: `.env` (gitignored, local dev) → User Secrets → real env vars.
Required keys (double underscore = config hierarchy separator): `Discord__Token`,
`Discord__TestGuildId` (optional — omitting it registers commands globally, which takes up to 1h
to propagate instead of instantly), `Postgres__ConnectionString`. See `.env.example`.

### Database setup (fresh machine)

`Database/schema.sql` is self-sufficient for a brand-new database (creates every table/column/
extension currently in use). Run in this exact order against an empty database:

```
schema.sql → seed.sql → add_weapon_family.sql → seed_class_gear_and_monster_drops.sql
  → seed_consumables_and_base_swords.sql → seed_zones_and_monsters.sql → seed_zone_bosses.sql
  → seed_travel_monsters.sql → finalize_monster_roster.sql
  → seed_recipes.sql → seed_zone2_gear_and_recipes.sql → seed_zone3_gear_and_recipes.sql
  → seed_zone4_gear_and_recipes.sql → seed_zone5_gear_and_recipes.sql
  → finalize_consumable_catalog.sql → remove_legacy_consumables.sql → rebalance_consumable_prices.sql
  → rework_food_catalog.sql → seed_boxes.sql → update_item_emojis.sql
```

`seed_recipes.sql` must come after every seed that creates items it uses: a recipe whose result or ingredient
item doesn't exist yet would be silently skipped (or created *without* that ingredient), so the seed verifies
itself and raises instead. (`seed_zone_bosses.sql` used to be missing from this list, so a fresh install had no
zone bosses at all.)

`Database/run_fresh_install.sql` runs all twenty in this exact order in one shot via `psql` (or
pgAdmin's "PSQL Tool", NOT its plain Query Tool — both need real `psql`, since it uses the `\ir`
meta-command) — **only against a genuinely empty database**, never against one with existing data
(see below, several of these are not safe to re-run).

The other `Database/add_*.sql` / `cleanup_*.sql` / `fix_*.sql` files are historical incremental
migrations for databases that predate `schema.sql` catching up to include everything inline —
**do not run them against a fresh install**, `schema.sql` already has their end state. They matter
only if you're patching an old, already-running database that hasn't been recreated from scratch.

`seed.sql`, `add_weapon_family.sql`, and `seed_class_gear_and_monster_drops.sql` insert
unconditionally and will duplicate rows if run twice against a database that already has their
data (their own header comments predate `items.name UNIQUE`, so they undersell it — with that
constraint now in schema.sql, their `ON CONFLICT DO NOTHING` inserts are actually idempotent too,
but don't rely on that for the `UPDATE`/data-shape parts). Everything else in the order above
(`seed_consumables_and_base_swords.sql`, `seed_zones_and_monsters.sql`, `seed_zone_bosses.sql`,
`seed_travel_monsters.sql`, `finalize_monster_roster.sql`, `seed_recipes.sql`, `seed_zoneN_gear_and_recipes.sql` (N = 2..5), `finalize_consumable_catalog.sql`, `remove_legacy_consumables.sql`, `rebalance_consumable_prices.sql`, `rework_food_catalog.sql`, `seed_boxes.sql`, `update_item_emojis.sql`) is
safe to re-run.

**Known recurring problem**: catalog items have repeatedly been added by hand directly to a
Postgres instance on one machine and never captured in a script, so a fresh install on another
machine silently ends up missing items (this happened at least twice — weapons and the entire
Consumable catalog; and a third time with Carbón, hand-edited from Común to Raro on the live DB while the seed kept
creating it Común, which on a fresh install would have doubled Hierro's `/mine` rate and unbalanced the recipes). If you
add/edit `items` rows by hand, write the INSERT into a new `Database/*.sql` file in the same session, or the next fresh
install will be short again. **Way to check, and worth doing after any catalog change**: create a scratch database, run
`run_fresh_install.sql` against it, and diff a few `concat_ws(...)` summary queries (items, recipes, monsters, boxes, buffs)
between it and the live one — they must be identical (that comparison is how the Carbón drift was found).

## Architecture

**Layering**: `Modules/` (Discord command handlers) → `Repositories/` (Dapper + SQL, one per
aggregate: `IUserRepository`, `IItemRepository`, `IInventoryRepository`, `IShopRepository`,
`ICraftingRepository`, `IRecipeRepository`, `ICasinoRepository`, `IAdventureRepository`,
`IGatheringRepository`, `ICooldownRepository`, `IProgressionRepository`, `IGameEventRepository`, `ITransferRepository`,
`IBoxRepository`, `IBuffRepository`, `IMissionRepository`, `IAchievementRepository`) + `Services/` (stateful
or pure-logic helpers that don't touch SQL directly) + `GameData/` (pure calculators/catalogs, no
I/O — `LevelingCalculator`, `RarityCatalog`, `CombatMath`, `ClassPassives`, etc.). There is no
"DatabaseService" god-object — each repository owns its own slice of the schema.

**Dual command surface**: every player-facing feature is exposed twice — as a slash command
(`Modules/*Module.cs`, `InteractionModuleBase<SocketInteractionContext>`) and as a text command
with prefix `"aa "` (`Modules/TextCommandModule*.cs`, partial class split by domain, `ModuleBase
<SocketCommandContext>`). The text-command side never reimplements logic: it calls the exact same
static `Execute*Async` methods and public `Build*Embed` methods that the slash module exposes, so
behavior and embeds stay identical. When adding a new player-facing command, follow this pattern:
put the real logic in a `public static` method on the slash module (parameterized, no `Context`
dependency) and have both the slash handler and the `TextCommandModule` partial call it.

**Transactions**: every operation touching gold, inventory, or cooldowns opens its own
`DbConnection`/`DbTransaction` explicitly (not `IDbConnection` — see `Data/IDbConnectionFactory.cs`)
and either commits once at the end or rolls back on any failure/precondition-not-met. Two recurring
patterns, both in `Repositories/TransactionalHelpers.cs`:
- **Guarded update**: `UPDATE ... WHERE <precondition still holds> RETURNING ...` — if the
  precondition (enough gold, enough items, cooldown expired) fails, zero rows come back and no
  read-then-write race is possible. Used for cooldown claims (`CooldownGuard`), gold spends, item
  decrements.
- **`SELECT ... FOR UPDATE`** — used when the update needs a computed value from the current row
  first (leveling curve, HP delta), e.g. `LevelingApplier.ApplyAsync`.

Never add a check-then-act sequence across two separate statements without one of these guards —
that's exactly the double-click/concurrent-execution bug class this schema is designed to prevent.

**Item catalog is data-driven, not code-driven**: `items.type` (`Weapon`, `Amulet`, `Consumable`,
`Madera`, `Mineral`, `Material`) and `items.weapon_family` (`Espadas`/`Dagas`/`Arcos`/`Grimorios`,
matching `ClassCatalog.WeaponType`) drive gameplay — `/chop` and `/mine` only ever drop their own
type, `/hunt`/`/travel` drops are `type = 'Material'` exclusively (never Madera/Mineral, never
Weapon/Amulet), `/shop` only lists/sells `type = 'Consumable'`, weapon damage gets a synergy
multiplier only when `weapon_family` matches the player's class. Forge recipes also live in the
database (`recipes` + `recipe_ingredients`, referencing `items` by id), not in code — there used to
be a `GameData/CraftingCatalog.cs`, it was deleted in favor of `Repositories/RecipeRepository.cs`.

**Zones**: the world is split into difficulty-scaled zones (`zones` table, `Repositories/IZoneRepository.cs`).
Zone IDs are shown to players ("Zona 2"), so they should be consecutive 1..N in difficulty order; a failed
insert burns identity numbers and leaves gaps — `Database/renumber_zones_consecutively.sql` fixes an existing DB
(it rewrites every column that stores a zone id, so any *new* column holding a zone id must be added to it).
Each player has `users.current_zone_id` (default 1); `/zona [id]` moves them after validating
`zones.min_level`, `/zonas` lists them. `/hunt`, `/travel` and `/boss` are all zone-scoped, and every
monster lives in the DB (`monsters` + `monster_drops`, `Repositories/IMonsterRepository.cs`; nothing is
hardcoded in `MonsterCatalog` anymore). A zone has three *mutually exclusive* kinds of monster
(`monsters.is_boss` / `monsters.is_travel`, `CHECK (NOT (is_boss AND is_travel))`): the `/hunt` pool
(`PrepareHuntAsync`, **3 in zone 1 and 2 in zones 2–5**), the boss (`/boss`, `/raid`) and **one dedicated
`/travel` monster per zone** (`PrepareTravelAsync`, `Database/seed_travel_monsters.sql`). The travel monster
is the average of that zone's commons at HP ×1.25 / damage ×1.1 (an élite, not a boss — measured with the
real `CombatTurnResolver`, see the seed header), and `CombatRewardCalculator.RollTravelReward` pays the
**whole `/hunt` formula ×10** (`TravelRewardMultiplier` — what the 10-minute cooldown is worth in hunts). So
travel rewards follow the zone ladder automatically: a new zone's travel payout is set by the
`gold_reward`/`xp_reward` bonus of its monster, same as hunts. If you add a zone, add its travel monster or
`/travel` answers "zona sin monstruos" (without charging the cooldown). **A boss pays more than a travel**: `RollBossReward` is the `/hunt` formula (plus the boss's own big gold/XP bonus,
`seed_zone_bosses.sql`) ×6 (`BossRewardMultiplier`), i.e. ~3.2× a travel of its zone in zones 2–5 (it was ×1 until v0.6.0 and paid HALF a travel — the 30-minute exam paid less than a
10-minute fight). That is about what its 30-minute cooldown is worth in travels (3) plus a premium for the risk of losing; every raid participant gets the full amount. It speeds up
leveling (a player cycling hunt + travel + boss on cooldown levels ~40% faster than with ×1), so if pacing needs to slow down, lower this one constant.

**Drops: every monster drops exactly ONE item, and the chances are deliberately low.** Per zone there are 4 drop
materials — one per `/hunt` monster ("a granel"), one from the travel monster ("escaso") and one from the boss
("raro") — and `Database/finalize_monster_roster.sql` is the single source of truth for the hunt + boss drops (and for
which Zone-1 hunt monsters exist; it runs after the seeds that create them, deletes the extras and raises if any
monster doesn't end with exactly one drop or a monster is missing from its roster list); `seed_travel_monsters.sql`
owns the travel drops. The chances live in `CombatRewardCalculator` and nowhere else: `HuntDropChancePercent` 6 (it was 10 until v0.6.0: lowered so that reaching a zone's level is not enough to breeze through it — you have to stay and farm the gear),
`TravelDropChancePercent` 20, `BossDropChancePercent` 15 (the boss — solo and raid — has its own constant on purpose:
it used to share the hunt formula, so lowering hunt would have silently changed it). `/drops` (`aa drops`,
`Modules/DropsModule.cs` + the pure `GameData/DropsCatalog.cs`) lists every zone's monsters and drops from those same
constants, so the list can't drift. These are **run-1 baseline values** — the reset unlocked after Zone 5 is meant to
raise drop % and material quantity. **Recipe quantities are calibrated against these chances**, so changing a chance,
a monster or a drop means re-running `Database/report_recipe_pacing.sql` (minutes of farming per recipe; pass the
chances as `-v ph= -v pt= -v pb=`) and retuning the seeds. Note the trap this avoids: with one item per monster each
specific item drops *more* often than with two, so lowering the percentages alone would have made progress faster,
not slower. Target: a zone's gear ≈ as long as leveling through that zone (~100 min of continuous play), the
boss-drop amulet ~200 min.

**Co-op zone bosses (`/raid`)**: `Modules/RaidModule.cs` + `Services/RaidSessionService.cs`. Same
in-memory philosophy as solo combat, but a *different concurrency model on purpose*: solo combat's
`ICombatSessionService.TryAdvance` is a compare-and-swap that **rejects** the loser of a race — right
for one player double-clicking, wrong for a raid where several different players clicking "Atacar" at
once is the normal case. So a `RaidSession` is mutable and every mutation happens under
`RaidSession.Lock` (plain `lock`, so **no `await` inside it** — do the I/O after releasing).
Rules that must keep holding: (1) the phase flips to `Resolved`/`Activating` *inside the same lock*
that decides the outcome — doing it afterwards let a concurrent click trigger a second victory and
double rewards; (2) HP is persisted exactly once per participant (on flee, or at raid resolution),
never incrementally, or the delta gets applied twice; (3) fleeing forfeits the reward, like solo;
(4) `IRaidSessionService`'s player→raid index and `RaidSession.Participants` must never disagree: the
starter is added to `Participants` in `BuildSessionAsync` (they used to be registered in the index but
not the roster, so they couldn't join, couldn't start, and stayed locked out of `/hunt` after the
lobby expired), `Remove` releases players *by the index* (not by iterating `Participants`), and
`TryActivateAsync` closes the raid if it throws midway. Known gap: an `Active` raid has no
inactivity timeout (see `MEJORAS.md`). Raid difficulty is not the stored boss stats: the raid boss is
`GameData/RaidDifficulty.cs` (HP +50% per extra player, plus per-zone multipliers) applied *at activation*
once the roster is final (`RaidSession.BossBaseHp` keeps the unscaled roll), so the lobby's provisional
`BossMaxHp` is meaningless until the phase flips to `Active`. The multipliers depend on the zone's **rank by
difficulty** (`RaidSession.ZoneRank`): zone 1 keeps ×2.2 HP / ×1.35 damage (calibrated on its softer boss), zone 2+
uses ×1.5 / ×1.1 because those bosses are already tuned to the zone ladder — stacking the zone-1 multipliers on a
ladder boss made the raid impossible (0-3% win). Whenever a zone boss is retuned, re-measure its raid.
Nobody can be in a solo fight and a raid at once (`AdventureCombatStarter` and `RaidModule` check
both `ICombatSessionService` and `IRaidSessionService`). Shared helpers extracted for reuse:
`GameData/ZoneRanking.cs` (zone order by `min_level`, never raw `zone_id`) and
`GameData/PlayerCombatProfileCalculator.cs` (level + gear + class passives → combat stats).

**Class abilities & the single turn resolver**: each class has one active ability with a button in
manual combat (`GameData/ClassAbilities.cs` — every balance number lives in `AbilityTuning`, 3-turn
cooldown for all four). The turn itself (player strike → Sifón de Almas → monster counter, with
ability effects applied) is resolved in exactly **one** place, the pure
`GameData/CombatTurnResolver.cs`, used by solo combat (`AdventureModule.ResolveTurnAsync`), raids
(`RaidModule.ResolveParticipantTurn`), `/use` mid-fight (`ResolveCounterTurn`) and `/autohunt`. Do
**not** re-inline that math in a module — it used to be copy-pasted in four places and adding
abilities to four copies was the reason it got extracted. Two deliberate rules: `/autohunt` never
uses abilities (design decision — it's for AFK farming of weak mobs; strong play means manual
combat), and callers must check `CheckAbility` before requesting `PlayerAction.Ability` (an invalid
request throws). Per-fight ability state (`AbilityState`) lives in `CombatState.Ability` /
`RaidParticipant.Ability`. In a raid the button is a generic "Habilidad" (the message is shared, so
it can't be labelled per player).

**Zone difficulty is a ladder, and it is calibrated, not guessed.** All 5 zones are loaded. Each zone needs a *jump*
in gear, ×1.6 the previous one (affinity weapon stat: Z2 +20, Z3 +32, Z4 +50, Z5 +80; generals and amulets follow the
same pace — the full table is in `MEJORAS.md` and each `seed_zoneN_gear_and_recipes.sql`), and its monsters are
tuned so that **entering** with the previous zone's gear costs (~5 turns, ~47% of HP in a common fight), the zone's
own gear leaves commons at 13-18% HP (comfortable, never a walkover), and the **boss** is the exam (67-87% defeat
with the previous zone's gear, ~16% with its own). Numbers come from a simulation that drives the real
`CombatTurnResolver`: fix a nominal, strictly increasing gear ladder, then bisect the monsters' HP and damage factors
until the entry fight hits those targets (the scratchpad harness is not in the repo — rebuild it; the sensitivity to
remember is that the "exit" target controls how fast the ladder inflates, because each zone is entered with the
previous zone's exit gear). Never retune a zone's monsters without its gear (recipes) shipping with it, or it becomes
a wall, and re-measure that zone's raid when its boss changes. The whole ladder is the **run-1 baseline**: a future
post-Zone-5 reset will raise drop % and material quantities, so don't lower recipes for run 1 because they're slow.
The per-zone recipe seeds (`seed_recipes.sql` = Zone 1, `seed_zone2..5_gear_and_recipes.sql`) own their recipes'
ingredients (they delete the old ones before loading) so a moved/changed recipe never keeps stale rows.

**Forge recipes: 7 per zone, and a player only sees their own zone's** (`recipes.zone_id` + `recipes.affinity`;
`add_recipe_zone_and_affinity.sql` for old DBs). The per-zone template is **4 affinity weapons (one per class — the
weapon family of that class) + 1 general weapon + 2 amulets (amulets are always general, never class-locked)**
(it used to be 2 generals; with one drop per monster the sources don't stretch that far, and the second general never
beat anyone's affinity weapon — `remove_extra_general_recipes.sql` migrates an old DB);
`affinity` is an explicit flag because a *general* weapon also has a family (Hoja de Acero Puro is Espadas), so it
can't be inferred from the item. A player sees 4: their class's affinity weapon + the general + the 2 amulets, for
their **current zone** (falling back to the nearest earlier zone that has recipes, with a note). That view lives in
one pure place, `GameData/RecipeCatalog.cs`, shared by `/forge recipes` and the `/forge make` autocomplete — they must
show the same thing. Each line shows what the result adds via `GameData/ItemStatLabel.cs` ("+15 ATQ" / "+20 DEF", plus
the class-synergy value for weapons). A Discord embed is capped at **6000 characters in total** (not just 1024 per
field) and `EmbedBuilder.Build()` *throws* past it — the template + single-zone view is what keeps it around 1000; any
embed built from a growing catalog needs a similar cap. Recipe seeds verify themselves (`seed_recipes.sql` raises if
an item/zone is missing — otherwise the row is silently dropped or created without that ingredient). Higher-zone
gear for zones 3-5 follows the same template (`seed_zone3..5_gear_and_recipes.sql`).
Deleting catalog items is dangerous: `inventory.item_id` is `ON DELETE CASCADE` (it silently wipes player inventories),
so any script that deletes items must first abort if anyone holds them (see `trim_recipes_to_zone_template.sql`).

**Name/ID slash parameters use Discord autocomplete** (`Modules/ItemAutocomplete.cs` for `/shop buy` and
`/equip`, `ZoneAutocomplete.cs` for `/zona`, `ForgeAutocomplete.cs` for `/forge make`, shared limits and
accent-insensitive filtering in `AutocompleteText.cs`): the parameter takes `[Autocomplete(typeof(...Handler))]`,
and the handlers are thin — the option-building logic is in pure static `*Choices` classes (no Discord,
testable). The option *value* is exactly what the command already accepted (item name / zone id), so
`Execute*Async` are unchanged, and typing a name by hand still works. The forge list puts what the player
can craft *now* first (✅) and says what's missing for the rest; the zone list must use
`ZoneRanking.PendingGatekeeperZone` (the one definition of the boss-gate rule, shared with `/zona`) or it
would promise zones the command then rejects. Handlers resolve repositories via the `IServiceProvider` argument (not constructor injection) and
must use `GetByDiscordIdAsync`, never `GetOrCreateUserAsync` — opening a list must not create accounts.
Every list derives from `Modules/SafeAutocompleteHandler.cs`, which logs (`BotLog.Error`) and returns an empty list if building the list
throws, instead of letting Discord show a silent "options failed to load"; lists exist for `/shop buy`, `/shop sell`, `/use`, `/equip`,
`/open`, `/forge make` and `/zona`. Item lookup by name ignores case, accents and surrounding whitespace (`ItemRepository.GetByNameAsync`).
Text commands ("aa ...") can't have autocomplete (a Discord limitation). Discord.Net throws if an option
value exceeds 100 chars, hence the `FitsAsValue` guard.

**Combat is stateful and in-memory, not per-command**: `/hunt` and `/travel` start a turn-based
fight tracked by `ICombatSessionService` (in-process, keyed by discord id — not persisted). The
database is only touched once, when the fight resolves (victory/defeat/flee/timeout), via a single
HP *delta* applied against whatever the player's real HP is at that moment (`IUserRepository.
ApplyCombatHpDeltaAsync`), specifically so that a `/heal` or `/use` fired mid-fight isn't silently
overwritten by a stale in-memory HP snapshot when the fight ends. `CombatState.PlayerStartingHp`
is the anchor for that delta calculation.

**Mid-fight healing is limited to ONE use per fight in `/travel` and `/boss`** (`GameData/CombatHeal.cs`,
`CombatState.HealUsed`): the combat message gets a select menu ("🍖 Curarte con comida") with the food in the player's
bag (re-read every turn, biggest heal first; `AdventureModule.HandleHealAsync`), and once used it stays on screen
disabled. The menu and a typed `/use` are the **same code path** (`UseModule.ExecuteUseAsync`) and share the limit —
otherwise the command would be a back door. `/hunt` has no menu and its `/use` is unchanged (unlimited); the raid has
neither. Healing still costs the turn (the monster counterattacks). The menu options deliberately carry no custom
emoji: if the bot can't access one, Discord rejects the WHOLE message, which would break starting every travel/boss.
Food prices (`rebalance_consumable_prices.sql`) rise faster than the heal (~0.5 gold/HP for the smallest, ~3 for the
biggest) because with one heal per fight the big one is worth much more.

**`/chop` and `/mine` give several units per action** (`GameData/GatheringYield.cs`): Común 1–5, Raro/Épico 1–3,
Legendario/Mítico 1, times `RunMultiplier` (1 in run 1; the post-Zone-5 reset is meant to raise it). The gathered
ingredients in the recipe seeds are ~3× (Común) / ~2× (Raro/Épico) what they used to be so the pace didn't change —
`Database/report_recipe_pacing.sql` measures it (it covers gathering too). Keep that in sync if the yields change.
The rarity odds of `/chop` and `/mine` (first the rarity is rolled, then a uniform item among those of that type+rarity) live in ONE
table, `RarityCatalog.GatheringWeights` (per mille): Común 68 / Raro 21 / Épico 7 / Legendario 3.5 / Mítico 0.5 (it was 60 / 25 / 10 /
4.5 / 0.5 until v0.6.0). `report_recipe_pacing.sql` takes `-v wc= wr= we= wl= wm=` to try a scenario before applying it. Known
bottleneck: Hierro (10.5% per `/mine`, cooldown 5 min — it shares the Raro pool with Carbón) makes several zone 2–4 weapons take
~190–290 min of mining, more than their drop farming.

**Readable embeds** (the owner's rule: spaced out, not crowded — use blank lines and columns, not walls of text): `/inventory` is two inline columns per row
(Recolección = Madera first, then Mineral, grouped by TYPE; next to it Drops de monstruo, i.e. `items.type = 'Material'`), then Armas | Amuletos, then
Comida | Cajas, with a blank spacer field between rows and no rarity text per line (the item emoji, or a coloured dot, carries it);
`/shop view` is two inline columns (Comida | Cajas), each item with ITS OWN emoji and a one-line detail, no rarity text and no decorative emojis; the recipes page (`/forge` zone picker, `aa forge recipes [zone]`) shows "have/need" per ingredient (✅/❌) when given the player's inventory. it shows ONE recipe per field (item + stat, gold, one ingredient per line) and
`/drops` sends one embed per zone (a field per fight type, two short lines per monster). Both were single walls of text
before. Every embed built from a growing catalog must stay under Discord's 6000-character total (all embeds of a message
count together).

**Errors are logged, never swallowed.** Every `catch (Exception ex)` in a module calls `BotLog.Error(ex)` (`Services/BotLog.cs`;
the file and method come from `[CallerFilePath]`/`[CallerMemberName]`, so it is one line per catch) *before* answering the
player's "¡Upa!" message — in production the console is the only log there is, and 52 silent catches used to make every
reported failure undiagnosable. Expected, harmless failures (a message that can no longer be edited) use `BotLog.Warn`.
`Program.cs` also logs unhandled and unobserved task exceptions, fails fast (exit code 1) if the database is unreachable or
empty (`EnsureDatabaseAsync`) or if Discord doesn't connect within 90 s (a bad token or a missing privileged intent otherwise
leaves a zombie process that looks alive), and shuts down cleanly on SIGTERM/Ctrl+C.

**Deployment is host-agnostic** (`DEPLOY.md`, `Dockerfile`, `docker-compose.yml`, `deploy/`): the bot is one process (no port)
plus Postgres, config only through environment variables, ONE instance running at a time (fights and raids live in memory, and
the same Discord token on two machines makes both answer every command — the owner uses one bot for dev and play, so a second
instance means a second bot). Branches: `main` is what runs/gets deployed, `develop` is where work happens; a release is a merge
of `develop` into `main` plus a tag. The Dockerfile
must keep using the standard Debian images — `string.Normalize(FormD)` in the autocompletes needs ICU, which the chiseled and
Alpine images don't ship. `<Version>` in the csproj is the bot version (`BotVersion.Current`, shown in `/info` and the startup
log); releases are git tags (`v0.5.0`).

**Game events & stats (`game_events`, `player_stats`)** — ONE pipeline for "something happened": `IGameEvents.RecordAsync(discordId,
kind, zoneId?, amount, detail?)` (`Services/GameEventService.cs`, kinds in `GameData/GameEventKinds.cs`, helpers in
`Services/GameEventExtensions.cs`) appends a row to `game_events` and bumps the per-player counter in `player_stats`, both in one
transaction (`Repositories/GameEventRepository.cs`). It **never throws** — a stats failure only logs a warning, it must not break
a purchase or a fight — so call it *after* the real transaction committed, never inside it. Messages to show a player later
(future mission/achievement unlocks) go into a notice queue (`TakeNotices`) that `Program.cs` delivers after every command
(`DeliverNoticesAsync`). Missions and achievements plug into this same service, not into the modules.
`Database/report_game_events.sql` summarizes the table. `/give` (`Modules/GiveModule.cs`, `ITransferRepository`) moves gold between
players atomically: `SELECT ... FOR UPDATE` on both rows **ordered by discord_id** (two opposite transfers can't deadlock), then a
guarded update each; no tax today — the events table is there to spot abuse, and `ExecuteGiveAsync` is the one place to add a cap.

**Boxes (`items.type = 'Caja'`, `boxes`, `box_loot`)** — five tiers, one per rarity; four are sold in `/shop` (150 / 700 / 1800 /
3500 gold) and the Mítica ("Arca del Soberano") is prize-only (`buy_price = 0`; `GameData/ShopCatalog.IsForSale` is the single
definition of "sold in the shop" — food and boxes alike). `/open` / `aa open <caja> [n]` (`Modules/BoxModule.cs`, 1–10 at a time)
consumes the box and pays the loot in ONE transaction (`Repositories/BoxRepository.OpenAsync`: guarded decrement, then gold + items),
the roll itself is the pure `GameData/BoxLootRoller.cs`. Loot lives in the DB (`Database/seed_boxes.sql` is the source of truth and
verifies itself): gold with a rare jackpot, gathering materials (Hierro from the cheapest box — it is the known bottleneck), "trophy"
materials no monster drops anymore, food and lower boxes. Two rules that must keep holding: (1) shop boxes **never** give the zone
drops used in recipes (Garra de Puma, Esencia Espectral...) — a 1000-gold box giving those would make buying ~5× faster than the
farming the pacing was calibrated on; (2) Corteza del Árbol de Vida / Fragmento de Meteorito (the very-long-term goals) come only from
the Mítica box, which can't be bought. Expected value of shop boxes is ~52–60% of price (a gold sink, not a business), and the best outcomes are deliberately rare (v0.6.0 tuning: the Cofre de Oro has a Legendary material in ~36% of openings, the Arcón de Hierro an Epic one in ~50%, and an opening that returns more than the box cost happens 6–9% of the time) —
`Database/report_box_economy.sql` computes it; re-run it if weights or prices change.

**Food: 6 items, and the two Mítica ones are "banquetes" with an attack buff** — the catalog was cut from 9 to 6 (Pan Casero, Choripán
and Vacío al Disco removed by `Database/rework_food_catalog.sql`, which refunds their gold value to anyone holding them *before* the
cascade delete). Banquetes (Asado Completo del Domingo en Familia 2100, Mate Dulce de la Abuela 2800 — ~3× the heal curve) also give
**+15% attack for 30 minutes**: data in `item_buffs` (tunable by SQL, no redeploy), the active one in `player_buffs` (one row per
player and buff key, **a new banquete REPLACES the old one, never stacks**; expiry is `now() + minutes` computed by the database and
`GetActiveAttackAsync` simply doesn't return an expired row — no cleanup job). The pure math is `GameData/AttackBuff.cs`:
`Apply` over the player's TOTAL attack (level + weapon with synergy, via `PlayerCombatProfileCalculator.Resolve(..., attackBuffPercent)`),
and `Rescale` for eating one mid-fight (`CombatState.AttackBuffPercent` remembers what's already applied so a second banquete doesn't
multiply twice). It is read at the start of a solo fight (`AdventureCombatStarter`) and when a raid participant is built (the attack is
fixed when joining), and shown in `/profile`, `/shop view`, the shop autocomplete and the heal dropdown. `/use` is the only way to eat
one — it works even at full HP because the buff is the point — and `/heal` (auto-pick) deliberately skips banquetes. In `/travel` and
`/boss` it uses the one-heal-per-fight slot like any food.

**Missions & achievements (`/missions`, `/achievements`)** — *progress is never stored*: a mission's progress is `SUM(game_events.amount)` of its
kind since the period started, and an achievement is "the `player_stats` counter reached N", so the event pipeline stays the single
source of truth and there is no assignment/progress row that can drift. The DB holds only what was **claimed** (`mission_claims`,
`achievement_claims`) plus `player_collection` (distinct trophies — materials no monster drops, i.e. the ones only boxes give; it
feeds the `trophy_found` counter of the Coleccionista achievement, and `AchievementCatalog.TrophyTotal` must match the real count).
Which missions apply is a pure function of the period start (`GameData/MissionCatalog.ForPeriod`, seeded SplitMix64 — identical for
everyone and across .NET versions; don't swap in `System.Random`, its seeded sequence is not guaranteed stable): 3 daily + 2 weekly,
never two of the same event kind, and every pool entry must be doable by *any* player (so no boss missions — level-gated — and no
fixed gold amounts — worth different per zone). Periods reset at **Uruguay midnight** (`GameData/UruguayCalendar.cs`; a fixed UTC−3
offset on purpose: Uruguay has no DST since 2015 and this avoids the OS time-zone database, whose ids differ between Windows and
Linux/Docker; weeks start Monday 00:00). Claiming (`MissionRepository`, `AchievementRepository`) is ONE transaction: lock the user row
`FOR UPDATE`, re-check the goal against the DB (a stale button can't claim an unfinished mission), `INSERT ... ON CONFLICT DO NOTHING
RETURNING` the claim mark (concurrent claims pay once — tested with 20 parallel), then `RewardPayer` pays gold + XP (with level-up) +
box in the same transaction; a reward that names a box that doesn't exist throws and rolls everything back instead of marking it
paid. Rewards are `RewardSpec` units that scale with the claimer's zone rank (`GameData/MissionRewards.cs`: gold = N × the gold of one
hunt in that zone — 14/58/118/215/375, the same unit the box prices use; XP = a % of the current level's requirement; boxes follow the
zone ladder). Boxes are bought **one per purchase and one purchase per hour** (`CooldownCatalog.BoxBuy` in `/cd`, `ShopCatalog.BoxesPerPurchase`;
`ShopRepository.BuyItemWithCooldownAsync` claims the cooldown, spends the gold and adds the box in ONE transaction, so a purchase that
fails for lack of gold never burns the cooldown); food has no limit. Cooldown times are shown with days/hours/minutes
(`GameData/TimeFormat.Remaining`, the one formatter for every cooldown message). The Mythic box (Arca del Soberano) is paid only by the tier-III achievements Matajefes and Coleccionista — it holds the
very-long-term goals and must stay unbuyable. Achievements count from the day event tracking went live (v0.6), not before. The
"¡Misión completada!" / "¡Logro desbloqueado!" notices come from `Services/ProgressNotifier.cs`, called by `GameEventService` right
after each saved event: it compares the counter before and after the event, so it fires exactly when a goal is crossed and stores
nothing. Two gotchas that bit this feature: Postgres `SUM(bigint)` is `numeric` and Dapper won't read it as `long` (cast `::bigint`),
and a reward's gold/XP/box are resolved from the claimer's zone and level *at claim time*, so unclaimed achievements pay more if you
wait for a later zone (accepted).

**Player trading (`/trade`, `aa trade @user "give" "get"`)** — a 1-for-1 swap of gathering materials between two players, and nothing else:
the rules are pure (`GameData/TradeRules.cs`): both items must be `Madera`/`Mineral` (what `/chop` and `/mine` drop), **the same
rarity**, and different items (so Roble↔Hierro↔Carbón, Pino↔Piedra, Nogal↔Oro Puro...). Do not loosen the rarity rule: a free converter
inside the Raro pool would double the Hierro rate and break the calibrated pacing. Flow: the proposer picks player + give + get (autocomplete
lists), `TradeModule.ExecuteOfferAsync` validates everything and posts a message with Accept/Decline buttons (only the target can accept;
either side can cancel); pending offers live in memory (`Services/TradeOfferService.cs`, 2 minutes, one open offer per proposer,
`TryTake` is atomic so concurrent clicks swap once). The swap itself is `ITransferRepository.SwapItemsAsync`: one transaction, four
operations always in (player, item) order so crossed swaps (A→B and B→A) can't deadlock, each decrement a guarded `UPDATE ... WHERE
quantity >= 1`. Commands are in English (`/open`, `/missions`, `/achievements`, `/trade`); the old Spanish names remain only as `aa` aliases.
Every text command must record its game event too: `aa daily` once forgot `daily_claim`, so the "claim your daily" mission never completed
for players using the text command.

**NPC dialogue and the blacksmith scene** — everything a character says lives in ONE pure place, `GameData/NpcDialogue.cs`: tables of lines per
situation for the blacksmith, the shopkeeper, the innkeeper (`/heal`) and the five zone bosses (intro / when it falls / when it beats you,
keyed by the boss name in the DB; a boss without its own lines gets generic ones, so adding a boss never leaves it mute). A line is picked
at random from its table (inject a `Random` to test), always formatted `emoji **Name:** «line»`. To add dialogue, add lines to a table;
a new character is an enum + a table. Every table must keep ≥2 distinct lines (a test checks it). `/forge` (`aa herrero`; the slash `/forge` has NO subcommands — `/forge make|recipes` were removed, `ForgeModule` is now only static logic, while `aa forge make|recipes` still exist for text users and parse unambiguously next to the `aa forge` alias),
`Modules/BlacksmithModule.cs`) is a scene: the blacksmith greets (with his picture if `Images__Blacksmith` is set in the `.env` to a public
image URL — the bot hosts no files — otherwise text only; see "NPC images ship with the bot" below) and a select menu lists the player's zone recipes (✅ craftable / ❌ what's
missing, the same `ForgeChoices` the `/forge make` autocomplete uses); picking one runs EXACTLY `ForgeModule.ExecuteMakeAsync`, so
validations, the atomic charge and the craft event are shared, and the menu is rebuilt after each order. the blacksmith's answer goes in a SEPARATE message right below (`BlacksmithModule.BuildAnswerEmbed`, same as the tavern's) so it doesn't get lost, and the scene is rebuilt after each order. A second select menu
("📜 Ver las recetas de una zona", `blacksmith_zone:{owner}`) switches the scene to that zone's recipes page (`ForgeModule.RenderRecipesEmbed`, the old `/forge recipes` view: have/need ✅/❌ per
ingredient, a 🔒 note for zones above the player's level, the picked zone stays marked); it is view-only — the forge menu still lists the player's own zone. `aa forge recipes [zone]` is the
text equivalent. Each menu's custom id carries the owner id so nobody else can use your conversation. Boss victory text depends on
whether it was the FIRST clear of that zone's boss (`highest_zone_cleared` is read *before* applying the victory): the first time it
announces the next zone opens; every later kill says "¡Volviste a ganarle!" and never claims an advance.

**"What should I farm?" advice (`/tips`, `aa tips` / `aa consejo`, `Modules/TipsModule.cs`; logic in `GameData/FarmAdvisor.cs`, `Services/FarmAdviceService.cs`)** — of the recipes the
player sees (same `RecipeCatalog.ViewFor` as the blacksmith) it picks the most advanced one (or says "you can forge X now" and points at `/forge`), lists at most 2 missing
ingredients and the command that yields each (`/chop`, `/mine`, or `/hunt`/`/travel`/`/boss` according to which monster of that zone drops it) and the gold shortfall. It used
to be a "💡 Para tu próxima forja" field glued to the end of `/chop`, `/mine` and every combat victory, which made messages that were already full even longer, so it is now its
**own command** (ephemeral in slash) and those embeds do not carry it any more — don't re-add the field (`GatheringModule.BuildResultEmbed` and `AdventureModule.BuildVictoryEmbed`
have no advice parameter). The choice is pure (`FarmAdvisor.Choose`); the service only gathers data and caches the near-static parts (recipes, zones, monsters, gatherable item
names) for 5 minutes. It NEVER throws (an advice is an extra: any failure returns null, and `/tips` then says there is nothing to advise).

**Level-up banner (`GameData/LevelUpCard.cs`, `Services/ProgressNotifier.cs`)** — leveling up used to be one small field ("Ahora sos nivel 7") inside victory/daily/receipt embeds that
already had gold, XP, HP, summary, drop... and got lost. Now it is its own **public gold embed** ("🎉 ¡SUBISTE DE NIVEL! 🎉": mention, `Nivel 6 ➜ Nivel 7`, max HP +15 per level, "curada al
máximo", a "zona nueva a tu alcance" line when the new level crosses a zone's `min_level`, and a random cheer in the footer). It comes out of the ONE place every XP source already
goes through: they all record a `level_up` event (`GameEventExtensions.RecordVictoryAsync`, the mission/achievement claims), `ProgressNotifier` turns it into a `GameNotice` carrying the
embed, and `Program.cs` delivers it right after the command (follow-up for slash and button interactions, channel message for text commands). So a new XP source gets the banner for free
as long as it records `level_up`; the in-embed field was removed everywhere (a raid keeps its short per-player line in the shared victory embed, because only the player who landed
the last blow is still "in" an interaction). `GameNotice.ExpiresUtc` (2 min for this one) drops notices that were never delivered, so another raid participant doesn't get a stale
banner hours later glued to an unrelated command.

**Mini-event (`Services/MiniEventService.cs`, `GameData/MiniEvents.cs`, `Modules/MiniEventModule.cs`)** — after a slash or text command in a guild channel, with
a small chance (default 3%, at most one per channel every 30 min, one open at a time) the bot posts "a miner dropped a bag of stones / a woodcutter's bundle
came loose / a traveller's pocket tore" with a "¡Juntar!" button; whoever clicks within ~15 s joins once, then every participant gets a SMALL reward that
**grows with the number of participants** (stones/wood: 2 units + 2 per extra person, cap 10 people; silver: N "hunts of gold" of the player's own zone).
Same concurrency idea as the raid: state mutated under a lock with no `await`, the window closes inside the lock (no late joins, no double pay), payment
(`IMiniEventRepository.PayAsync`, one transaction per participant) happens outside it; in-memory, a restart just makes the button say "ya terminó".
Tunable by env vars (`MiniEvent__ChancePercent`, `MiniEvent__ChannelCooldownMinutes`, `MiniEvent__JoinSeconds`); to test it set chance 100 and cooldown 0.

**The tavern (`/taberna`, `aa taberna`, `Modules/TabernaModule.cs`)** — the shopkeeper IS the innkeeper ("El Tabernero", one character; there is no "Tendero" any more): a scene with his photo, the
price list (two inline columns Comida | Cajas, `ShopModule.BuildViewEmbed`) and four select menus, one per row — eat something from your bag, buy food, buy a box, sell something.
Every pick is ONE unit and runs exactly the existing logic (`ShopModule.ExecuteBuyAsync/ExecuteSellAsync`, `UseModule.ExecuteUseAsync`: same validations, atomic charge, box cooldown and
game events), then the scene is rebuilt with your fresh gold and the innkeeper's answer goes in a SEPARATE message right below (`TabernaModule.BuildAnswerEmbed`: replacing the scene text made it get lost between the price list and the menus). Each menu's custom id carries the owner id. `/shop` (view/buy/sell/sellall) remains as the direct
shortcut for quantities and for text commands; `/shop view` carries no photo on purpose (an `attachment://` thumbnail without its file would make Discord reject the message).

**NPC images ship with the bot**: `Assets/npc/blacksmith.jpg` and `innkeeper.jpg` (256x256, ~25 KB; `Assets/**` is copied to the output and the publish)
are attached to the message as `attachment://file.jpg` (the embed's thumbnail), so nothing has to be hosted; an `Images__*` URL in the `.env` wins if set.

**Autoritative source for "how much SQL debt does this repo have right now"**: `MEJORAS.md` at the
repo root. Read it before assuming the schema in `Database/schema.sql` is what's actually running
on any given machine's Postgres instance — they drift (see "known recurring problem" above).
