-- =========================================================
-- Habilita comparar texto sin distinguir tildes (ej. "Jabali" encuentra "Jabalí") para la
-- búsqueda de ítems por nombre en /equip, /shop buy, /shop sell y /forge make.
-- No renombra ningún dato existente, solo agrega la función unaccent() de Postgres.
-- Ejecutar UNA SOLA VEZ contra la base ya creada (requiere permisos de superusuario;
-- el usuario "postgres" ya los tiene).
-- =========================================================

CREATE EXTENSION IF NOT EXISTS unaccent;
