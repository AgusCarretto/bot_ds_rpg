-- =========================================================
-- Recorte de la comida y banquetes con buff.
--
-- 1) La comida pasa de 9 a 6: salen Pan Casero (20 HP), Choripán (50 HP) y Vacío al Disco (80 HP). Eran escalones casi
--    iguales a los vecinos (con una sola curación por pelea importa curar mucho o poco, no 5 grados intermedios) y ahora hay
--    cajas para gastar el oro. Quien las tenga en la mochila recibe su valor en oro (cantidad x lo que cuestan hoy en la
--    tienda) ANTES de que el ítem desaparezca: nadie pierde nada.
-- 2) Los dos BANQUETES Míticos (Asado Completo del Domingo en Familia y Mate Dulce de la Abuela) además de curar dan
--    +15% de ataque durante 30 minutos (item_buffs). El precio sube ~3 veces (rebalance_consumable_prices.sql). Elegir
--    un banquete es elegir poder: por eso valen mucho más que la comida que solo cura.
--
-- Re-ejecutable (si los 3 ya no están no hace nada) y se verifica solo. Ejecutar DESPUÉS de add_buffs.sql (o schema.sql) y de
-- rebalance_consumable_prices.sql (el reembolso usa los precios vigentes).
-- =========================================================

DO $$
DECLARE
    retired      TEXT[] := ARRAY['Pan Casero', 'Choripán', 'Vacío al Disco'];
    v_name       TEXT;
    v_refunded   INTEGER;
    v_total      INTEGER := 0;
    v_missing    TEXT;
BEGIN
    -- 1) Reembolso y baja. DELETE de items borra en cascada su inventario (ver CLAUDE.md): por eso el reembolso va primero y
    -- todo en esta misma transacción.
    FOREACH v_name IN ARRAY retired LOOP
        UPDATE users u
        SET gold = u.gold + sub.refund
        FROM (
            SELECT inv.discord_id, inv.quantity * i.buy_price AS refund
            FROM inventory inv JOIN items i ON i.item_id = inv.item_id
            WHERE i.name = v_name AND inv.quantity > 0
        ) sub
        WHERE u.discord_id = sub.discord_id;

        GET DIAGNOSTICS v_refunded = ROW_COUNT;
        v_total := v_total + v_refunded;

        DELETE FROM items WHERE name = v_name AND type = 'Consumable';
    END LOOP;

    IF v_total > 0 THEN
        RAISE NOTICE 'rework_food_catalog: se reembolsó en oro a % jugador(es) por la comida retirada', v_total;
    END IF;

    -- 2) Buffs de los banquetes (upsert: re-ejecutable).
    INSERT INTO item_buffs (item_id, attack_percent, minutes)
    SELECT i.item_id, 15, 30
    FROM items i
    WHERE i.name IN ('Asado Completo del Domingo en Familia', 'Mate Dulce de la Abuela')
    ON CONFLICT (item_id) DO UPDATE SET attack_percent = EXCLUDED.attack_percent, minutes = EXCLUDED.minutes;

    -- Verificación: los dos banquetes tienen su buff y la comida quedó en 6.
    SELECT string_agg(n, ', ') INTO v_missing
    FROM unnest(ARRAY['Asado Completo del Domingo en Familia', 'Mate Dulce de la Abuela']) AS n
    WHERE NOT EXISTS (SELECT 1 FROM item_buffs b JOIN items i ON i.item_id = b.item_id WHERE i.name = n);

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'rework_food_catalog: no se pudo cargar el buff de [%] (¿existen esos ítems?)', v_missing;
    END IF;

    IF (SELECT COUNT(*) FROM items WHERE type = 'Consumable') <> 6 THEN
        RAISE EXCEPTION 'rework_food_catalog: la comida tendría que quedar en 6 y hay %', (SELECT COUNT(*) FROM items WHERE type = 'Consumable');
    END IF;
END $$;

-- Chequeo rápido:
-- SELECT i.name, i.stat_value AS cura_hp, i.buy_price, b.attack_percent, b.minutes
-- FROM items i LEFT JOIN item_buffs b USING (item_id) WHERE i.type = 'Consumable' ORDER BY i.stat_value;
