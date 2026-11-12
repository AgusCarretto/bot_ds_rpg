-- =========================================================
-- v0.53.0: el jefe de la Zona 5 (Soberano de la Escoria) sube un poco: +10 % de VIDA y +5 % de DAÑO. El dueño (2026-11-04 y otra vez 2026-11-12): «el de la zona 5 no tuve que pestañear con arma de zona 4… sumale
-- unos puntos de vida y daño». Medido con el combate real (tools/balance2 -- J 4 …, 4 clases, con la habilidad y una comida, SIN mascotas, dragón, bendiciones, encantamientos ni platos): con el equipo de la zona
-- ANTERIOR a nivel 45 pasa de perder 5 % a ~25 % de las veces (y le cuesta ~75 % de la vida en vez de 66 %), a nivel 35 de 64 % a ~85 %; con el equipo PROPIO a nivel 28 (mínimo + 8) de 19 % a ~46 %, a nivel 35 de 1 % a
-- ~8 %. A nivel 55-60 sigue sin arriesgar nada (con el equipo anterior cuesta ~54 % de la vida): ahí lo único que cambia algo es que el jefe escale con el nivel del jugador (sin construir).
--
-- Valores ABSOLUTOS a partir de los de la v0.17.1 (la columna base de abajo), así que correrlo dos veces deja lo mismo. Para otro factor: cambiar los dos números de «tuning», actualizar la lista de verificación de
-- abajo y poner los mismos números en seed_zone_bosses.sql (lo que usa una instalación NUEVA, que no necesita este script). El RAID de la Zona 5 queda IGUAL de difícil que antes: GameData/RaidDifficulty.cs divide
-- sus multiplicadores por estos mismos factores (ZoneFiveBossHpBoost / ZoneFiveBossDamageBoost). Se puede correr antes o después del binario v0.53.0.
-- Se verifica solo: si el jefe no queda como debe, frena con un error.
-- =========================================================
WITH base (name, base_min_hp, base_max_hp, base_min_dmg, base_max_dmg) AS (
    VALUES ('Soberano de la Escoria', 1925, 2309, 202, 248)
), tuning AS (
    SELECT 1.10::numeric AS hp_factor, 1.05::numeric AS dmg_factor
)
UPDATE monsters m
SET min_hp = ROUND(b.base_min_hp * t.hp_factor)::int,
    max_hp = ROUND(b.base_max_hp * t.hp_factor)::int,
    min_damage = ROUND(b.base_min_dmg * t.dmg_factor)::int,
    max_damage = ROUND(b.base_max_dmg * t.dmg_factor)::int
FROM base b, tuning t
WHERE m.name = b.name AND m.is_boss;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM monsters m
        WHERE m.name = 'Soberano de la Escoria' AND m.is_boss
          AND m.min_hp = 2118 AND m.max_hp = 2540 AND m.min_damage = 212 AND m.max_damage = 260
    ) THEN
        RAISE EXCEPTION 'rebalance_zone5_boss: el Soberano de la Escoria no quedó con 2118-2540 de vida y 212-260 de daño (¿existe?). Si cambiaste los factores, actualizá también esta verificación.';
    END IF;
END $$;

-- Chequeo rápido: SELECT name, min_hp, max_hp, min_damage, max_damage FROM monsters WHERE is_boss ORDER BY min_hp;
