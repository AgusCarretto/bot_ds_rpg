-- v0.23.0: títulos (GameData/TitleCatalog.cs). Lo único que se guarda es CUÁL título tiene puesto cada jugador (users.equipped_title = la clave del título, NULL = ninguno y el perfil muestra «Recién Llegado»):
-- qué títulos tiene se calcula siempre de los contadores y eventos que el juego ya lleva, así que no hay otra tabla ni nada que sembrar. El Fuego Nuevo no toca la columna.
--
-- CORRER ANTES de arrancar el binario v0.23.0: toda consulta de jugador (Repositories/UserSql.cs) lee la columna nueva. Re-ejecutable.

BEGIN;

ALTER TABLE users ADD COLUMN IF NOT EXISTS equipped_title TEXT;

DO $$
BEGIN
    IF (SELECT count(*) FROM information_schema.columns WHERE table_name = 'users' AND column_name = 'equipped_title') <> 1 THEN
        RAISE EXCEPTION 'add_titles: falta la columna users.equipped_title';
    END IF;
END $$;

COMMIT;
