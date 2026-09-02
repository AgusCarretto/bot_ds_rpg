-- =========================================================
-- Migración: limpieza de columnas de equipo en users.
-- Ejecutar UNA SOLA VEZ contra la base ya creada.
-- =========================================================

-- current_weapon_id quedó reemplazada por weapon_id desde que armamos /equip;
-- ningún código la lee ni la escribe más.
ALTER TABLE users DROP COLUMN IF EXISTS current_weapon_id;

-- weapon_id/amulet_id se agregaron a mano sin Foreign Key (ver MEJORAS.md); las agregamos ahora.
ALTER TABLE users ADD CONSTRAINT users_weapon_id_fkey FOREIGN KEY (weapon_id) REFERENCES items (item_id) ON DELETE SET NULL;
ALTER TABLE users ADD CONSTRAINT users_amulet_id_fkey FOREIGN KEY (amulet_id) REFERENCES items (item_id) ON DELETE SET NULL;
