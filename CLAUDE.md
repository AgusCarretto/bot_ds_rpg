# CLAUDE.md

Índice corto. El detalle de cada sistema (números, historia, decisiones del dueño) está en `docs/claude/`:
leé SOLO el archivo del área que vas a tocar (mapa abajo). `MEJORAS.md` = deuda y pendientes.

## Proyecto y stack

"Asado y Acero RPG": bot de Discord, RPG de texto. C# / .NET 10, Discord.Net (slash con `InteractionService`
+ comandos de texto con prefijo `aa ` vía `CommandService`), PostgreSQL con Dapper/Npgsql (sin EF Core).
Textos al jugador y comentarios en español rioplatense (el dueño es URUGUAYO); desde v0.26.0 también inglés (`/language`).
Capas: `Modules/` (handlers) → `Repositories/` (Dapper + SQL, uno por agregado) + `Services/` (sin SQL) + `GameData/` (puro, sin I/O).

## Reglas innegociables

**Git**
- Trabajar en `develop` (o una rama propia). `main` es lo que Railway despliega: cada push corta las peleas en curso.
  Nunca push/merge a `main` sin que el dueño lo pida. Release = merge `develop`→`main` + tag `vX.Y.Z` + `<Version>` del csproj.
- Commits solo si el dueño los pide.

**Transacciones (oro, inventario, cooldowns)** — `DbConnection`/`DbTransaction` explícitos, un commit o rollback.
- *Update guardado*: `UPDATE ... WHERE <precondición> RETURNING ...` (0 filas = no se cumple). Para gastar oro/ítems, cooldowns.
- *`SELECT ... FOR UPDATE`* cuando hace falta leer antes de escribir. Dos filas de usuario: bloquear ordenadas por `discord_id`.
- Nunca chequear y después actuar en dos sentencias sueltas. Helpers: `Repositories/TransactionalHelpers.cs`.
- `IGameEvents.RecordAsync` va DESPUÉS del commit (nunca adentro) y nunca tira.

**Errores** — todo `catch (Exception ex)` llama `BotLog.Error(ex)` antes de responder el "¡Upa!". Fallos esperables e
inofensivos: `BotLog.Warn`. Nunca tragar una excepción en silencio.

**Patrón de comandos** — la lógica va en un `public static Execute*Async` / `Build*Embed` del módulo slash (sin `Context`);
el slash y el `TextCommandModule.*` parcial llaman a ESE método. Nunca reimplementar lógica en el lado de texto.
- Ids de componentes llevan el id del dueño (`accion:{owner}:...`) y validan quién aprieta.
- Botones en un módulo con `[Group]` reciben el prefijo del grupo: ponerlos en un módulo sin `[Group]`.
- Descripción de un slash ≤ 100 caracteres (si no, se cae el registro de TODOS los comandos).
- Autocompletes: derivan de `SafeAutocompleteHandler`, usan `GetByDiscordIdAsync` (abrir una lista no crea cuentas).
- Comando nuevo: queda bloqueado en pelea salvo que se agregue a la allow-list de `GameData/FightLock.cs`.
- No pasar `InteractionService` a `RunMode.Sync`; los avisos se entregan en `NoticeDelivery` (ver arquitectura).

**Idioma (`Loc`, `Locales/en.json`)**
- Literal → `Loc.T("...")`; interpolado → `Loc.F($"... {x} ...")`; texto en tabla/constante → `Loc.Mark` donde se declara
  y `Loc.Tr` donde se muestra (nunca `T`/`F` en un `static readonly`).
- Una oración entera por variante (nada de pegar "otr"+"a"). Nunca decidir comparando texto en español.
- Nunca traducir custom ids, nombres de comando, valores de opción ni claves guardadas. Nombres propios quedan en español.
- Números: `GameHistory.Number`, `Loc.Decimal`, `Loc.Percent`.
- `Loc.Use(lang)` se abre en el método que despacha (un `AsyncLocal` puesto dentro de un helper async no vuelve al llamador).
- Después de tocar textos: `dotnet run --project tools/loc -- --check` debe dar 0/0/0/0.

**UI de Discord (reglas del dueño, pensadas para el celular)** — detalle en `docs/claude/ui-embeds-emojis-idioma.md`
- Mensajes cortos y con aire; lo secundario va a otro comando o a un mensaje aparte.
- Nunca un campo vacío como separador; nunca "chips" (varios ítems por línea): UN ítem por línea, `icono **Nombre**: 1.234`.
- Columnas inline: completar la fila con un tercer campo vacío inline; listas largas partidas en mitades lado a lado.
- Barras de emoji solo en campos de ancho completo (8 piezas en el perfil); en columnas, barra de texto.
- Emojis propios: no se dibujan en títulos de embed, nombres de campo, footers, texto de opciones de menú ni autocompletes
  → ponerlos en descripción/valor de campo, o como thumbnail (`ItemDisplay.ImageUrl`).
- Ítem mostrado por nombre → `ItemEmojiRegistry.Label/IconOf`, nunca un emoji unicode a mano.
- Opciones de menú con emoji → `ItemMenuExtensions.AddItemOption` (un emoji desconocido rechaza el mensaje ENTERO).
- Límite: 6000 caracteres por mensaje (todos los embeds juntos), 1024 por campo; `EmbedBuilder.Build()` tira si se pasa.
- Espacio al final de un campo: salto + U+200B; buscarlo con `StringComparison.Ordinal`.

**Balance, datos y migraciones — no tocar sin aprobación del dueño**
- Números de balance (chances de drop, recetas, vida/daño de monstruos, precios, multiplicadores, curvas) los decide el dueño.
  Proponer con números medidos (`tools/balance2`, `Database/report_*.sql`), no cambiar por cuenta propia.
- Migraciones SQL: muchas van ANTES del binario (el código lee la columna). Ver `docs/claude/setup-db-tests.md`.
- Ítem del catálogo agregado/editado a mano → escribirlo en un `Database/*.sql` en la misma sesión.
- No re-correr seeds viejos contra una base viva (`rework_drops_and_recipes.sql`, `seed_fogon.sql`, `rework_boxes.sql`,
  `rebalance_dust.sql`, etc.): traen estados anteriores o borran. Borrar ítems hace CASCADE en inventarios.
- Toda pantalla de PvP (duelos, Arena) se calcula SIN `PlayerBonuses` (mascotas, FN, bendiciones, dragón, platos).
- La base guarda la vida REAL; toda vida mostrada pasa por `HpDisplay.Scale` (ver combate).

## Verificación

```
dotnet build                                   # después de cualquier cambio
.\tests\run-all.ps1                            # todos los arneses (tests/run-all.sh en bash); -Only a,b para algunos
dotnet run --project tests/<nombre>            # un arnés suelto
dotnet run --project tools/loc -- --check      # traducciones: 0 faltantes / 0 viejas / 0 {n} mal / 0 no literales
dotnet run --project tools/balance2 -- <parte> # simulador de balance (partes A–N)
```
Los arneses ESCRIBEN y borran datos: solo contra una base LOCAL (`TestEnv` rechaza otra salvo `HARNESS_ALLOW_REMOTE=1`;
nunca producción). Si un cambio rompe un arnés, decidir si el arnés fijaba el comportamiento VIEJO a propósito o si el código está mal.
Config: `.env` → User Secrets → variables de entorno (`Discord__Token`, `Discord__TestGuildId`, `Postgres__ConnectionString`).

## Mapa: qué leer según lo que vas a tocar

| Área | Archivo |
|---|---|
| Comandos, tests, `.env`, orden de instalación de la base, migraciones, drift del catálogo | `docs/claude/setup-db-tests.md` |
| Capas, comando dual, transacciones, catálogo por datos, autocompletes, eventos/stats, errores, deploy (Railway, Docker) | `docs/claude/arquitectura.md` |
| Pelea solo (estado en memoria, comandos tipeados, huir, bloqueo en pelea, curación), jefe con amigos/raid, habilidades, PvP y Arena, `/heal`, cooldown del jefe | `docs/claude/combate.md` |
| Zonas 1–5, drops y chances, `zone_boxes`, viaje sin drop, amuletos y defensa, balance por clase (`PveTuning`, HP mostrada), XP de peleas y curva, vida de jefes, escalera de dificultad | `docs/claude/balance-zonas-drops.md` |
| Zonas 6–10, `min_fuego_nuevo`, Reliquias, las 5 mazmorras, laberinto de humo, Asador escalado | `docs/claude/zonas-6-10-mazmorras.md` |
| Recetas de forja y recetario (2 formas), pesca, cocina y `/stats`, herramientas y Carpintero, `/chop` `/mine`, canje 3×1, `/tips`, equipo (no hay `/equip`), Polvo, encantamientos y efectos, oficios | `docs/claude/crafteo-recoleccion.md` |
| `/daily`, cajas y su botín, trofeos retirados, misiones y logros, `/trade`, montos (`all`, `10k`), atajos `aa sell/deposit/cf`, historial, banco y penalidad de muerte, blackjack, jefe mundial, títulos, logros por página, sell all / `/stats` de otro (v0.53.0) | `docs/claude/economia-progreso.md` |
| **UI**: bestiario (v0.56.0), inventario y perfil legibles, NPCs y escena del herrero, banner de nivel, taberna, pestañas del inventario, emojis de ítems/aplicación/registro, íconos y barras, emojis en menús, imágenes de NPC, `/info` por tema, idioma, `/config` y tabla de resumen, banner del perfil, `/cd` personalizable, respuesta = mención | `docs/claude/ui-embeds-emojis-idioma.md` |
| Dragón (cría, entrenamiento, tablón), mascotas (tiers, evolución, huevos, expediciones — sus premios son SECRETOS en pantalla) | `docs/claude/mascotas-dragon.md` |
| El Fogón (Zona 0), Tabernero del Inframundo, Fuego Nuevo y bendiciones, la historia (Crónicas del Fogón) | `docs/claude/fogon-fn-historia.md` |
| Mini-evento, recordatorios, voto top.gg y su página (`docs/topgg/`), `aa admingive/adminset`, códigos canjeables, invitaciones | `docs/claude/sistemas-comunidad-admin.md` |

Comentarios del código que dicen "ver CLAUDE.md, «X»" → buscar «X» con Grep en `docs/claude/`.

## Regla operativa: ahorrar contexto

- No leer archivos grandes enteros en el hilo principal. Para auditar o recorrer varios archivos, mandar un subagente
  `Explore` que lea y devuelva SOLO un resumen (hallazgos con `archivo:línea`), no el contenido.
- Buscar con Grep/Glob antes de abrir; abrir con `offset`/`limit` la parte que hace falta.
- De `docs/claude/` leer solo el archivo del área; dentro de él, Grep por el nombre del sistema.
- Al cerrar un cambio que el jugador nota: sumar el párrafo al archivo de `docs/claude/` que corresponde (no a este índice)
  y, si aplica, el anuncio del día en `docs/topgg/anuncios.md` (uno por día, fusionado).
