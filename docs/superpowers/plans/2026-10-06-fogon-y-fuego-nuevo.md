# El Fogón Eterno (v0.11) y Fuego Nuevo (v0.12) — plan de implementación

> Spec: `docs/superpowers/specs/2026-10-06-fuego-nuevo-design.md` (aprobado por el dueño el 2026-10-06: «Dale arma todo si me gusta»). Ejecución en línea, tarea por tarea, con el arnés de pruebas de cada versión. Oficios (v0.13) queda fuera: pide su propio diseño.

**Meta de estado (para retomar tras una compactación):** el avance de cada tarea se anota en la memoria del proyecto (`project_design_intents_and_roadmap.md`) y en `git log`; lo que no tenga commit no está hecho.

## Decisiones ya tomadas (no reabrir)

- La **Zona 0** es una fila de `zones` con `kind = 'gate'` y `zone_id = 0`. `IZoneRepository.GetAllAsync` y `IMonsterRepository.GetAllAsync` devuelven SOLO lo de las zonas normales; la puerta se pide aparte. Por eso `/zonas`, la escalera, las recetas por zona, los drops, las cajas, las mascotas y los arneses viejos no la ven.
- El jugador **nunca** tiene `current_zone_id = 0`: `/zona 0` prende `users.in_gate` (su zona sigue siendo la última normal). `/zona <otra>` lo apaga. Mientras `in_gate`: `/hunt`, `/travel` y `/raid` no andan y `/boss` pelea al Asador.
- Entrar exige: jefe de la última zona normal vencido (`highest_zone_cleared` ≥ esa zona) y **arma y amuleto del Fogón puestos**. Se vuelve a comprobar al empezar el combate.
- Equipo del Fogón (calibrado con `fogonsim`, nivel 28): **Trinche del Asador Eterno** arma +128 ATQ (sin familia, sin sinergia) y **Brasa del Fogón Eterno** amuleto +120 DEF, rareza Mítico. **El Asador Eterno**: HP 2002–2401 (jefe de Zona 5 ×1,3), daño 188–244 (×1,2), oro/XP de bono 1500/1500: con el equipo del Fogón pierde ~16 % y con el de Zona 5 (arma de clase + amuleto) ~75 %.
- Recetas del Fogón (zona 0, sin afinidad): arma = Colmillo de Jabalí, Garra de Puma Cenizo, Yunque Fragmentado, Núcleo de Magma y Corazón de Titán ×5 cada uno + Madera de Ébano ×4 + Hierro ×18, 25.000 de oro; amuleto = Pluma de Ñandú, Esencia Espectral, Gema en Bruto, Escama Ígnea y Fragmento de Alma ×5 cada uno + Gema de Zafiro ×4 + Carbón ×18, 25.000 de oro. Sin materiales Míticos (el 0,5 % por acción es una lotería demasiado cruel). Se mide con `report_recipe_pacing.sql` (~14 h de juego perfecto).
- Ganarle al Asador: en la MISMA transacción del premio, `gate_cleared = true`, `in_gate = false` y vuelve a la última zona normal; no toca `highest_zone_cleared`; evento `gate_win`; logro «Asador».
- Fuego Nuevo (v0.12): `/fuegonuevo` (alias texto `aa fuegonuevo`, `aa fn`), confirmación en dos pasos (resumen y elegir clase), UNA transacción. Se va: nivel/EXP, zonas, equipo con encantamientos, materiales, drops, cajas, comida que cura, Polvo, buffs, cooldowns. Se queda: mascotas, **huevos sin abrir y Comida para Mascotas** (son de las mascotas; decisión mía, se avisa en la pantalla), logros, misiones reclamadas, trofeos, historial, oro en mano y banco, bendiciones, racha diaria.
- % por Fuego Nuevo (lineales, `base × (1 + paso × N)`): `/hunt` chance ×1,30; `/chop` y `/mine` cantidad ×1,20; EXP +10 % (mío); `/travel` base 40 % con paso +10 % que baja 0,5 puntos por FN (mínimo +2 %). Sin oro. El cofre del jefe no cambia.
- Bendiciones: una por FN entre 3 sorteadas, niveles I–V al repetir, pool de 11 (spec sección 6), la oferta queda guardada en la base. Nada de lo permanente cuenta en duelos ni Arena.

## v0.11.0 — El Fogón Eterno

- [x] **T1 Base de datos.** `zones.kind`, `users.in_gate`, `users.gate_cleared`, `Database/add_fogon.sql` (re-ejecutable: columnas + fila de la Zona 0 + ítems + recetas + monstruo, todo con su verificación) y `seed_fogon.sql` incluido por `run_fresh_install.sql`; `schema.sql`; arreglar los scripts viejos que recorren `zones` (`renumber_zones_consecutively`, verificaciones del rework, los `report_*`). Aplicar a la base viva (con backup) y comparar con una instalación limpia.
- [x] **T2 Modelos y repositorios.** `Zone.Kind`, `User.InGate/GateCleared` en `UserSql`; `GetAllAsync` solo normales + `GetGateAsync`; `MonsterRepository.GetAllAsync` solo de zonas normales; `IUserRepository.SetInGateAsync` y `ChangeZoneAsync` apaga `in_gate`.
- [x] **T3 Reglas puras.** `GameData/FogonRules.cs`: nombres del equipo, `CanEnter`, estados y textos; `GameData/NpcDialogue` líneas del Asador.
- [x] **T4 `/zona 0` y `/zonas`.** Entrar, salir, la línea del Fogón en `/zonas`, autocompletado, `aa zona 0`.
- [x] **T5 Combate.** Bloquear hunt/travel/raid en la puerta, `/boss` contra el Asador (con la comprobación del equipo), victoria transaccional (`ApplyBossVictoryAsync` rama puerta), `LevelUpOutcome.GateCleared`, mensaje de victoria, evento `gate_win`, logro «Asador».
- [x] **T6 Recetas y herrería.** `RecipeCatalog.ViewFor(..., includeGate)`, menú y página de recetas del herrero, `/forge make` y su autocompletado, `/tips`.
- [x] **T7 Ayuda y perfil.** Tema `/info tema:fogon`, línea en `/info`, `/profile`, `docs`.
- [x] **T8 Pruebas, documentación y release v0.11.0.** Arnés `fogontest`; correr los 28 arneses; instalación limpia vs. base real; `CLAUDE.md`, `MEJORAS.md`, `DEPLOY.md` (`add_fogon.sql` ANTES de arrancar), versión, commit, develop → main + tag.

## v0.12.0 — Fuego Nuevo

- [x] **T9 Base de datos.** `users.fuego_nuevo`, `users.run_started_at`, `fuego_nuevo_history`, `player_blessings`, `blessing_offers`; `add_fuego_nuevo.sql`; esquema; instalación limpia.
- [x] **T10 Reglas puras.** `GameData/FuegoNuevoRules.cs` (pasos, multiplicadores, tabla de `/travel`), `GameData/BlessingCatalog.cs` (pool, niveles, sorteo de ofertas, efectos) y `PlayerBonuses` (mascotas + FN + bendiciones) que reemplaza a `PetBonuses` en recompensas y perfil de combate.
- [x] **T11 Repositorios.** `IFuegoNuevoRepository.RenewAsync` (una transacción) y `IBlessingRepository` (niveles, oferta pendiente, elegir).
- [x] **T12 Enganches.** Recompensas (drop de hunt y travel, EXP), cantidad de `/chop` y `/mine`, perfil de combate (ATQ, DEF y vida de las bendiciones, solo contra monstruos), mascotas (Manada, Buen Pienso), comida (Buen Mate), `/class` solo al reiniciar.
- [x] **T13 Comandos.** `/fuegonuevo` (resumen, clase, confirmación), `/blessings`, `aa fuegonuevo|fn`, botones, perfil («Fuego Nuevo ×N» y bendiciones), tema de ayuda, logros, eventos.
- [x] **T14 Pruebas, documentación y release v0.12.0.** Arnés `fntest` (transacción, doble click, qué se va y qué queda, ofertas, efectos), los 29 arneses, instalación limpia, docs, versión, release.
