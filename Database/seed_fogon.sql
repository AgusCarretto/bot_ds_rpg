-- =========================================================
-- v0.11.0: El Fogón Eterno (la "Zona 0"), la puerta al Fuego Nuevo. Re-ejecutable. Va DESPUÉS de todo lo demás (usa los drops y los materiales de las 5 zonas) y la incluyen
-- run_fresh_install.sql (instalación nueva) y add_fogon.sql (base que ya existe). Las COLUMNAS (zones.kind, users.in_gate, users.gate_cleared) están en schema.sql / add_fogon.sql.
--
--   · La zona: una fila de "zones" con zone_id = 0 y kind = 'gate'. IZoneRepository.GetAllAsync y IMonsterRepository.GetAllAsync NO la devuelven, así que la escalera, /zonas,
--     las recetas por zona, los drops, las cajas y las mascotas no la ven (solo /zona 0 y /boss, GameData/FogonRules.cs).
--   · El equipo para entrar (PUESTO, uno solo de cada uno, igual para todas las clases, sin sinergia): Trinche del Asador Eterno (+128 ATQ) y Brasa del Fogón Eterno (+165 DEF; era +120 hasta la v0.15.1, cuando todos los amuletos subieron y el Asador pegó +45),
--     el salto x1,6 de la escalera sobre la Zona 5 (80 / 75). Rareza Mítico.
--   · Sus recetas (zona 0, no son de afinidad): CUESTAN MUCHÍSIMO a propósito, con los drops de CAZA de las 5 zonas (x5 cada uno) más madera/mineral Legendarios y Raros y 25.000 de
--     oro. Desde la v0.15.1 (el dueño: «la corteza y el meteorito tienen que usarse para algo») cada pieza pide UN material Mítico de recolección: la Corteza del Árbol de Vida en el
--     Trinche y el Fragmento de Meteorito en la Brasa (0,5 % por acción, un solo ítem Mítico por tipo: ~1000 min cada uno, en paralelo porque /chop y /mine tienen cooldown aparte; se
--     juntan mientras se hace el resto, por eso conviene no desmantelarlos). Antes de eso el camino medía ~14 h de juego perfecto (report_recipe_pacing.sql -v gate=1).
--   · El jefe: El Asador Eterno, calibrado con el CombatTurnResolver real a nivel 28 (el examen de cada jefe de la escalera): con el equipo del Fogón pierde ~16 % y con el de
--     Zona 5 (arma de clase + amuleto) ~75 %. HP y daño = los del jefe de Zona 5 x1,3 y x1,2. No suelta nada (monster_drops vacío): ganarle abre el Fuego Nuevo.
-- =========================================================

-- La zona (con su id fijo 0: la identidad es GENERATED ALWAYS, de ahí OVERRIDING SYSTEM VALUE).
INSERT INTO zones (zone_id, name, description, min_level, emoji, kind)
OVERRIDING SYSTEM VALUE
VALUES (0, 'El Fogón Eterno', 'Una brasa que nunca se apaga, al final del mundo. Solo entra quien lleva el equipo del Fogón, y solo hay un enemigo: el Asador Eterno.', 25, '🔥', 'gate')
ON CONFLICT (zone_id) DO UPDATE
    SET name = EXCLUDED.name, description = EXCLUDED.description, min_level = EXCLUDED.min_level, emoji = EXCLUDED.emoji, kind = EXCLUDED.kind;

-- El equipo (no toca el emoji si ya se lo cargaron).
INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price, weapon_family, class_requirement)
VALUES
    ('Trinche del Asador Eterno', 'Weapon', 'Mítico', 128, 1000, 1300, NULL, NULL),
    ('Brasa del Fogón Eterno',    'Amulet', 'Mítico', 165, 1000, 1300, NULL, NULL)
ON CONFLICT (name) DO UPDATE
    SET type = EXCLUDED.type, rarity = EXCLUDED.rarity, stat_value = EXCLUDED.stat_value, sell_price = EXCLUDED.sell_price,
        buy_price = EXCLUDED.buy_price, weapon_family = EXCLUDED.weapon_family, class_requirement = EXCLUDED.class_requirement;

-- El jefe.
INSERT INTO monsters (zone_id, name, emoji, min_hp, max_hp, min_damage, max_damage, gold_reward, xp_reward, is_boss, is_travel)
VALUES (0, 'El Asador Eterno', '🔥', 2002, 2401, 233, 289, 3000, 300, true, false)
ON CONFLICT (name) DO UPDATE
    SET zone_id = EXCLUDED.zone_id, emoji = EXCLUDED.emoji, min_hp = EXCLUDED.min_hp, max_hp = EXCLUDED.max_hp, min_damage = EXCLUDED.min_damage,
        max_damage = EXCLUDED.max_damage, gold_reward = EXCLUDED.gold_reward, xp_reward = EXCLUDED.xp_reward, is_boss = true, is_travel = false;
DELETE FROM monster_drops WHERE monster_id = (SELECT monster_id FROM monsters WHERE name = 'El Asador Eterno');

-- Las recetas.
INSERT INTO recipes (result_item_id, gold_cost, zone_id, affinity)
SELECT i.item_id, 25000, 0, false FROM items i WHERE i.name IN ('Trinche del Asador Eterno', 'Brasa del Fogón Eterno')
ON CONFLICT (result_item_id) DO UPDATE SET gold_cost = EXCLUDED.gold_cost, zone_id = EXCLUDED.zone_id, affinity = EXCLUDED.affinity;

-- Los ingredientes se reescriben enteros (una receta cambiada nunca conserva filas viejas).
DELETE FROM recipe_ingredients WHERE recipe_id IN (SELECT r.recipe_id FROM recipes r JOIN items i ON i.item_id = r.result_item_id WHERE i.name IN ('Trinche del Asador Eterno', 'Brasa del Fogón Eterno'));

DROP TABLE IF EXISTS fogon_ingredients;
CREATE TEMP TABLE fogon_ingredients (result_name TEXT, item_name TEXT, quantity INTEGER);
INSERT INTO fogon_ingredients VALUES
    ('Trinche del Asador Eterno', 'Colmillo de Jabalí', 5),
    ('Trinche del Asador Eterno', 'Garra de Puma Cenizo', 5),
    ('Trinche del Asador Eterno', 'Yunque Fragmentado', 5),
    ('Trinche del Asador Eterno', 'Núcleo de Magma', 5),
    ('Trinche del Asador Eterno', 'Corazón de Titán', 5),
    ('Trinche del Asador Eterno', 'Madera de Ébano', 4),
    ('Trinche del Asador Eterno', 'Hierro', 18),
    ('Trinche del Asador Eterno', 'Corteza del Árbol de Vida', 1),
    ('Brasa del Fogón Eterno', 'Pluma de Ñandú', 5),
    ('Brasa del Fogón Eterno', 'Esencia Espectral', 5),
    ('Brasa del Fogón Eterno', 'Gema en Bruto', 5),
    ('Brasa del Fogón Eterno', 'Escama Ígnea', 5),
    ('Brasa del Fogón Eterno', 'Fragmento de Alma', 5),
    ('Brasa del Fogón Eterno', 'Gema de Zafiro', 4),
    ('Brasa del Fogón Eterno', 'Carbón', 18),
    ('Brasa del Fogón Eterno', 'Fragmento de Meteorito', 1);

DO $$
DECLARE
    v_missing TEXT;
BEGIN
    -- Un ingrediente que no existe se omitiría en silencio (la receta nacería sin él): se avisa en voz alta.
    SELECT string_agg(f.item_name, ', ') INTO v_missing FROM fogon_ingredients f WHERE NOT EXISTS (SELECT 1 FROM items i WHERE i.name = f.item_name);
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'seed_fogon: faltan ítems para las recetas del Fogón: %', v_missing;
    END IF;
END $$;

INSERT INTO recipe_ingredients (recipe_id, item_id, quantity)
SELECT r.recipe_id, ing.item_id, f.quantity
FROM fogon_ingredients f
JOIN items res ON res.name = f.result_name
JOIN recipes r ON r.result_item_id = res.item_id
JOIN items ing ON ing.name = f.item_name;

-- Verificación del estado final.
DO $$
DECLARE
    v_zone INTEGER;
    v_boss INTEGER;
    v_drops INTEGER;
    v_recipes INTEGER;
    v_bad INTEGER;
BEGIN
    SELECT count(*) INTO v_zone FROM zones WHERE zone_id = 0 AND kind = 'gate';
    SELECT count(*) INTO v_boss FROM monsters WHERE zone_id = 0 AND is_boss AND NOT is_travel;
    SELECT count(*) INTO v_drops FROM monster_drops md JOIN monsters m USING (monster_id) WHERE m.zone_id = 0;
    SELECT count(*) INTO v_recipes FROM recipes WHERE zone_id = 0 AND NOT affinity;
    SELECT count(*) INTO v_bad FROM recipes r WHERE r.zone_id = 0 AND (SELECT count(*) FROM recipe_ingredients ri WHERE ri.recipe_id = r.recipe_id) <> 8;

    IF v_zone <> 1 OR v_boss <> 1 OR v_drops <> 0 OR v_recipes <> 2 OR v_bad <> 0 THEN
        RAISE EXCEPTION 'seed_fogon: estado final inesperado (zona 0 = %, jefes = %, drops del jefe = %, recetas = %, recetas sin 8 ingredientes = %)', v_zone, v_boss, v_drops, v_recipes, v_bad;
    END IF;
END $$;

DROP TABLE IF EXISTS fogon_ingredients;
