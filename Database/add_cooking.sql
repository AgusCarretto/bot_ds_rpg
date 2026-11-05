-- La cocina (v0.49.0, /cook): lo que cada jugador cocinó de los platos PERMANENTES en esta vuelta. Un plato se cocina y se come en el momento, así que no hay ítem: solo cuántos lleva de cada uno, y
-- de eso salen sus bonus (+10 de vida máx por Surubí al Disco, +1 DEF por Dorado a la Parrilla, +2 ATQ por Mojarras en Escabeche, GameData/CookingCatalog.cs). El Fuego Nuevo borra las filas del jugador.
-- Correr en una base viva ANTES del binario v0.49.0: el Fuego Nuevo y cada pelea leen esta tabla. Re-ejecutable (schema.sql ya la trae para una base nueva).
CREATE TABLE IF NOT EXISTS player_cooked (
    discord_id BIGINT  NOT NULL REFERENCES users(discord_id) ON DELETE CASCADE,
    dish_key   TEXT    NOT NULL,
    count      INTEGER NOT NULL DEFAULT 0 CHECK (count >= 0),
    PRIMARY KEY (discord_id, dish_key)
);

-- Los platos de TIEMPO (v0.49.0, tanda 2): cuándo se le vence el efecto de cada uno. Comer el mismo otra vez SUMA 60 minutos a lo que le quede (GREATEST(vencimiento, ahora) + 60 min por plato, con el reloj de
-- la base); varios platos distintos corren a la vez. El Fuego Nuevo borra las filas del jugador.
CREATE TABLE IF NOT EXISTS player_cook_buffs (
    discord_id BIGINT      NOT NULL REFERENCES users(discord_id) ON DELETE CASCADE,
    dish_key   TEXT        NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (discord_id, dish_key)
);
