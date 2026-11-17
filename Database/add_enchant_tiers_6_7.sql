-- =========================================================
-- v0.58.0: los tiers VI (Infernal) y VII (Primordial) de los encantamientos (GameData/Enchantments.cs). El dueño (2026-11-16): «vamos a poder encantar a 35 % y 50 %
-- con menos probabilidad que el 26 %».
--
-- Correr en una base viva ANTES del binario v0.58.0: hasta ahora users.weapon_enchant y amulet_enchant solo aceptaban 0 a 5, y un encantamiento que saliera VI o VII
-- haría fallar la transacción. Solo cambia los CHECK (no toca ningún dato). Re-ejecutable.
-- =========================================================

ALTER TABLE users DROP CONSTRAINT IF EXISTS users_weapon_enchant_check;
ALTER TABLE users ADD CONSTRAINT users_weapon_enchant_check CHECK (weapon_enchant BETWEEN 0 AND 7);

ALTER TABLE users DROP CONSTRAINT IF EXISTS users_amulet_enchant_check;
ALTER TABLE users ADD CONSTRAINT users_amulet_enchant_check CHECK (amulet_enchant BETWEEN 0 AND 7);
