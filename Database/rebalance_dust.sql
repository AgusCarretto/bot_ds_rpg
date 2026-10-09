-- =========================================================
-- v0.15.1: el Polvo de los DROPS DE MONSTRUO se paga por su origen (cacería o viaje), no por una rareza que no tienen. Migración para la base viva Y último paso de run_fresh_install.sql.
-- Re-ejecutable (pone valores absolutos). No cambia el código del combate: alcanza con correrlo, aunque el bot esté andando (pero el binario v0.15.1 lee la columna nueva: correrlo ANTES de arrancarlo).
--
--   psql -d asado-y-acero -v ON_ERROR_STOP=1 -f rebalance_dust.sql
--
-- Por qué (el dueño, 2026-10-09: «el drop de monstruo NO tiene rareza; el % de caída es el mismo en la Zona 1 y en la 5»): /dismantle pagaba por la rareza del catálogo (Común 1, Raro 6, Épico 24,
-- Legendario 70, Mítico 500), que está calibrada para lo RECOLECTADO (la rareza de /chop y /mine es una chance de verdad: ~0,5 Polvo por minuto de farmeo en todas). Pero un drop de la Zona 5 figura
-- como Mítico y cae igual que uno de la Zona 1: desmantelar los drops de cacería de la Zona 5 daba 500 de Polvo c/u = 30 por cacería, 60 veces el ritmo calibrado, y encantar al máximo pasaba de
-- ~67 h de Polvo a casi una hora.
--
-- Ahora cada drop de monstruo trae su Polvo en items.dust_value: 17 los de CACERÍA (sale uno cada 2 / 6 % = 33,3 min → 0,5 Polvo/min) y 38 los de VIAJE (cada 30 min / 40 % = 75 min). Son los mismos
-- números que GameData/Dismantling.HuntDropDust y TravelDropDust (la prueba los compara). Si cambian las chances de drop (HuntDropChancePercent / TravelDropChancePercent) hay que volver a correr esto.
-- La madera y el mineral (dust_value NULL) siguen pagando por rareza. El jefe no suelta material (su "drop" es un cofre), así que no entra.
--
-- Hacé un backup antes (DEPLOY.md, sección 6).
-- =========================================================

-- La columna (en una base nueva ya la trae schema.sql y esto no hace nada).
ALTER TABLE items ADD COLUMN IF NOT EXISTS dust_value INTEGER CHECK (dust_value IS NULL OR dust_value >= 0);

-- Cada material que suelta un monstruo NO jefe de una zona normal: 38 si lo suelta el de viaje, 17 si lo sueltan los de cacería.
UPDATE items i
SET dust_value = CASE
        WHEN EXISTS (SELECT 1 FROM monster_drops d JOIN monsters m ON m.monster_id = d.monster_id WHERE d.item_id = i.item_id AND m.is_travel) THEN 38
        ELSE 17
    END
WHERE i.type = 'Material'
  AND EXISTS (
        SELECT 1 FROM monster_drops d
        JOIN monsters m ON m.monster_id = d.monster_id
        JOIN zones z ON z.zone_id = m.zone_id
        WHERE d.item_id = i.item_id AND NOT m.is_boss AND z.kind = 'normal');

-- Se verifica sola.
DO $$
DECLARE
    v_total INTEGER;
    v_hunt INTEGER;
    v_travel INTEGER;
    v_other INTEGER;
BEGIN
    SELECT count(*) INTO v_total FROM items WHERE dust_value IS NOT NULL;
    SELECT count(*) INTO v_hunt FROM items WHERE dust_value = 17 AND type = 'Material';
    SELECT count(*) INTO v_travel FROM items WHERE dust_value = 38 AND type = 'Material';
    SELECT count(*) INTO v_other FROM items WHERE dust_value IS NOT NULL AND (type <> 'Material' OR dust_value NOT IN (17, 38));

    -- 15 drops de monstruo: 2 de cacería + 1 de viaje por cada una de las 5 zonas.
    IF v_total <> 15 OR v_hunt <> 10 OR v_travel <> 5 OR v_other <> 0 THEN
        RAISE EXCEPTION 'rebalance_dust: se esperaban 10 drops de cacería (17) y 5 de viaje (38) y nada más; hay % con valor, % de cacería, % de viaje, % de otro tipo', v_total, v_hunt, v_travel, v_other;
    END IF;

    -- Todo drop de viaje cobra 38 y ninguno de cacería: cada drop pertenece a un solo origen.
    IF EXISTS (
        SELECT 1 FROM items i JOIN monster_drops d ON d.item_id = i.item_id JOIN monsters m ON m.monster_id = d.monster_id
        WHERE i.dust_value IS NOT NULL AND ((m.is_travel AND i.dust_value <> 38) OR (NOT m.is_travel AND NOT m.is_boss AND i.dust_value <> 17))) THEN
        RAISE EXCEPTION 'rebalance_dust: un drop de viaje no cobra 38 o uno de cacería no cobra 17.';
    END IF;
END $$;
