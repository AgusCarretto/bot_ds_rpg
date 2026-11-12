# Setup, base de datos y tests

> Texto movido literal desde el CLAUDE.md viejo el 2026-11-12 (en inglés, como estaba). El índice está en CLAUDE.md.

## Commands

```
dotnet build              # compile — do this after any change
dotnet run                # start the bot (needs .env or User Secrets configured, see below)
.\tests\run-all.ps1       # the 76 verification harnesses against a LOCAL database (or tests/run-all.sh); -Only name1,name2 for some
```

**Tests (since v0.18.3; `tests/`, `tools/`, design in `tests/README.md`)** — until v0.18.2 the ~40 harnesses mentioned all over this file ("Harness: `dailytest`", "`typedtest`"...) lived outside the repo, in a temporary folder of whoever was
working, and `MEJORAS.md` counted "zero tests" as a debt. Now each is a console project `tests/<name>/<name>.csproj` + `Program.cs` that references the real bot project (no Discord) and prints `[OK]`/`[FALLA]` per check, ending with `TODO OK`
(exit 0) or `N FALLAS` (exit 1); they are deliberately plain scripts, not a test framework. `tests/Directory.Build.props` holds everything the csprojs share, `tests/Shared/TestEnv.cs` the connection string (the `Postgres__ConnectionString`
variable, or the `.env` of the repo root, or — in a git worktree — the main folder's) and `RepoRoot`; **never write an absolute path in a harness**. **They write and delete data** (disposable users `999000000000xxxxx`, and a few touch the
catalog or `cooldowns` for a moment), so `TestEnv` **refuses a non-local database** unless `HARNESS_ALLOW_REMOTE=1`: never point them at production. The main csproj excludes `tests/**` and `tools/**` from its own compilation (`Compile
Remove`) and `.dockerignore` keeps them out of the image. **`dotnet build` plus `.\tests\run-all.ps1` is the mechanical check now**; when a change breaks a harness, first decide whether the harness encoded the OLD behaviour on purpose (update it, like the
XP ranges in `stage11test`) or the code is wrong. `tools/balance2` is the calibration simulator (the real `CombatTurnResolver` and the local monsters): parts A–J, e.g. `dotnet run --project tools/balance2 -- J` measures every boss against
the previous zone's gear.

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
  → rework_food_catalog.sql → seed_boxes.sql → rework_drops_and_recipes.sql → rework_boxes.sql
  → update_item_emojis.sql → update_monster_portraits.sql → seed_pets.sql → seed_fogon.sql
  → seed_zone_boxes.sql → retire_box_trophies.sql → rebalance_dust.sql → update_item_emojis.sql (again)
```

**The emoji script runs TWICE on purpose**: `update_item_emojis.sql` sits at position 21, but `seed_pets.sql`, `seed_fogon.sql` and `seed_zone_boxes.sql` create items AFTER it, so its UPDATEs-by-name found nothing for the Trinche, the Brasa, the eggs and the two new boxes and a fresh install ended with them empty (found on 2026-10-09 when the Fogón pair got its art). The last line of `run_fresh_install.sql` runs it again, and the scratch-vs-live comparison of `name=emoji` for every item came out identical. Any item created late in the order gets its emoji in `update_item_emojis.sql` and nothing else.

`seed_recipes.sql` must come after every seed that creates items it uses: a recipe whose result or ingredient
item doesn't exist yet would be silently skipped (or created *without* that ingredient), so the seed verifies
itself and raises instead. (`seed_zone_bosses.sql` used to be missing from this list, so a fresh install had no
zone bosses at all.)

`Database/run_fresh_install.sql` runs all twenty-eight in this exact order (plus the emoji script once more at the end) in one shot via `psql` (or
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
`seed_travel_monsters.sql`, `finalize_monster_roster.sql`, `seed_recipes.sql`, `seed_zoneN_gear_and_recipes.sql` (N = 2..5), `finalize_consumable_catalog.sql`, `remove_legacy_consumables.sql`, `rebalance_consumable_prices.sql`, `rework_food_catalog.sql`, `seed_boxes.sql`, `rework_drops_and_recipes.sql`, `rework_boxes.sql`, `update_item_emojis.sql`, `update_monster_portraits.sql`, `seed_pets.sql`, `seed_fogon.sql`, `seed_zone_boxes.sql` — **the last one must be applied through `add_zone_boxes.sql` to a live database BEFORE the v0.14.0 binary**: the code reads the `zone_boxes` table in every mission/achievement payout and boss repeat) is
safe to re-run (**`rework_boxes.sql` must be applied to a live database BEFORE starting the v0.8.0 binary**: the code reads the `boxes.min_items/max_items` columns it adds; the same goes for **`update_monster_portraits.sql` before the v0.8.2 binary**, which reads `monsters.portrait_emoji`, and for **`add_pets.sql` before the v0.10.0 binary** — it creates the `pet_species`/`player_pets` tables, runs `seed_pets.sql` and hands an egg to every player who had already beaten a zone boss; re-runnable, and the code reads those tables on every fight start), **with one exception since v0.7.0**: `rework_drops_and_recipes.sql` supersedes the drops, the boss drops, the Zone-1 roster and every recipe of the seeds before it
(`finalize_monster_roster.sql`, `seed_zone_bosses.sql`, `seed_travel_monsters.sql`'s boss/roster parts, `seed_recipes.sql`, `seed_zoneN_gear_and_recipes.sql`). Never re-run those alone on a live database: they would bring the old state back (or fail on
items that no longer exist). The rework script always goes after them and is itself re-runnable — but it deletes on purpose (retired items vanish from inventories with no refund; it prints a NOTICE with what was lost), so take a `pg_dump` first.

**Known recurring problem**: catalog items have repeatedly been added by hand directly to a
Postgres instance on one machine and never captured in a script, so a fresh install on another
machine silently ends up missing items (this happened at least twice — weapons and the entire
Consumable catalog; and a third time with Carbón, hand-edited from Común to Raro on the live DB while the seed kept
creating it Común, which on a fresh install would have doubled Hierro's `/mine` rate and unbalanced the recipes). If you
add/edit `items` rows by hand, write the INSERT into a new `Database/*.sql` file in the same session, or the next fresh
install will be short again. **Way to check, and worth doing after any catalog change**: create a scratch database, run
`run_fresh_install.sql` against it, and diff a few `concat_ws(...)` summary queries (items, recipes, monsters, boxes, buffs)
between it and the live one — they must be identical (that comparison is how the Carbón drift was found).
