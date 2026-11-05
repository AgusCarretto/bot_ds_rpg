-- =========================================================
-- v0.51.0: qué líneas de /cd ve cada jugador y en qué orden (/config, «aa cd orden», GameData/CooldownLayout.cs). Guarda las claves de las líneas en el orden del jugador, separadas por comas, con un «-» delante
-- de las que ocultó («hunt,travel,-chop,mine,...»); NULL = todas, en el orden de siempre (lo de antes). Para una base que ya estaba andando: agrega la columna a player_settings. schema.sql ya la trae, así que
-- una instalación nueva NO necesita correr esto. Re-ejecutable (IF NOT EXISTS) y sin tocar las filas que ya había: todas quedan viendo todo.
--
-- Conviene correrlo ANTES de arrancar el binario de la v0.51.0, pero no es obligatorio: sin la columna la lectura de la configuración sigue andando (SettingsRepository prueba con las columnas que existen) y lo
-- único que no funciona es guardar el orden o las líneas ocultas de /cd («no pude guardar el cambio»). Se corre con psql (NO con el panel de consultas de Railway, que le agrega un LIMIT).
-- =========================================================

ALTER TABLE player_settings ADD COLUMN IF NOT EXISTS cd_layout TEXT NULL;
