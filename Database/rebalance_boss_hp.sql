-- =========================================================
-- v0.17.1: más VIDA a los seis jefes (los cinco de zona y El Asador Eterno), con el DAÑO igual. El dueño (a nivel 60): «aumentemos la dificultad de TODOS los jefes un poco, dale más vida a todos.
-- Daño dejalo igual». Medido con el combate real (4 clases, equipo propio, con la habilidad y una comida): ×1,25 deja el examen del jefe a nivel mínimo+3 en 22-31 % de derrotas en las zonas 2 a 4
-- (era 5-12 %), 55 % en la zona 5 (era 24 %) y 35 % en el Fogón (era 11 %); a nivel mínimo+8, que es donde suele estar la gente cuando tiene el equipo, 0-5 % en las zonas 2 a 4, 21 % en la 5 y 9 % en el
-- Fogón. Con el daño igual un jugador MUY por encima del nivel de la zona no corre riesgo igual (a nivel 60 el jefe de la zona 5 cuesta 15 % de la vida en vez de 11 %): la vida solo alarga la pelea.
--
-- Valores ABSOLUTOS a partir de la vida de la v0.17.0 (la columna base de abajo), así que correrlo dos veces deja lo mismo. Para otro factor: cambiar factor y volver a correrlo (y poner los números nuevos
-- en seed_zone_bosses.sql y seed_fogon.sql, que son lo que usa una instalación nueva). Una instalación NUEVA no necesita este script: las semillas ya traen el estado final.
-- Se verifica solo: si algún jefe falta o no queda como debe, frena con un error.
-- =========================================================
WITH base (name, base_min_hp, base_max_hp) AS (
    VALUES
        ('Rey Jabalí',             140,  196),
        ('Lobisón Alfa',           499,  635),
        ('Capataz de Hierro',      743,  929),
        ('Señor del Volcán',       1072, 1310),
        ('Soberano de la Escoria', 1540, 1847),
        ('El Asador Eterno',       2002, 2401)
), tuning AS (
    SELECT 1.25::numeric AS factor
)
UPDATE monsters m
SET min_hp = ROUND(b.base_min_hp * t.factor)::int,
    max_hp = ROUND(b.base_max_hp * t.factor)::int
FROM base b, tuning t
WHERE m.name = b.name AND m.is_boss;

DO $$
DECLARE
    v_bad TEXT;
BEGIN
    SELECT string_agg(x.name, ', ') INTO v_bad
    FROM (VALUES
            ('Rey Jabalí', 175, 245), ('Lobisón Alfa', 624, 794), ('Capataz de Hierro', 929, 1161),
            ('Señor del Volcán', 1340, 1638), ('Soberano de la Escoria', 1925, 2309), ('El Asador Eterno', 2503, 3001)
         ) AS x(name, min_hp, max_hp)
    WHERE NOT EXISTS (SELECT 1 FROM monsters m WHERE m.name = x.name AND m.is_boss AND m.min_hp = x.min_hp AND m.max_hp = x.max_hp);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'rebalance_boss_hp: estos jefes no quedaron con la vida esperada (¿existen?): [%]. Si cambiaste el factor, actualizá también esta lista.', v_bad;
    END IF;
END $$;

-- Chequeo rápido: la vida y el daño de cada jefe (el daño no cambia).
-- SELECT name, min_hp, max_hp, min_damage, max_damage FROM monsters WHERE is_boss ORDER BY min_hp;
