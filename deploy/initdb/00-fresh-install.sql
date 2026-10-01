-- Carga inicial de la base de Postgres del docker-compose.yml. El entrypoint de la imagen de Postgres corre este archivo
-- UNA sola vez: la primera vez que arranca con el volumen vacío (si la base ya existe, no lo vuelve a correr).
--
-- /seed es la carpeta Database/ del repo montada en solo lectura. run_fresh_install.sql usa \ir (incluir "relativo al
-- archivo"), así que alcanza con incluirlo con su ruta completa y el resto lo encuentra solo. Si algún script falla,
-- run_fresh_install.sql corta con ON_ERROR_STOP y la base queda a medias: en ese caso se borra el volumen
-- (docker compose down -v) y se vuelve a levantar, ver DEPLOY.md.
\i /seed/run_fresh_install.sql
