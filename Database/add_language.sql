-- =========================================================
-- El idioma de cada jugador (v0.26.0, GameData/Loc.cs y Services/PlayerLanguageService.cs): español o inglés. Para una base que ya estaba andando: agrega la tabla.
-- schema.sql ya la trae, así que una instalación nueva NO necesita correr esto. Re-ejecutable (IF NOT EXISTS).
--
-- Es OPCIONAL antes de arrancar el binario: sin la tabla el servicio de idioma devuelve español para todos (y registra el aviso como mucho una vez por minuto), así que el bot anda igual,
-- solo que nadie puede elegir inglés. Se corre con psql cuando se quiera (NO con el panel de consultas de Railway, que le agrega un LIMIT).
--
-- Sin clave foránea a users a propósito: se puede elegir idioma con /language antes de /start.
-- =========================================================

CREATE TABLE IF NOT EXISTS player_language (
    discord_id BIGINT PRIMARY KEY,
    language   VARCHAR(5) NOT NULL CHECK (language IN ('es', 'en')),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
