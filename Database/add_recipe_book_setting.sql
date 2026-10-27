-- =========================================================
-- v0.28.1: qué recetario ve cada jugador en la herrería (/config, Services/PlayerSettingsService.cs). El CLÁSICO (una zona por página, con lo que tenés de cada material) es el de siempre y va por defecto;
-- el COMPLETO de la v0.27.0 (todas las zonas juntas) se elige desde /config o con «aa config recetas completo». Para una base que ya estaba andando: agrega la columna a player_settings.
-- schema.sql ya la trae, así que una instalación nueva NO necesita correr esto. Re-ejecutable (IF NOT EXISTS) y sin tocar las filas que ya había: todas quedan en el clásico.
--
-- Correrlo ANTES de arrancar el binario de la v0.28.1: la lectura de la configuración pide la columna nueva; sin ella el servicio contesta lo de siempre (resumen prendido, recetario clásico) y registra el aviso como
-- mucho una vez por minuto, pero quien había apagado el resumen lo vería de nuevo y nadie podría cambiar nada hasta que se corra. Se corre con psql (NO con el panel de consultas de Railway, que le agrega un LIMIT).
-- =========================================================

ALTER TABLE player_settings ADD COLUMN IF NOT EXISTS full_recipe_book BOOLEAN NOT NULL DEFAULT FALSE;
