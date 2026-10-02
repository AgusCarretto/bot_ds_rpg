-- =========================================================
-- Armas y amuletos ya NO viven en el inventario: se forjan directo a equipamiento (users.weapon_id / users.amulet_id) y para cambiarlos hay
-- que vender el que llevás puesto. Esta migración ordena a los jugadores que ya existían, cuando equipar era un paso aparte y lo equipado
-- seguía contando como una copia en el inventario:
--
--   1. La copia del inventario de la pieza que llevás puesta ERA esa pieza: se borra sin pagar nada (pasa a ser solo "lo equipado").
--   2. Si no llevabas nada en un casillero y tenías piezas en el inventario, se equipa la mejor que tu clase puede usar (la que más
--      suma contando la sinergia de familia x1.5), y se descuenta esa copia.
--   3. Todo lo que quede de armas y amuletos en el inventario (piezas de más, o de menos nivel) se VENDE: se paga items.sell_price por cada
--      unidad y se borra del inventario. Es lo mismo que obtendría vendiéndolas en la tienda.
--
-- Re-ejecutable: después de correrla no queda ningún arma ni amuleto en inventory, así que una segunda pasada no encuentra nada.
-- Corré un pg_dump antes (toca oro e inventario de jugadores reales).
-- =========================================================

DO $$
DECLARE
    player      RECORD;
    slot        TEXT;
    equipped    INTEGER;
    best        INTEGER;
    refund      BIGINT;
    class_family TEXT;
    total_refund BIGINT := 0;
    total_equipped INTEGER := 0;
    total_removed INTEGER := 0;
BEGIN
    FOR player IN
        SELECT u.discord_id, u.class, u.weapon_id, u.amulet_id
        FROM users u
        WHERE EXISTS (
            SELECT 1 FROM inventory inv JOIN items i ON i.item_id = inv.item_id
            WHERE inv.discord_id = u.discord_id AND i.type IN ('Weapon', 'Amulet')
        )
    LOOP
        class_family := CASE player.class
            WHEN 'Guerrero' THEN 'Espadas'
            WHEN 'Ninja' THEN 'Dagas'
            WHEN 'Arquero' THEN 'Arcos'
            WHEN 'Hechicero' THEN 'Grimorios'
        END;

        FOREACH slot IN ARRAY ARRAY['Weapon', 'Amulet'] LOOP
            equipped := CASE slot WHEN 'Weapon' THEN player.weapon_id ELSE player.amulet_id END;

            IF equipped IS NOT NULL THEN
                -- 1. La copia del inventario era la pieza equipada: se descuenta sin pagar.
                UPDATE inventory SET quantity = quantity - 1
                WHERE discord_id = player.discord_id AND item_id = equipped AND quantity >= 1;
            ELSE
                -- 2. Nada puesto: se equipa la mejor del inventario que la clase pueda usar.
                SELECT i.item_id INTO best
                FROM inventory inv JOIN items i ON i.item_id = inv.item_id
                WHERE inv.discord_id = player.discord_id AND i.type = slot AND inv.quantity >= 1
                  AND (i.class_requirement IS NULL OR i.class_requirement = player.class)
                ORDER BY (i.stat_value * CASE WHEN slot = 'Weapon' AND i.weapon_family = class_family THEN 1.5 ELSE 1 END) DESC, i.item_id
                LIMIT 1;

                IF best IS NOT NULL THEN
                    IF slot = 'Weapon' THEN
                        UPDATE users SET weapon_id = best WHERE discord_id = player.discord_id;
                    ELSE
                        UPDATE users SET amulet_id = best WHERE discord_id = player.discord_id;
                    END IF;
                    UPDATE inventory SET quantity = quantity - 1 WHERE discord_id = player.discord_id AND item_id = best;
                    total_equipped := total_equipped + 1;
                END IF;
            END IF;

            -- 3. Lo que sobre de ese tipo se vende.
            SELECT COALESCE(SUM(i.sell_price::BIGINT * inv.quantity), 0), COALESCE(SUM(inv.quantity), 0)
            INTO refund, equipped
            FROM inventory inv JOIN items i ON i.item_id = inv.item_id
            WHERE inv.discord_id = player.discord_id AND i.type = slot AND inv.quantity > 0;

            IF refund > 0 OR equipped > 0 THEN
                UPDATE users SET gold = gold + refund WHERE discord_id = player.discord_id;
                total_refund := total_refund + refund;
                total_removed := total_removed + equipped;
            END IF;

            DELETE FROM inventory inv USING items i
            WHERE inv.item_id = i.item_id AND inv.discord_id = player.discord_id AND i.type = slot;
        END LOOP;
    END LOOP;

    RAISE NOTICE 'Armas/amuletos pasados a equipamiento: % equipados, % piezas sueltas vendidas por % de oro en total.', total_equipped, total_removed, total_refund;
END $$;

-- Chequeo: no debe quedar ningún arma ni amuleto en el inventario de nadie.
-- SELECT COUNT(*) FROM inventory inv JOIN items i USING (item_id) WHERE i.type IN ('Weapon', 'Amulet');
