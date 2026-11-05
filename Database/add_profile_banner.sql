-- =========================================================
-- v0.50.0: el banner del perfil que cada jugador eligió (/config, «aa banner»). Guarda la CLAVE de la bendición cuyo banner usa (GameData/ProfileBanners.cs); NULL = sin banner (lo de siempre).
-- Para una base que ya estaba andando: agrega la columna a player_settings. schema.sql ya la trae, así que una instalación nueva NO necesita correr esto. Re-ejecutable (IF NOT EXISTS) y sin tocar las filas
-- que ya había: todas quedan sin banner.
--
-- Conviene correrlo ANTES de arrancar el binario de la v0.50.0, pero no es obligatorio: sin la columna la lectura de la configuración sigue andando (SettingsRepository cae a la consulta de antes) y lo único que no
-- funciona es elegir un banner («no pude guardar el cambio»). Se corre con psql (NO con el panel de consultas de Railway, que le agrega un LIMIT).
-- =========================================================

ALTER TABLE player_settings ADD COLUMN IF NOT EXISTS profile_banner TEXT NULL;
