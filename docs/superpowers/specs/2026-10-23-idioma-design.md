# Idioma: español e inglés (`/language`) — diseño

Fecha: 2026-10-23. Pedido del dueño: «pasar todo a inglés con un selector (`/language`)», pensando en más jugadores. Aprobado el diseño en la conversación del 2026-10-23.

## Objetivo

Que un jugador pueda usar el bot en inglés o en español, elegir con `/language` (y `aa language`) y que arranque en el idioma de su Discord sin preguntarle nada. Son **dos idiomas y nada más**: no se diseña para un tercero (si algún día hiciera falta, se vuelve a mirar).

## Decisiones tomadas

- **Alcance del primer corte: el motor y las pantallas más usadas.** Lo que todavía no está traducido sigue saliendo en español: nunca se rompe nada, y cada versión traduce otro bloque.
- **Los nombres se quedan en español**: ítems, monstruos, zonas, personajes y títulos (Mate Amargo, Cajón de Pino, Gólem del Yunque, El Tabernero). Son sabor del juego y además son las claves de la base (`/shop buy Mate Amargo`, recetas, emojis). Se traduce todo lo que los rodea.
- **Mecanismo estilo gettext: el texto en español es la clave.** El código sigue legible en español, no hay claves que inventar, y un archivo `Locales/en.json` dice «texto en español» → «English text». Si falta la traducción, sale el español.
- **El idioma por defecto es el de Discord del jugador** (`interaction.UserLocale`): si empieza con `es`, español; si no, inglés.
- **El idioma se guarda en una tabla aparte** (`player_language`), no en una columna de `users`: ninguna consulta de jugador depende de la migración, y el bot anda igual aunque todavía no se haya corrido.

## Fuera de alcance (por ahora)

- La historia (`Lore.cs`) y los temas de ayuda que no son de las pantallas principales (`HelpTopics`).
- Las descripciones de los comandos en el selector de `/` (Discord las traduce aparte, con localizaciones por comando).
- El jefe en equipo (`RaidModule`), la Arena, el jefe mundial, el blackjack, los títulos, los oficios, las mascotas y el Fuego Nuevo.
- Traducir los nombres de ítems, monstruos y zonas.
- Un tercer idioma.

## Mecanismo

Una clase estática `Loc` (`GameData/Loc.cs`, sin dependencias de Discord ni de la base):

```csharp
Loc.T("Estás en plena pelea")            // texto simple
Loc.F($"Tenés {hp} HP")                  // texto con datos: la plantilla es «Tenés {0} HP»
```

- `Loc.Current` es un `AsyncLocal<string>` (`"es"` por defecto). `Loc.Use("en")` abre un alcance que lo fija y lo restaura al cerrarse. Se fija **una vez al despachar cada comando**, así las decenas de métodos estáticos `Execute*Async` y los constructores de pantallas no cambian de firma: leen el idioma de ahí.
- `Loc.T(es)`: en español devuelve el texto; en inglés busca el texto en el catálogo y, si no está, devuelve el español.
- `Loc.F(FormattableString)`: toma la plantilla (`fs.Format`) como clave y los datos (`fs.GetArguments()`) para armar el resultado en el idioma actual. Es un cambio mínimo en el código: se pone `Loc.F(` delante de un `$"..."` que ya existe. En el catálogo la traducción puede reordenar los `{0}`, `{1}`. **En español devuelve exactamente lo que daba el `$"…"` de antes** (`fs.ToString()`, con la misma cultura), así que envolver un texto no cambia ni una coma de lo que hoy se ve.
- Nunca se traduce: los `custom id` de botones y menús, los nombres de comandos, los valores de las opciones (nombres de ítems), las claves de la base. Solo lo que lee el jugador.
- El catálogo (`Locales/en.json`, un objeto «plantilla en español» → «plantilla en inglés») va **incrustado en el ensamblado** y se carga una sola vez al arrancar, así no depende de rutas dentro de Docker.
- **Formato regional**: `GameHistory.Number` pasa a mirar el idioma (español `1.234.567`, inglés `1,234,567`) y se agrega `Loc.Decimal`/`Loc.Percent` (español `4,9 %`, inglés `4.9%`) para los lugares de las pantallas del primer corte que formatean decimales a mano.
- **Plurales**: se resuelven pantalla por pantalla con dos `Loc.T` (uno para 1 y otro para el resto), como ya hace el código con `Plural(n, "turno", "turnos")`. No se arma un sistema general.

## El idioma de cada jugador

- Tabla `player_language (discord_id BIGINT PRIMARY KEY, language VARCHAR(5) NOT NULL, updated_at TIMESTAMPTZ NOT NULL DEFAULT now())`, sin clave foránea (se puede elegir idioma antes de `/start`). Migración `Database/add_language.sql` (re-corrible); `schema.sql` la trae.
- `IPlayerLanguageService` (`Services/PlayerLanguageService.cs`) con un repositorio chico (`Repositories/LanguageRepository.cs`):
  - `ResolveAsync(discordId, discordLocale)`: primero un caché en memoria (30 minutos), después la tabla; si no hay nada guardado, lo deduce del `discordLocale` (`es…` → `es`, otro → `en`) y lo guarda; si no hay locale (comandos de texto), devuelve `es` sin guardar.
  - `SetAsync(discordId, language)`: guarda y actualiza el caché.
  - **Nunca tira**: si la tabla no existe (migración sin correr) o la base falla, devuelve `es`, lo registra una vez por minuto como mucho, y el bot sigue como hoy.
- `/language [idioma]` (opciones «Español» y «English») y `aa language [es|en]` (alias `idioma`): sin argumento muestra el idioma actual y cómo cambiarlo; con argumento lo guarda y contesta **ya en el idioma nuevo**. La descripción del comando es bilingüe y de menos de 100 caracteres (el límite de Discord que ya nos tumbó el registro una vez).

## Dónde se fija el idioma

- `Program.HandleInteractionAsync` (comandos de barra, botones y menús): resuelve el idioma con `interaction.UserLocale` y abre `Loc.Use(...)` antes de cualquier respuesta, incluidos el aviso de «todavía no te registraste» y el bloqueo de pelea.
- `Program.HandleTextMessageAsync` (comandos `aa`): resuelve el guardado (sin locale) y abre `Loc.Use(...)`.
- **Fuera de un comando**: los recordatorios (`ReminderScheduler`) resuelven el idioma de cada destinatario y arman sus líneas dentro de su alcance. Lo que se arma dentro del flujo del comando (avisos de nivel, logros, el mensaje de combate abandonado, que nace en una tarea lanzada desde el comando) ya hereda el idioma por el `AsyncLocal`.
- **Mensajes compartidos** (anuncio de la Arena y del jefe mundial, y el jefe en equipo): en el primer corte salen en español, o en el idioma de quien los dispara. Se decide cuando se traduzcan.

## Primer corte: qué se traduce

Perfil; el combate solitario completo (`/hunt`, `/travel`, `/boss`: encuentro, turnos, victoria, derrota, huida, abandono, `aa attack|ability|flee`, `/use`, `/autohunt`) con las frases de los monstruos y de los personajes de `NpcDialogue`; `/chop` y `/mine`; `/inventory`; la taberna, `/heal` y `/shop`; `/daily`; `/missions`; `/start` y las descripciones de las clases; `/cd`; el aviso de subida de nivel; `/info` (la lista y los temas de primer contacto: combate, recolectar, tienda, daily y misiones); el bloqueo en pelea; los avisos de error («¡Upa!») y los de cooldown; y `/language`.

## Controles automáticos

- **`tools/loc`** (consola): recorre `Modules`, `GameData`, `Services` y `Program.cs`, extrae los textos de cada `Loc.T("…")` y `Loc.F($"…")` (acepta literales unidos con `+`, y avisa de cualquier llamada cuyo argumento no sea un literal) y los compara con `Locales/en.json`. Informa: **falta en inglés**, **sobra** (el texto cambió y la entrada quedó vieja) y **placeholders distintos** (`{0}`, `{1}` que no coinciden entre español e inglés).
- **Arnés `loctest`** (en `tests/`): corre esa misma revisión y falla con cualquiera de las tres. Además renderiza las pantallas del primer corte con `Loc.Use("en")` contra la base real y revisa que **no se cuele español**: ningún carácter `áéíóúñ¿¡` ni una lista corta de palabras (` de `, ` tu `, `tenés`…) fuera de los nombres que se quedan en español (ítems, monstruos, zonas, personajes y clases, sacados de la base y del catálogo). También revisa que cada embed traducido entre en los 6.000 caracteres de Discord y que cada frase de `NpcDialogue` tenga su par en inglés.
- Los textos con el idioma en español no cambian: los arneses viejos que comparan texto siguen valiendo, porque `Loc.Current` es `es` salvo adentro de un `Loc.Use("en")`.

## Manejo de errores

- Una clave que no está en el catálogo, un catálogo que no carga, una tabla de idioma que no existe: siempre se cae a español, nunca se rompe un comando.
- Un `{0}` de más o de menos en una traducción hace caer esa frase al español en vez de tirar `FormatException` (se captura al armar el texto) y la herramienta la marca.

## Despliegue y pruebas

- La migración `add_language.sql` **no es obligatoria antes del binario** (sin ella todos quedan en español). Con ella corrida, cada jugador se detecta en su primer comando.
- Las traducciones las redacto yo (tono de juego, informal, sin regionalismos del Río de la Plata en inglés); el dueño puede revisarlas leyendo `Locales/en.json`, que es un solo archivo.
- Pruebas: `loctest` (arriba), las reglas puras de `Loc` (español devuelve lo mismo, inglés cae a español, reordenar placeholders, un `{0}` roto, el alcance anidado y que un hilo no pisa el idioma de otro), el servicio de idioma contra la base real (guardar, deducir del locale, caché, tabla ausente) y el registro de `/language` con el `InteractionService` real.

## Riesgos y límites conocidos

- **Texto escrito que hay que teclear:** si una pantalla en inglés le dice al jugador que escriba una palabra (`aa enchant arma crítico`), el comando de texto tiene que entender también la palabra en inglés (`weapon`, `critical`). Para cada pantalla del primer corte se revisa qué palabras sugiere y se agregan los sinónimos en inglés al comando.
- **Embeds más largos en inglés:** el tope de 6.000 caracteres y de 1.024 por campo se revisa en `loctest`.
- **Mezcla de idiomas en pantallas compartidas** (jefe en equipo, Arena): ya descrito arriba.
- Un cambio en un texto en español sin tocar el catálogo deja la traducción vieja huérfana y ese texto sale en español hasta que se corrija: lo detecta la herramienta (**sobra / falta**).
