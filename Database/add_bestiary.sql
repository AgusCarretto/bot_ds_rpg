-- El bestiario (v0.56.0): qué monstruos venció cada jugador y cuántas veces. Lo llena el bot solo, cada vez que se anota un enemigo
-- vencido (GameEventService → IBestiaryRepository.RecordKillAsync). NO es retroactivo (el dueño eligió «todo desde cero»).
-- Opcional antes del binario: sin la tabla, /bestiary contesta un error y las victorias se anotan igual (el fallo solo deja un aviso en el log).
-- Re-ejecutable.
CREATE TABLE IF NOT EXISTS player_bestiary (
    discord_id    BIGINT  NOT NULL,
    monster_id    INTEGER NOT NULL REFERENCES monsters (monster_id) ON DELETE CASCADE,
    kills         BIGINT  NOT NULL DEFAULT 0 CHECK (kills >= 0),
    first_kill_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (discord_id, monster_id)
);
