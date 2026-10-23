-- =========================================================
-- La configuración de cada jugador (v0.26.1, /config, Services/PlayerSettingsService.cs): por ahora una sola opción, mostrar u ocultar la tablita del resumen del combate. Para una base que ya estaba
-- andando: agrega la tabla. schema.sql ya la trae, así que una instalación nueva NO necesita correr esto. Re-ejecutable (IF NOT EXISTS).
--
-- Es OPCIONAL antes de arrancar el binario: sin la tabla el servicio devuelve la configuración de siempre (el resumen se muestra) y registra el aviso como mucho una vez por minuto, así que el bot anda
-- igual, solo que nadie puede cambiarla. Se corre con psql cuando se quiera (NO con el panel de consultas de Railway, que le agrega un LIMIT).
--
-- Sin clave foránea a users a propósito (como player_language). Cada opción nueva es una columna nueva con su DEFAULT: los que no tienen fila ven lo de siempre.
-- =========================================================

CREATE TABLE IF NOT EXISTS player_settings (
    discord_id        BIGINT PRIMARY KEY,
    show_fight_summary BOOLEAN NOT NULL DEFAULT TRUE,
    updated_at        TIMESTAMPTZ NOT NULL DEFAULT now()
);
