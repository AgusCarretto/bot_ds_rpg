-- =========================================================
-- La Comida de Dragón (v0.32.0)
--
-- En cada cría (cada intento de criar, salga bien o mal) el dragón cambia de tipo al azar (GameData/DragonRules.cs). Quien tenga esta comida en la mochila conserva el tipo: se gasta UNA, sola, en cada cría. El precio (5.000) lo fijó el dueño: «el que quiera mantener su tipo que pague».
-- Se compra en la taberna / la tienda (items.type = 'DragonFood', ver GameData/ShopCatalog.cs), no cura (no se come) y sobrevive al Fuego Nuevo igual que el dragón y la comida de las mascotas.
--
-- Se corre contra una base ya andando ANTES o DESPUÉS del binario v0.32.0: sin este ítem el dragón cría igual (cambia el tipo siempre) y la tienda simplemente no la ofrece.
-- Re-ejecutable: si el ítem ya existe, solo le repone tipo, rareza y precios (el emoji lo pone update_item_emojis.sql cuando haya arte; hasta entonces el 🍖 de acá).
-- =========================================================

INSERT INTO items (name, type, rarity, stat_value, sell_price, buy_price, emoji) VALUES
    ('Comida de Dragón', 'DragonFood', 'Común', 0, 1250, 5000, '🍖')
ON CONFLICT (name) DO UPDATE SET
    type = EXCLUDED.type, rarity = EXCLUDED.rarity, stat_value = EXCLUDED.stat_value, sell_price = EXCLUDED.sell_price, buy_price = EXCLUDED.buy_price;

DO $$
DECLARE
    v_food int;
BEGIN
    SELECT count(*) INTO v_food FROM items WHERE type = 'DragonFood' AND name = 'Comida de Dragón' AND buy_price > 0;
    IF v_food <> 1 THEN
        RAISE EXCEPTION 'add_dragon_food: falta la Comida de Dragón (%)', v_food;
    END IF;
END $$;
