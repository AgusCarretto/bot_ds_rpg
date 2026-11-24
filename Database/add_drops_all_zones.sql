-- =========================================================
-- v0.65.0: cómo ve cada jugador /drops (/config, «aa config drops todas», Services/PlayerSettingsService.cs). FALSE (lo de siempre) = UNA zona por mensaje con un menú para pasar a otra; TRUE = la vista compacta de antes:
-- todas las zonas juntas, con la cacería y el viaje de cada una. Para una base que ya estaba andando: agrega la columna a player_settings. schema.sql ya la trae, así que una instalación nueva NO necesita correr esto.
-- Re-ejecutable (IF NOT EXISTS) y sin tocar las filas que ya había: todas quedan viendo una zona por vez.
--
-- Conviene correrlo ANTES de arrancar el binario de la v0.65.0, pero no es obligatorio: sin la columna la lectura de la configuración sigue andando (SettingsRepository prueba con las columnas que existen) y lo único que
-- no funciona es elegir la vista compacta de /drops («no pude guardar el cambio»). Se corre con psql (NO con el panel de consultas de Railway, que le agrega un LIMIT).
-- =========================================================

ALTER TABLE player_settings ADD COLUMN IF NOT EXISTS drops_all_zones BOOLEAN NOT NULL DEFAULT FALSE;
