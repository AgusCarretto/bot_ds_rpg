-- =========================================================
-- Migración para bases YA EXISTENTES (schema.sql ya la incluye para instalaciones nuevas): agrega a
-- las recetas del herrero dos datos que /forge recipes y /forge make necesitan para mostrar solo lo de
-- la zona del jugador y armar el molde "4 armas de afinidad + 2 generales + 2 amuletos por zona":
--
--   recipes.zone_id   la zona a la que pertenece la receta (NULL = sin zona, no se muestra).
--   recipes.affinity  true = es el ARMA DE AFINIDAD de una clase en esa zona (la de la familia de su
--                     clase: Espadas = Guerrero, Dagas = Ninja, Arcos = Arquero, Grimorios = Hechicero);
--                     solo la ve un jugador de esa clase. false = arma general o amuleto. Hace falta un
--                     dato explícito porque una arma GENERAL también tiene familia (la Hoja de Acero Puro
--                     es de Espadas) y no se puede deducir cuál es cuál solo mirando el ítem.
--
-- Re-ejecutable (IF NOT EXISTS). Ejecutar ANTES de seed_recipes.sql en una base que ya estaba corriendo.
-- =========================================================

ALTER TABLE recipes ADD COLUMN IF NOT EXISTS zone_id INTEGER REFERENCES zones (zone_id) ON DELETE SET NULL;
ALTER TABLE recipes ADD COLUMN IF NOT EXISTS affinity BOOLEAN NOT NULL DEFAULT false;
