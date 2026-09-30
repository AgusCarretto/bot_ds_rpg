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
  → update_item_emojis.sql
```

`seed_recipes.sql` must come after every seed that creates items it uses: a recipe whose result or ingredient
item doesn't exist yet would be silently skipped (or created *without* that ingredient), so the seed verifies
itself and raises instead. (`seed_zone_bosses.sql` used to be missing from this list, so a fresh install had no
zone bosses at all.)

`Database/run_fresh_install.sql` runs all eighteen in this exact order in one shot via `psql` (or
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
`seed_travel_monsters.sql`, `finalize_monster_roster.sql`, `seed_recipes.sql`, `seed_zoneN_gear_and_recipes.sql` (N = 2..5), `finalize_consumable_catalog.sql`, `remove_legacy_consumables.sql`, `rebalance_consumable_prices.sql`, `update_item_emojis.sql`) is
safe to re-run.

**Known recurring problem**: catalog items have repeatedly been added by hand directly to a
Postgres instance on one machine and never captured in a script, so a fresh install on another
machine silently ends up missing items (this happened at least twice — weapons and the entire
Consumable catalog). If you add/edit `items` rows by hand, write the INSERT into a new
`Database/*.sql` file in the same session, or the next fresh install will be short again.

## Architecture

**Layering**: `Modules/` (Discord command handlers) → `Repositories/` (Dapper + SQL, one per
aggregate: `IUserRepository`, `IItemRepository`, `IInventoryRepository`, `IShopRepository`,
`ICraftingRepository`, `IRecipeRepository`, `ICasinoRepository`, `IAdventureRepository`,
`IGatheringRepository`, `ICooldownRepository`, `IProgressionRepository`) + `Services/` (stateful
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
`/travel` answers "zona sin monstruos" (without charging the cooldown).

**Drops: every monster drops exactly ONE item, and the chances are deliberately low.** Per zone there are 4 drop
materials — one per `/hunt` monster ("a granel"), one from the travel monster ("escaso") and one from the boss
("raro") — and `Database/finalize_monster_roster.sql` is the single source of truth for the hunt + boss drops (and for
which Zone-1 hunt monsters exist; it runs after the seeds that create them, deletes the extras and raises if any
monster doesn't end with exactly one drop or a monster is missing from its roster list); `seed_travel_monsters.sql`
owns the travel drops. The chances live in `CombatRewardCalculator` and nowhere else: `HuntDropChancePercent` 10,
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
Known bottleneck, unchanged: Hierro (12.5% per `/mine`, cooldown 5 min) makes several zone 2–4 weapons take ~150–200 min
of mining, more than their drop farming.

**Readable embeds**: `/forge recipes` shows ONE recipe per field (item + stat, gold, one ingredient per line) and
`/drops` sends one embed per zone (a field per fight type, two short lines per monster). Both were single walls of text
before. Every embed built from a growing catalog must stay under Discord's 6000-character total (all embeds of a message
count together).

**Autoritative source for "how much SQL debt does this repo have right now"**: `MEJORAS.md` at the
repo root. Read it before assuming the schema in `Database/schema.sql` is what's actually running
on any given machine's Postgres instance — they drift (see "known recurring problem" above).
