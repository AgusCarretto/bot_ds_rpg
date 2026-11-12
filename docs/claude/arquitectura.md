# Arquitectura y reglas de código

> Texto movido literal desde el CLAUDE.md viejo el 2026-11-12 (en inglés, como estaba). El índice está en CLAUDE.md.

## Project

"Asado y Acero RPG" — a text-based RPG Discord bot (Spanish, Argentine-themed). C#/.NET 10,
Discord.Net (slash commands via `InteractionService` + legacy text commands via `CommandService`),
PostgreSQL via Dapper/Npgsql (no EF Core). Code comments and all user-facing text are in Spanish; since v0.26.0 the main screens also exist in English for players who choose it (`/language`, see "Language" at the end).

## Architecture

**Layering**: `Modules/` (Discord command handlers) → `Repositories/` (Dapper + SQL, one per
aggregate: `IUserRepository`, `IItemRepository`, `IInventoryRepository`, `IShopRepository`,
`ICraftingRepository`, `IRecipeRepository`, `ICasinoRepository`, `IAdventureRepository`,
`IGatheringRepository`, `ICooldownRepository`, `IProgressionRepository`, `IGameEventRepository`, `ITransferRepository`,
`IBoxRepository`, `IBuffRepository`, `IMissionRepository`, `IAchievementRepository`, `IBankRepository`, `IDustRepository`, `IPetRepository`, `IBlessingRepository`, `IFuegoNuevoRepository`, `IReminderRepository`, `IVoteRepository`) + `Services/` (stateful
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
Weapon/Amulet), `/shop` only lists/sells `type = 'Consumable'`, a class weapon's ATQ is a FIXED number (since v0.27.0 there is no class-synergy multiplier: `weapon_family` and `class_requirement` only decide which class forges and sees the recipe). Forge recipes also live in the
database (`recipes` + `recipe_ingredients`, referencing `items` by id), not in code — there used to
be a `GameData/CraftingCatalog.cs`, it was deleted in favor of `Repositories/RecipeRepository.cs`.

**Name/ID slash parameters use Discord autocomplete** (`Modules/ItemAutocomplete.cs` for `/shop buy` and
`/shop sell`, `ZoneAutocomplete.cs` for `/zona`, `ForgeAutocomplete.cs` for `/forge make`, shared limits and
accent-insensitive filtering in `AutocompleteText.cs`): the parameter takes `[Autocomplete(typeof(...Handler))]`,
and the handlers are thin — the option-building logic is in pure static `*Choices` classes (no Discord,
testable). The option *value* is exactly what the command already accepted (item name / zone id), so
`Execute*Async` are unchanged, and typing a name by hand still works. The forge list puts what the player
can craft *now* first (✅) and says what's missing for the rest; the zone list must use
`ZoneRanking.PendingGatekeeperZone` (the one definition of the boss-gate rule, shared with `/zona`) or it
would promise zones the command then rejects. Handlers resolve repositories via the `IServiceProvider` argument (not constructor injection) and
must use `GetByDiscordIdAsync`, never `GetOrCreateUserAsync` — opening a list must not create accounts.
Every list derives from `Modules/SafeAutocompleteHandler.cs`, which logs (`BotLog.Error`) and returns an empty list if building the list
throws, instead of letting Discord show a silent "options failed to load"; lists exist for `/shop buy`, `/shop sell`, `/use`,
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
log); releases are git tags (`v0.5.0`). **Railway is supported out of the box** (the owner chose it on 2026-10-05): `railway.toml` (Dockerfile worker, 1 replica, restart ALWAYS, overlap 0 / draining 20 s so
the old copy gets SIGTERM and time to leave Discord), the variables are pasted into the service's Raw Editor from `.env.railway` (gitignored through `.env.*`; the template without secrets is `deploy/railway.env.example`) and
`Postgres__ConnectionString` uses `${{Postgres.PGHOST}}`-style references, so the database service MUST be named `Postgres`; `deploy/migrate-to-railway.ps1` copies the live DB to any target URL (pg_dump → pg_restore, then compares
row counts; refuses a non-empty target without `-Replace` and refuses the same DB as the source). Railway tracks `main`: every push there redeploys and cuts fights, so work stays on `develop` and `main` only moves on releases.
Cost facts (docs, 2026-10-05): Hobby = US$5/month including US$5 of usage, overage is billed (not cut), the hard usage limit is optional and has a US$10 minimum; usage is per workspace. After any catalog change, the scratch
fresh-install-vs-live comparison (see "Known recurring problem") must come out identical — it did on 2026-10-05 after aligning `items.emoji` to VARCHAR(100) in `schema.sql`.

**Game events & stats (`game_events`, `player_stats`)** — ONE pipeline for "something happened": `IGameEvents.RecordAsync(discordId,
kind, zoneId?, amount, detail?)` (`Services/GameEventService.cs`, kinds in `GameData/GameEventKinds.cs`, helpers in
`Services/GameEventExtensions.cs`) appends a row to `game_events` and bumps the per-player counter in `player_stats`, both in one
transaction (`Repositories/GameEventRepository.cs`). It **never throws** — a stats failure only logs a warning, it must not break
a purchase or a fight — so call it *after* the real transaction committed, never inside it. Messages to show a player later
(future mission/achievement unlocks) go into a notice queue (`TakeNotices`) that `Services/NoticeDelivery.cs` delivers when a command ENDS: for slash commands and
buttons it hooks `InteractionService.InteractionExecuted` (`NoticeDelivery.Attach`, wired in `Program.cs`), for text commands `Program.cs` calls `NoticeDelivery.DeliverAsync` after `ExecuteAsync`.
**Do not deliver right after `_commands.ExecuteCommandAsync`**: Discord.Net's `InteractionService` runs commands in `RunMode.Async` by default, so that call returns success as soon as the command is
LAUNCHED, while it is still fighting/writing — the queue is empty at that moment and the "¡SUBISTE DE NIVEL!" banner used to wait for the player's NEXT command (fixed in v0.8.1; `noticetest` reproduces it with the real
`InteractionService`). Don't switch the service to `RunMode.Sync` to "fix" it: every command would block the gateway thread and all players would queue behind each other. Missions and achievements plug into this same service, not into the modules.
`Database/report_game_events.sql` summarizes the table. `/give` (`Modules/GiveModule.cs`, `ITransferRepository`) moves gold between
players atomically: `SELECT ... FOR UPDATE` on both rows **ordered by discord_id** (two opposite transfers can't deadlock), then a
guarded update each; no tax today — the events table is there to spot abuse, and `ExecuteGiveAsync` is the one place to add a cap.

**Autoritative source for "how much SQL debt does this repo have right now"**: `MEJORAS.md` at the
repo root. Read it before assuming the schema in `Database/schema.sql` is what's actually running
on any given machine's Postgres instance — they drift (see "known recurring problem" above).

