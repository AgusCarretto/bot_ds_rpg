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
  → seed_recipes.sql → seed_consumables_and_base_swords.sql
```

The other `Database/add_*.sql` / `cleanup_*.sql` / `fix_*.sql` files are historical incremental
migrations for databases that predate `schema.sql` catching up to include everything inline —
**do not run them against a fresh install**, `schema.sql` already has their end state. They matter
only if you're patching an old, already-running database that hasn't been recreated from scratch.

Only `seed_recipes.sql` and `seed_consumables_and_base_swords.sql` are safe to re-run (they upsert
via `ON CONFLICT`). The others insert unconditionally and will duplicate rows if run twice.

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

**Combat is stateful and in-memory, not per-command**: `/hunt` and `/travel` start a turn-based
fight tracked by `ICombatSessionService` (in-process, keyed by discord id — not persisted). The
database is only touched once, when the fight resolves (victory/defeat/flee/timeout), via a single
HP *delta* applied against whatever the player's real HP is at that moment (`IUserRepository.
ApplyCombatHpDeltaAsync`), specifically so that a `/heal` or `/use` fired mid-fight isn't silently
overwritten by a stale in-memory HP snapshot when the fight ends. `CombatState.PlayerStartingHp`
is the anchor for that delta calculation.

**Autoritative source for "how much SQL debt does this repo have right now"**: `MEJORAS.md` at the
repo root. Read it before assuming the schema in `Database/schema.sql` is what's actually running
on any given machine's Postgres instance — they drift (see "known recurring problem" above).
