-- v0.20.0: la XP de los oficios de recolección depende de la RAREZA del material (Común 2, Raro 7, Épico 15, Legendario 30, Mítico 100 por tirada).
--
-- ANTES de v0.20.0 el nivel del Leñador y del Minero salía del contador de usos (player_stats 'chop' / 'mine') × 10 de XP, con la curva de XpUnit = 10. Ahora sale de contadores PROPIOS
-- ('chop_xp' / 'mine_xp') que suma cada tirada, y la curva bajó a XpUnit = 5 para que el ritmo promedio siga igual. Para que NADIE baje de nivel, este script siembra el contador nuevo con
-- la XP vieja en la escala nueva: usos × 5 (10 de XP con la unidad de 10 = 5 de XP con la unidad de 5: el mismo nivel). El Encantador no cambia de contador ('enchant'): pasó de 50 a 25 de XP
-- por intento sobre la misma escala nueva, o sea el mismo nivel, y no necesita siembra.
--
-- CORRER ANTES de arrancar el binario v0.20.0 (si el binario ya anduvo, quien talló o minó en el medio ya tiene su fila nueva y este script no la toca: ver ON CONFLICT DO NOTHING; en ese caso
-- se arregla a mano sumándole usos × 5 a esa fila). Re-ejecutable: una segunda corrida no cambia nada.

BEGIN;

INSERT INTO player_stats (discord_id, stat_key, value)
SELECT discord_id, 'chop_xp', value * 5
FROM player_stats
WHERE stat_key = 'chop' AND value > 0
ON CONFLICT (discord_id, stat_key) DO NOTHING;

INSERT INTO player_stats (discord_id, stat_key, value)
SELECT discord_id, 'mine_xp', value * 5
FROM player_stats
WHERE stat_key = 'mine' AND value > 0
ON CONFLICT (discord_id, stat_key) DO NOTHING;

-- Verificación: cada jugador con usos tiene su contador de XP.
DO $$
DECLARE
    missing INT;
BEGIN
    SELECT count(*) INTO missing
    FROM player_stats u
    LEFT JOIN player_stats x ON x.discord_id = u.discord_id AND x.stat_key = u.stat_key || '_xp'
    WHERE u.stat_key IN ('chop', 'mine') AND u.value > 0 AND x.value IS NULL;

    IF missing > 0 THEN
        RAISE EXCEPTION 'add_profession_xp: % jugadores con usos quedaron sin su contador de XP', missing;
    END IF;

    RAISE NOTICE 'add_profession_xp: ok. Leñador sembrados: %, Minero sembrados: %',
        (SELECT count(*) FROM player_stats WHERE stat_key = 'chop_xp'),
        (SELECT count(*) FROM player_stats WHERE stat_key = 'mine_xp');
END $$;

COMMIT;
