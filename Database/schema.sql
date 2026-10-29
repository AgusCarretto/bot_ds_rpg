-- =========================================================
-- Asado y Acero RPG — Esquema inicial de base de datos
-- PostgreSQL
-- =========================================================
-- Orden de creación: items -> users -> inventory -> cooldowns
-- (users referencia items vía weapon_id/amulet_id, por eso items va primero)

BEGIN;

-- unaccent(): permite buscar ítems por nombre sin distinguir tildes (ej. "Jabali" encuentra
-- "Jabalí"), usado en ItemRepository.GetByNameAsync.
CREATE EXTENSION IF NOT EXISTS unaccent;

-- ---------------------------------------------------------
-- items: catálogo de armas, materiales y objetos del juego
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS items (
    item_id     INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    -- UNIQUE a propósito: ItemRepository.GetByNameAsync resuelve con LIMIT 1 sin ORDER BY, así que
    -- un nombre duplicado hace que /equip, /shop buy y /forge make resuelvan a cualquiera de las
    -- copias de forma inconsistente entre ejecuciones. También hace que el "ON CONFLICT DO NOTHING"
    -- que ya usan todos los seeds empiece a funcionar de verdad si se re-corren por error.
    name        TEXT NOT NULL UNIQUE,
    type        TEXT NOT NULL,                 -- ej: 'Espada', 'Daga', 'Arco', 'Grimorio', 'Madera', 'Mineral'
    rarity      TEXT NOT NULL DEFAULT 'Común'
                    CHECK (rarity IN ('Común', 'Raro', 'Épico', 'Legendario', 'Mítico')),
    stat_value  INTEGER NOT NULL DEFAULT 0 CHECK (stat_value >= 0),
    sell_price  INTEGER NOT NULL DEFAULT 0 CHECK (sell_price >= 0), -- lo que paga la tienda al vender
    buy_price   INTEGER NOT NULL DEFAULT 0 CHECK (buy_price >= sell_price), -- lo que cobra la tienda al comprar
    -- Familia del arma (solo aplica cuando type = 'Weapon'): define la sinergia de clase en combate,
    -- ver GameData/ClassCatalog.cs (WeaponType) y GameData/ClassWeaponSynergy.cs.
    weapon_family TEXT CHECK (weapon_family IN ('Espadas', 'Dagas', 'Arcos', 'Grimorios')),
    -- Clase exclusiva para equipar/forjar este ítem (ver Modules/EquipModule.cs y
    -- Modules/ForgeModule.cs). NULL = disponible para cualquier clase.
    class_requirement TEXT CHECK (class_requirement IN ('Guerrero', 'Ninja', 'Arquero', 'Hechicero')),
    -- Emoji personalizado de Discord ("<:nombre:id>"), NULL = todavía sin pixel art cargado
    -- (GameData/ItemDisplay.cs cae a mostrar solo el nombre en ese caso).
    emoji VARCHAR(100),
    -- Polvo que da UNA unidad al desmantelarla (/dismantle, GameData/Dismantling.cs). NULL = lo que diga su rareza (así se paga la madera y el mineral). Los drops de monstruo lo traen puesto
    -- (Database/rebalance_dust.sql): un drop de monstruo no tiene rareza, cae con la misma chance en la Zona 1 que en la 5, así que paga por su origen (cacería o viaje).
    dust_value INTEGER CHECK (dust_value IS NULL OR dust_value >= 0)
);

-- ---------------------------------------------------------
-- recipes / recipe_ingredients: recetas del herrero (ver Modules/ForgeModule.cs). Un ítem
-- resultado tiene como máximo una receta (UNIQUE); una receta puede tener varios ingredientes.
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS recipes (
    recipe_id       INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    result_item_id  INTEGER NOT NULL UNIQUE REFERENCES items (item_id) ON DELETE CASCADE,
    gold_cost       INTEGER NOT NULL DEFAULT 0 CHECK (gold_cost >= 0)
);

CREATE TABLE IF NOT EXISTS recipe_ingredients (
    recipe_id  INTEGER NOT NULL REFERENCES recipes (recipe_id) ON DELETE CASCADE,
    item_id    INTEGER NOT NULL REFERENCES items (item_id) ON DELETE CASCADE,
    quantity   INTEGER NOT NULL CHECK (quantity > 0),
    PRIMARY KEY (recipe_id, item_id)
);

CREATE INDEX IF NOT EXISTS idx_recipe_ingredients_recipe_id ON recipe_ingredients (recipe_id);

-- ---------------------------------------------------------
-- zones / monsters / monster_drops: mundo dividido en zonas de dificultad creciente (ver
-- Modules/ZoneModule.cs y Database/seed_zones_and_monsters.sql). /hunt solo caza monstruos de la
-- zona ACTUAL del jugador (users.current_zone_id), igual que /travel (su monstruo dedicado) y /boss.
-- Cada monstruo suelta UN solo ítem (ver Database/finalize_monster_roster.sql).
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS zones (
    zone_id     INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name        TEXT NOT NULL UNIQUE,
    description TEXT NOT NULL DEFAULT '',
    min_level   INTEGER NOT NULL DEFAULT 1 CHECK (min_level >= 1),
    emoji       TEXT,
    -- v0.11.0: 'normal' = una zona de la escalera; 'gate' = El Fogón Eterno (zone_id 0, Database/seed_fogon.sql), la puerta al Fuego Nuevo. IZoneRepository.GetAllAsync y
    -- IMonsterRepository.GetAllAsync solo devuelven las normales: la escalera, /zonas, las recetas por zona, los drops y las cajas no ven la puerta.
    kind        TEXT NOT NULL DEFAULT 'normal' CHECK (kind IN ('normal', 'gate'))
);

-- recipes.zone_id: a qué zona pertenece la receta (de dónde salen sus materiales y contra qué se
-- balancea); /forge recipes muestra solo las de la zona en la que está el jugador. NULL = sin zona
-- (no se muestra). Va acá y no dentro de CREATE TABLE recipes porque zones se crea DESPUÉS de recipes.
-- recipes.affinity: true = arma de afinidad de una clase (la de la familia de su clase; solo la ve un
-- jugador de esa clase); false = arma general o amuleto. Molde por zona: 4 de afinidad (una por
-- clase) + 2 armas generales + 2 amuletos (los amuletos siempre son generales).
ALTER TABLE recipes ADD COLUMN IF NOT EXISTS zone_id INTEGER REFERENCES zones (zone_id) ON DELETE SET NULL;
ALTER TABLE recipes ADD COLUMN IF NOT EXISTS affinity BOOLEAN NOT NULL DEFAULT false;

CREATE TABLE IF NOT EXISTS monsters (
    monster_id  INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    zone_id     INTEGER NOT NULL REFERENCES zones (zone_id) ON DELETE CASCADE,
    name        TEXT NOT NULL UNIQUE,
    emoji       TEXT,
    -- La cara del monstruo: un emoji de la aplicación ("<:nombre:id>") que sale como miniatura en los mensajes de combate
    -- (la carga Database/update_monster_portraits.sql). "emoji" de arriba es el unicode que va dentro del texto.
    portrait_emoji VARCHAR(100),
    min_hp      INTEGER NOT NULL CHECK (min_hp > 0),
    max_hp      INTEGER NOT NULL CHECK (max_hp >= min_hp),
    min_damage  INTEGER NOT NULL CHECK (min_damage >= 0),
    max_damage  INTEGER NOT NULL CHECK (max_damage >= min_damage),
    -- Bonus FIJO que este monstruo suma a la recompensa base de /hunt (ver
    -- GameData/CombatRewardCalculator.RollHuntReward) — no reemplaza la fórmula existente, la
    -- complementa, así que un monstruo de Zona 1 con 0/0 no cambia nada respecto a lo que ya había.
    gold_reward INTEGER NOT NULL DEFAULT 0 CHECK (gold_reward >= 0),
    xp_reward   INTEGER NOT NULL DEFAULT 0 CHECK (xp_reward >= 0),
    -- Jefe de zona (ver Modules/AdventureModule.cs, comando /boss): a lo sumo uno por zona,
    -- EXCLUIDO del pool aleatorio de /hunt (Repositories/MonsterRepository.GetMonstersByZoneAsync)
    -- — solo se enfrenta a propósito con /boss. Derrotarlo sube users.highest_zone_cleared.
    is_boss     BOOLEAN NOT NULL DEFAULT false,
    -- Monstruo DEDICADO de /travel (uno por zona, ver Database/seed_travel_monsters.sql): también
    -- EXCLUIDO del pool de /hunt, y nunca es a la vez el jefe de la zona.
    -- Repositories/MonsterRepository.GetTravelMonsterByZoneAsync.
    is_travel   BOOLEAN NOT NULL DEFAULT false,
    CONSTRAINT monsters_not_boss_and_travel CHECK (NOT (is_boss AND is_travel))
);

CREATE TABLE IF NOT EXISTS monster_drops (
    monster_id INTEGER NOT NULL REFERENCES monsters (monster_id) ON DELETE CASCADE,
    item_id    INTEGER NOT NULL REFERENCES items (item_id) ON DELETE CASCADE,
    PRIMARY KEY (monster_id, item_id)
);

CREATE INDEX IF NOT EXISTS idx_monsters_zone_id ON monsters (zone_id);
CREATE INDEX IF NOT EXISTS idx_monster_drops_monster_id ON monster_drops (monster_id);

-- ---------------------------------------------------------
-- users: perfil de cada jugador, una fila por discord_id
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS users (
    discord_id        BIGINT PRIMARY KEY,       -- snowflake ID de Discord
    class             TEXT NOT NULL DEFAULT 'Guerrero'
                        CHECK (class IN ('Guerrero', 'Ninja', 'Arquero', 'Hechicero')),
    level             INTEGER NOT NULL DEFAULT 1 CHECK (level >= 1),
    xp                INTEGER NOT NULL DEFAULT 0 CHECK (xp >= 0),
    gold              INTEGER NOT NULL DEFAULT 50 CHECK (gold >= 0),
    max_hp            INTEGER NOT NULL DEFAULT 100 CHECK (max_hp > 0),
    current_hp        INTEGER NOT NULL DEFAULT 100 CHECK (current_hp >= 0),
    weapon_id         INTEGER REFERENCES items (item_id) ON DELETE SET NULL,
    amulet_id         INTEGER REFERENCES items (item_id) ON DELETE SET NULL,
    daily_streak      INTEGER NOT NULL DEFAULT 0 CHECK (daily_streak >= 0),
    last_daily_claim  TIMESTAMPTZ, -- NULL = todavía no reclamó ningún /daily
    -- Zona donde caza /hunt (ver arriba). Default 1 = "Praderas del Mate": tiene que existir ANTES
    -- de que cualquier jugador corra /start (Database/seed_zones_and_monsters.sql debe correr
    -- antes de que haya jugadores nuevos, o el INSERT de un /start viola esta FK).
    current_zone_id   INTEGER NOT NULL DEFAULT 1 REFERENCES zones (zone_id),
    -- Rango (zone_id) de la zona MÁS DIFÍCIL cuyo jefe ya derrotó, 0 = ninguno todavía. Compara por
    -- posición en la lista de zonas ordenada por min_level, no por zone_id crudo (ver
    -- Modules/ZoneModule.ExecuteTravelAsync) — así no depende de que los zone_id sigan siendo
    -- consecutivos en orden de dificultad para siempre.
    highest_zone_cleared INTEGER NOT NULL DEFAULT 0 CHECK (highest_zone_cleared >= 0),
    -- El banco (v0.9.0, /bank): la cuenta se compra una vez (GameData/BankRules.cs) y el oro guardado ahí no lo toca la penalidad por muerte.
    has_bank          BOOLEAN NOT NULL DEFAULT false,
    bank_gold         INTEGER NOT NULL DEFAULT 0 CHECK (bank_gold >= 0),
    -- Polvo (v0.9.0): sale de desmantelar materiales (/dismantle) y se gasta en encantar (/enchant). No es un ítem: no se vende ni se regala.
    dust              INTEGER NOT NULL DEFAULT 0 CHECK (dust >= 0),
    -- Tier del encantamiento (0 = sin encantar, 1..5, ver GameData/Enchantments.cs) de la pieza que lleva puesta. Es de la PIEZA: al venderla vuelve a 0.
    weapon_enchant    INTEGER NOT NULL DEFAULT 0 CHECK (weapon_enchant BETWEEN 0 AND 5),
    amulet_enchant    INTEGER NOT NULL DEFAULT 0 CHECK (amulet_enchant BETWEEN 0 AND 5),
    -- v0.22.0: QUÉ efecto tiene ese encantamiento (GameData/Enchantments.cs). El arma: filo (+% de ATQ, el de siempre) | critico | sangre; el amuleto: guarda (+% de DEF, el de siempre) | vida | esquive.
    -- Los nuevos solo valen contra monstruos. Como el tier, es de la PIEZA: al venderla o con un Fuego Nuevo vuelve a filo / guarda.
    weapon_enchant_kind TEXT NOT NULL DEFAULT 'filo' CHECK (weapon_enchant_kind IN ('filo', 'critico', 'sangre')),
    amulet_enchant_kind TEXT NOT NULL DEFAULT 'guarda' CHECK (amulet_enchant_kind IN ('guarda', 'vida', 'esquive')),
    -- v0.11.0, El Fogón Eterno (zona 0): in_gate = está parado en la puerta (su current_zone_id sigue siendo la última zona normal: /hunt, /travel y /raid no andan y /boss pelea al Asador);
    -- gate_cleared = ya le ganó al Asador Eterno en esta vuelta (habilita el Fuego Nuevo).
    in_gate           BOOLEAN NOT NULL DEFAULT false,
    gate_cleared      BOOLEAN NOT NULL DEFAULT false,
    -- v0.12.0, Fuego Nuevo (el reinicio): cuántos hizo (0 = ninguno; "FN 3" = tres) y cuándo arrancó la vuelta actual (para el historial). Ver GameData/FuegoNuevoRules.cs.
    fuego_nuevo       INTEGER NOT NULL DEFAULT 0 CHECK (fuego_nuevo >= 0),
    run_started_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    equipped_title    TEXT,                                -- v0.23.0: la clave del título puesto (GameData/TitleCatalog.cs); NULL = ninguno. Database/add_titles.sql
    CONSTRAINT chk_current_hp_within_max CHECK (current_hp <= max_hp)
);

-- ---------------------------------------------------------
-- inventory: ítems que posee cada jugador (relación N a N entre users e items)
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS inventory (
    discord_id  BIGINT  NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    item_id     INTEGER NOT NULL REFERENCES items (item_id) ON DELETE CASCADE,
    quantity    INTEGER NOT NULL DEFAULT 1 CHECK (quantity >= 0),
    PRIMARY KEY (discord_id, item_id)
);

-- ---------------------------------------------------------
-- cooldowns: última ejecución de cada comando farmeable por jugador
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS cooldowns (
    discord_id       BIGINT NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    command_name     TEXT   NOT NULL,           -- ej: 'hunt', 'travel', 'chop', 'mine'
    last_executed_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (discord_id, command_name)
);

-- Índices para las consultas más frecuentes (lookup de inventario/cooldowns por jugador)
CREATE INDEX IF NOT EXISTS idx_inventory_discord_id ON inventory (discord_id);
CREATE INDEX IF NOT EXISTS idx_cooldowns_discord_id ON cooldowns (discord_id);

-- ---------------------------------------------------------
-- game_events / player_stats: registro de eventos de juego (qué se usa, cuánto se tarda en avanzar) y contadores por
-- jugador que leen las misiones y los logros. Ver Database/add_game_events_and_stats.sql (migración) y
-- Services/GameEventService.cs. game_events no tiene FK a users a propósito: sobrevive al borrado de cuentas de prueba.
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS game_events (
    event_id    BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    discord_id  BIGINT NOT NULL,
    kind        TEXT NOT NULL,                  -- ver GameData/GameEventKinds.cs
    zone_id     INTEGER,                        -- zona del jugador al momento (NULL si no aplica)
    amount      BIGINT NOT NULL DEFAULT 1,      -- cuánto: 1 para "pasó una vez", o el oro / las unidades involucradas
    detail      TEXT                            -- dato libre (nombre del ítem, tier de la caja, nivel nuevo...)
);

CREATE INDEX IF NOT EXISTS idx_game_events_kind_time ON game_events (kind, occurred_at);
CREATE INDEX IF NOT EXISTS idx_game_events_player_time ON game_events (discord_id, occurred_at);

CREATE TABLE IF NOT EXISTS player_stats (
    discord_id BIGINT NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    stat_key   TEXT   NOT NULL,                 -- mismo vocabulario que game_events.kind
    value      BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (discord_id, stat_key)
);

-- ---------------------------------------------------------
-- pet_species / player_pets: las mascotas (v0.10.0). El catálogo (huevos, comida y las 5 especies) lo carga Database/seed_pets.sql; las reglas (niveles, el
-- cooldown de una hora para alimentar, el bonus de cada nivel) viven en GameData/PetRules.cs. Una base vieja las crea con Database/add_pets.sql.
--   pet_species    una por zona (zone_id UNIQUE): qué bonus da (gold / xp / defense / drop), hasta cuánto (max_bonus_percent, al nivel 10) y su huevo (un ítem).
--   player_pets    las que tiene cada jugador. El NIVEL no se guarda: sale de feed_points (las comidas que se le dieron); last_fed_at manda el cooldown.
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS pet_species (
    species_id         INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    zone_id            INTEGER NOT NULL UNIQUE REFERENCES zones (zone_id) ON DELETE CASCADE,
    name               TEXT NOT NULL UNIQUE,
    emoji              TEXT,
    bonus_kind         TEXT NOT NULL CHECK (bonus_kind IN ('gold', 'xp', 'defense', 'drop', 'gather')),
    max_bonus_percent  DOUBLE PRECISION NOT NULL CHECK (max_bonus_percent > 0),
    egg_item_id        INTEGER NOT NULL UNIQUE REFERENCES items (item_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS player_pets (
    -- tier (v0.40.0): 1, 2 o 3 — el tope de nivel (10, 20, 30); expedition_hours: horas de expedición ya hechas por esta mascota (suman al recoger el premio). Database/add_pet_tiers.sql las agrega a una base viva.
    discord_id   BIGINT NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    species_id   INTEGER NOT NULL REFERENCES pet_species (species_id) ON DELETE CASCADE,
    feed_points  INTEGER NOT NULL DEFAULT 0 CHECK (feed_points >= 0),
    last_fed_at  TIMESTAMPTZ,
    hatched_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    tier             SMALLINT NOT NULL DEFAULT 1 CHECK (tier BETWEEN 1 AND 3),
    expedition_hours INTEGER  NOT NULL DEFAULT 0 CHECK (expedition_hours >= 0),
    PRIMARY KEY (discord_id, species_id)
);

-- ---------------------------------------------------------
-- fuego_nuevo_history / player_blessings / blessing_offers: Fuego Nuevo, el reinicio (v0.12.0, GameData/FuegoNuevoRules.cs y BlessingCatalog.cs). Una base vieja las crea con
-- Database/add_fuego_nuevo.sql. No hay datos de catálogo: las bendiciones viven en el código (BlessingCatalog), la base solo guarda lo que cada jugador tiene y le ofrecieron.
--   fuego_nuevo_history  una fila por Fuego Nuevo hecho (clase antes y después, nivel, cuánto tardó la vuelta)
--   player_blessings     las bendiciones del jugador con su nivel (1 a 5)
--   blessing_offers      las 3 bendiciones que se le ofrecieron en cada Fuego Nuevo (queda guardada hasta que elige; chosen_key NULL = pendiente)
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS fuego_nuevo_history (
    discord_id    BIGINT NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    number        INTEGER NOT NULL CHECK (number >= 1),
    class_before  TEXT NOT NULL,
    class_after   TEXT NOT NULL,
    level_before  INTEGER NOT NULL CHECK (level_before >= 1),
    started_at    TIMESTAMPTZ NOT NULL,
    finished_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (discord_id, number)
);

CREATE TABLE IF NOT EXISTS player_blessings (
    discord_id     BIGINT NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    blessing_key   TEXT NOT NULL,
    level          INTEGER NOT NULL CHECK (level BETWEEN 1 AND 5),
    PRIMARY KEY (discord_id, blessing_key)
);

CREATE TABLE IF NOT EXISTS blessing_offers (
    discord_id      BIGINT NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    fuego_nuevo_no  INTEGER NOT NULL CHECK (fuego_nuevo_no >= 1),
    offered_keys    TEXT NOT NULL,        -- las 3 claves separadas por coma ("manada,filo_antiguo,aprendiz")
    chosen_key      TEXT,                 -- NULL = todavía no eligió
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (discord_id, fuego_nuevo_no)
);

-- ---------------------------------------------------------
-- boxes / box_loot: cajas (items.type = 'Caja') y lo que pueden dar al abrirlas. Ver Database/add_boxes.sql (migración),
-- Database/seed_boxes.sql y Modules/BoxModule.cs.
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS boxes (
    box_item_id INTEGER PRIMARY KEY REFERENCES items (item_id) ON DELETE CASCADE,
    rolls       INTEGER NOT NULL CHECK (rolls BETWEEN 1 AND 10),   -- OBSOLETO desde la v0.8.0: la caja trae entre min_items y max_items (ver Database/rework_boxes.sql)
    min_items   INTEGER,
    max_items   INTEGER,
    CONSTRAINT boxes_items_range CHECK (min_items >= 1 AND max_items >= min_items)
);

CREATE TABLE IF NOT EXISTS box_loot (
    loot_id     INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    box_item_id INTEGER NOT NULL REFERENCES boxes (box_item_id) ON DELETE CASCADE,
    kind        TEXT NOT NULL CHECK (kind IN ('gold', 'item', 'gather', 'zone_drop')),   -- gather / zone_drop / gold: dinámicos, sin ítem fijo (ver rework_boxes.sql)
    item_id     INTEGER REFERENCES items (item_id) ON DELETE CASCADE,   -- NULL si es oro, recolección o drop de zona
    weight      INTEGER NOT NULL CHECK (weight > 0),
    min_qty     INTEGER NOT NULL CHECK (min_qty >= 1),
    max_qty     INTEGER NOT NULL CHECK (max_qty >= min_qty),
    CONSTRAINT box_loot_kind_item CHECK (
        (kind IN ('gold', 'gather', 'zone_drop') AND item_id IS NULL) OR (kind = 'item' AND item_id IS NOT NULL))
);

-- Una entrada por caja y por ítem (y una sola de oro por caja y rango no se repite: el rango distingue "común" de "jackpot").
CREATE UNIQUE INDEX IF NOT EXISTS ux_box_loot_item ON box_loot (box_item_id, item_id) WHERE kind = 'item';
CREATE UNIQUE INDEX IF NOT EXISTS ux_box_loot_gold ON box_loot (box_item_id, min_qty, max_qty) WHERE kind = 'gold';
CREATE INDEX IF NOT EXISTS idx_box_loot_box ON box_loot (box_item_id);

-- zone_boxes (v0.14.0): qué caja da cada zona en cada rol, y con qué chance. La fuente de verdad de las cajas GRATIS (el cofre de la primera vez por vuelta de cada jefe es su fila de monster_drops):
--   repeat     lo que da cada victoria repetida sobre el jefe    daily / weekly   el premio por completar todas las misiones del día / de la semana
--   prize      logros (tramo II) y campeón de la Arena           prize_top        logros (tramo III)
--   travel     la caja que PUEDE caer al ganar un /travel (v0.36.0: el viaje ya no suelta material), con su chance baja
-- Una zona nueva necesita sus filas (el arranque del bot avisa en el log si falta alguna). Las carga Database/seed_zone_boxes.sql.
CREATE TABLE IF NOT EXISTS zone_boxes (
    zone_id        INTEGER NOT NULL REFERENCES zones (zone_id) ON DELETE CASCADE,
    role           TEXT NOT NULL CHECK (role IN ('repeat', 'daily', 'weekly', 'prize', 'prize_top', 'travel')),
    box_item_id    INTEGER NOT NULL REFERENCES boxes (box_item_id) ON DELETE CASCADE,
    chance_percent INTEGER NOT NULL DEFAULT 100 CHECK (chance_percent BETWEEN 1 AND 100),
    PRIMARY KEY (zone_id, role)
);

-- ---------------------------------------------------------
-- item_buffs / player_buffs: buffs temporales (el +% de ataque de los banquetes). Ver Database/add_buffs.sql (migración) y
-- Database/rework_food_catalog.sql.
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS item_buffs (
    item_id        INTEGER PRIMARY KEY REFERENCES items (item_id) ON DELETE CASCADE,
    attack_percent INTEGER NOT NULL CHECK (attack_percent BETWEEN 1 AND 100),
    minutes        INTEGER NOT NULL CHECK (minutes BETWEEN 1 AND 1440)
);

CREATE TABLE IF NOT EXISTS player_buffs (
    discord_id BIGINT NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    buff_key   TEXT   NOT NULL,                  -- hoy solo 'attack'
    percent    INTEGER NOT NULL CHECK (percent BETWEEN 1 AND 100),
    expires_at TIMESTAMPTZ NOT NULL,
    source     TEXT,                             -- nombre del ítem que lo dio (para mostrarlo en /profile)
    PRIMARY KEY (discord_id, buff_key)
);

-- ---------------------------------------------------------
-- mission_claims / achievement_claims / player_collection: lo que cobró cada jugador de misiones y logros, y los trofeos distintos
-- que consiguió. El progreso NO se guarda acá: sale de game_events / player_stats. Ver Database/add_missions_and_achievements.sql
-- (migración, con la explicación completa), GameData/MissionCatalog.cs y GameData/AchievementCatalog.cs.
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS mission_claims (
    discord_id   BIGINT      NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    period       TEXT        NOT NULL CHECK (period IN ('daily', 'weekly')),
    period_start TIMESTAMPTZ NOT NULL,            -- el instante UTC en que empezó el día/semana de Uruguay
    mission_key  TEXT        NOT NULL,            -- la clave de la misión, o '_bonus' (el premio por completar todas las del período)
    claimed_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (discord_id, period, period_start, mission_key)
);

CREATE TABLE IF NOT EXISTS achievement_claims (
    discord_id      BIGINT      NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    achievement_key TEXT        NOT NULL,
    tier            INTEGER     NOT NULL CHECK (tier >= 1),
    claimed_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (discord_id, achievement_key, tier)
);

CREATE TABLE IF NOT EXISTS player_collection (
    discord_id        BIGINT      NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    item_id           INTEGER     NOT NULL REFERENCES items (item_id) ON DELETE CASCADE,
    first_obtained_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (discord_id, item_id)
);

COMMIT;

-- ---------------------------------------------------------
-- arena_days / arena_entries / arena_matches: la Arena, un torneo PvP por día (hora de Uruguay). Durante el día la gente se anota
-- (/arena join); a las 00:00 se arma la llave de eliminación directa, se pelea sola y se paga el premio al campeón (Services/ArenaService.cs).
-- Ver Database/add_arena.sql (migración) y GameData/ArenaRules.cs.
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS arena_days (
    day          DATE PRIMARY KEY,                    -- el día de Uruguay del torneo (se juega a la medianoche que lo cierra)
    status       TEXT NOT NULL DEFAULT 'open' CHECK (status IN ('open', 'resolved', 'cancelled')),
    channel_id   BIGINT,                              -- canal donde se anotó el primero: ahí se anuncia el resultado
    winner_id    BIGINT,
    winner_name  TEXT,
    participants INTEGER NOT NULL DEFAULT 0,
    rounds       INTEGER NOT NULL DEFAULT 0,
    reward_text  TEXT,                                -- lo que cobró el campeón, ya armado para mostrar
    resolved_at  TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS arena_entries (
    day          DATE        NOT NULL REFERENCES arena_days (day) ON DELETE CASCADE,
    discord_id   BIGINT      NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    display_name TEXT        NOT NULL,                -- el nombre al anotarse: la llave se muestra con él aunque la persona se vaya
    joined_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (day, discord_id)
);

-- Las peleas de la llave. Sin FK a users a propósito: es historia, sobrevive al borrado de una cuenta.
CREATE TABLE IF NOT EXISTS arena_matches (
    day       DATE    NOT NULL REFERENCES arena_days (day) ON DELETE CASCADE,
    round     INTEGER NOT NULL,                       -- 1 = primera ronda
    slot      INTEGER NOT NULL,                       -- posición dentro de la ronda
    p1_id     BIGINT  NOT NULL,
    p1_name   TEXT    NOT NULL,
    p2_id     BIGINT,                                 -- NULL = pasó directo (no tuvo rival)
    p2_name   TEXT,
    winner_id BIGINT  NOT NULL,
    actions   INTEGER NOT NULL DEFAULT 0,             -- cuántas acciones duró la pelea
    winner_hp_pct INTEGER NOT NULL DEFAULT 100,       -- % de vida con el que terminó el ganador (para narrar si fue paliza o por un pelo)
    PRIMARY KEY (day, round, slot)
);

CREATE INDEX IF NOT EXISTS idx_arena_days_status ON arena_days (status, day);

-- ---------------------------------------------------------
-- worldboss_days / worldboss_entries: el jefe mundial (v0.24.0, GameData/WorldBoss.cs). Un jefe por día (hora de Uruguay): la gente se anota con /miniboss (gasta el cooldown del jefe) y a las 00:00 lo pelean
-- entre todos; cada anotado cobra de 30 a 50 cacerías de oro de su zona, según la vida que le sacaron entre todos. Migración para una base que ya existe: Database/add_worldboss.sql.
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS worldboss_days (
    day              DATE PRIMARY KEY,                -- el día de Uruguay del jefe (se pelea a la medianoche que lo cierra)
    status           TEXT NOT NULL DEFAULT 'open' CHECK (status IN ('open', 'resolved', 'cancelled')),
    channel_id       BIGINT,                          -- canal donde se anotó el primero: ahí se anuncia el resultado
    boss_key         TEXT,                            -- cuál jefe fue (WorldBossRules.Bosses)
    participants     INTEGER NOT NULL DEFAULT 0,
    total_damage_pct NUMERIC(8, 2),                   -- cuánta vida le sacaron entre todos (100 = cayó)
    defeated         BOOLEAN,
    hunts            INTEGER,                         -- el premio de cada anotado, en cacerías de oro de su zona
    resolved_at      TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS worldboss_entries (
    day          DATE        NOT NULL REFERENCES worldboss_days (day) ON DELETE CASCADE,
    discord_id   BIGINT      NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    display_name TEXT        NOT NULL,                -- el nombre al anotarse: el anuncio lo muestra aunque la persona se vaya
    joined_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    damage_pct   NUMERIC(8, 2),                       -- lo que le sacó al jefe (se llena al pelear)
    reward_gold  INTEGER,                             -- el oro que cobró (se llena al pelear)
    PRIMARY KEY (day, discord_id)
);

CREATE INDEX IF NOT EXISTS idx_worldboss_days_status ON worldboss_days (status, day);

-- ---------------------------------------------------------
-- Recordatorios de cooldown (v0.16.0, GameData/Reminders.cs): cuando termina la espera de algo (cacería, viaje, talar, minar, jefe/raid, caja, mascotas, diario) el bot avisa en el canal donde
-- se usó. reminders = lo que está por avisar (UNA fila por jugador y tipo: un comando nuevo pisa el aviso anterior; el reloj la borra al avisarla). reminder_settings = los tipos que el jugador
-- APAGÓ (sin fila, o con la lista vacía, recibe todos; un tipo nuevo del futuro arranca prendido). Migración para una base que ya existe: Database/add_reminders.sql.
-- ---------------------------------------------------------
CREATE TABLE IF NOT EXISTS reminders (
    discord_id BIGINT      NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    kind       TEXT        NOT NULL,                 -- 'hunt', 'travel', 'chop', 'mine', 'boss', 'buybox', 'chop_adv', 'mine_adv', 'pet' o 'daily'
    channel_id BIGINT      NOT NULL,                 -- el canal donde se usó el comando que lo generó
    due_at     TIMESTAMPTZ NOT NULL,                 -- cuándo termina la espera
    PRIMARY KEY (discord_id, kind)
);

CREATE INDEX IF NOT EXISTS idx_reminders_due_at ON reminders (due_at);

CREATE TABLE IF NOT EXISTS reminder_settings (
    discord_id BIGINT PRIMARY KEY REFERENCES users (discord_id) ON DELETE CASCADE,
    off_kinds  TEXT[] NOT NULL DEFAULT '{}'
);

-- El idioma de cada jugador (v0.26.0, Database/add_language.sql): español o inglés. Sin clave foránea: se puede elegir idioma antes de /start.
CREATE TABLE IF NOT EXISTS player_language (
    discord_id BIGINT PRIMARY KEY,
    language   VARCHAR(5) NOT NULL CHECK (language IN ('es', 'en')),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- La configuración de cada jugador (v0.26.1, Database/add_player_settings.sql): mostrar u ocultar la tablita del resumen del combate y (v0.28.1, Database/add_recipe_book_setting.sql) qué recetario ve en la herrería
-- (false = el clásico, una zona por página; true = el completo). Sin clave foránea (como player_language).
CREATE TABLE IF NOT EXISTS player_settings (
    discord_id         BIGINT PRIMARY KEY,
    show_fight_summary BOOLEAN NOT NULL DEFAULT TRUE,
    full_recipe_book   BOOLEAN NOT NULL DEFAULT FALSE,
    updated_at         TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Las herramientas de cada jugador (v0.29.0, Database/add_tools.sql): una por tipo ('axe' = hacha para /chop, 'pickaxe' = pico para /mine); la nueva reemplaza a la vieja y el Fuego Nuevo las borra.
CREATE TABLE IF NOT EXISTS player_tools (
    discord_id BIGINT  NOT NULL REFERENCES users(discord_id) ON DELETE CASCADE,
    kind       TEXT    NOT NULL CHECK (kind IN ('axe', 'pickaxe')),
    item_id    INTEGER NOT NULL REFERENCES items(item_id) ON DELETE CASCADE,
    PRIMARY KEY (discord_id, kind)
);

-- El dragón de cada jugador (v0.30.0, Database/add_dragons.sql): uno por jugador, tipo, tier (1 a 10, sube criando) y nivel (0 hasta tier x 10, sube entrenando con oro). El Fuego Nuevo no lo toca.
CREATE TABLE IF NOT EXISTS player_dragons (
    discord_id   BIGINT PRIMARY KEY REFERENCES users(discord_id) ON DELETE CASCADE,
    kind         TEXT NOT NULL CHECK (kind IN ('fuerza', 'defensa', 'oro', 'especial')),
    tier         INTEGER NOT NULL DEFAULT 1 CHECK (tier BETWEEN 1 AND 10),
    level        INTEGER NOT NULL DEFAULT 0 CHECK (level >= 0),
    name         TEXT CHECK (name IS NULL OR char_length(name) BETWEEN 2 AND 24),
    fail_streak  INTEGER NOT NULL DEFAULT 0 CHECK (fail_streak >= 0),
    last_bred_at TIMESTAMPTZ,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Los códigos canjeables (v0.37.0, Database/add_redeem_codes.sql): el código con sus reglas, lo que da y quién ya lo canjeó (una fila por código y jugador = «uno por persona»).
CREATE TABLE IF NOT EXISTS redeem_codes (
    code_id     SERIAL      PRIMARY KEY,
    code        TEXT        NOT NULL UNIQUE,                                          -- normalizado: MAYÚSCULAS, sin acentos ni espacios (A-Z 0-9 _ -)
    max_uses    INTEGER     CHECK (max_uses IS NULL OR max_uses > 0),                 -- cuántas personas lo pueden canjear en total; NULL = sin tope
    uses        INTEGER     NOT NULL DEFAULT 0 CHECK (uses >= 0),
    expires_at  TIMESTAMPTZ,                                                          -- NULL = no vence
    active      BOOLEAN     NOT NULL DEFAULT TRUE,                                    -- apagarlo (aa admincode off) lo frena sin borrar nada
    note        TEXT,
    created_by  BIGINT      NOT NULL,                                                 -- el ID de Discord del administrador que lo creó
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS redeem_code_rewards (
    reward_id   SERIAL      PRIMARY KEY,
    code_id     INTEGER     NOT NULL REFERENCES redeem_codes (code_id) ON DELETE CASCADE,
    kind        TEXT        NOT NULL CHECK (kind IN ('gold', 'dust', 'xp', 'item')),
    item_id     INTEGER     REFERENCES items (item_id) ON DELETE CASCADE,            -- solo para kind = 'item' (si el ítem se borra del catálogo, el premio desaparece con él)
    amount      INTEGER     NOT NULL CHECK (amount > 0),
    CONSTRAINT redeem_reward_item_ck CHECK ((kind = 'item') = (item_id IS NOT NULL))
);

CREATE TABLE IF NOT EXISTS redeem_code_claims (
    code_id     INTEGER     NOT NULL REFERENCES redeem_codes (code_id) ON DELETE CASCADE,
    discord_id  BIGINT      NOT NULL REFERENCES users (discord_id) ON DELETE CASCADE,
    claimed_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (code_id, discord_id)
);

CREATE INDEX IF NOT EXISTS idx_redeem_code_rewards_code ON redeem_code_rewards (code_id);

-- Las expediciones de las mascotas (v0.39.0, Database/add_pet_expeditions.sql): una fila por jugador con la mascota que salió, por cuántas horas y cuándo vuelve (se borra al recoger el premio).
CREATE TABLE IF NOT EXISTS pet_expeditions (
    discord_id  BIGINT      PRIMARY KEY REFERENCES users (discord_id) ON DELETE CASCADE,
    species_id  INTEGER     NOT NULL REFERENCES pet_species (species_id) ON DELETE CASCADE,
    hours       INTEGER     NOT NULL CHECK (hours > 0),
    started_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    ends_at     TIMESTAMPTZ NOT NULL
);
