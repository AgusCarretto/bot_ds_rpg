# Pruebas del bot (arneses)

Hasta la v0.18.2 estas pruebas vivían fuera del repo (en una carpeta temporal de quien trabajaba) y `MEJORAS.md` las contaba como una deuda. Desde la v0.18.3 están acá.

Cada carpeta es **una consola** (`tests/<nombre>/<nombre>.csproj` + `Program.cs`) que usa el bot de verdad (referencia al proyecto `bot_ds_rpg.csproj`, sin Discord) y chequea un área: las reglas puras, las
transacciones contra la base real, las pantallas (embeds), el enrutado de comandos con el `InteractionService` real y la inyección de dependencias. Imprime una línea `[OK]` o `[FALLA]` por chequeo y termina con
`TODO OK` (salida 0) o `N FALLAS` (salida 1). No usan un framework de pruebas a propósito: son scripts de verificación que se leen de corrido y se pueden correr de a uno.

## Cómo correrlos

Hace falta una base **LOCAL** de Postgres con la instalación completa (`Database/run_fresh_install.sql`, ver `CLAUDE.md`) y su cadena de conexión, de una de estas formas (de más a menos prioridad):

1. la variable de entorno `Postgres__ConnectionString`;
2. un `.env` apuntado por `HARNESS_ENV_FILE`;
3. el `.env` de la raíz del repo (si trabajás en un *git worktree*, el `.env` de la carpeta principal: se busca solo).

```powershell
.\tests\run-all.ps1                              # todos, en orden; deja tests/.logs/<nombre>.log
.\tests\run-all.ps1 -Only dailytest,votetest    # solo esos
dotnet run --project tests/dailytest            # uno solo, con su salida en pantalla
```

En Linux/macOS: `tests/run-all.sh` (igual, y `tests/run-all.sh dailytest votetest`).

## Seguridad: NUNCA contra la base de producción

Los arneses **escriben y borran datos**: crean usuarios descartables (ids `999000000000xxxxx`) y los borran al terminar, y algunos tocan un instante el catálogo (por ejemplo cambian el `type` de una caja para
probar que un pago se revierte) o las filas de `cooldowns`. Por eso `TestEnv` **se niega a correr si el servidor de la cadena no es `localhost`/`127.0.0.1`** (salvo `HARNESS_ALLOW_REMOTE=1`, que no deberías
usar). Si un arnés se corta a la mitad, puede quedar un usuario descartable: se limpia solo en la corrida siguiente.

## Qué cubre cada uno (por familia)

| Arnés | Qué prueba |
|---|---|
| `stage1test` … `stage14test` | las etapas en que se fue armando el juego, una por una (1: eventos, contadores y `/give`; 2a: cajas; 2b: comida y banquetes; 3: misiones y logros; 4: ajustes de recolección y listas; 6: la foto de los NPC; 8: minieventos; 9: economía de las cajas; 10: parseo de los comandos de texto; 12: duelos y habilidades; 13: equipo; 14: curación). La cabecera de cada `Program.cs` dice qué cubre. |
| `abilitytest`, `leaktest`, `raidtest`, `choicetest`, `noticetest`, `embedtest`, `healtest` | el resolutor de turnos y las habilidades, fugas de estado, la concurrencia del raid, las listas desplegables, la entrega de avisos y los límites de los embeds |
| `reworktest`, `cofrestest`, `forgespecialtest`, `portraittest` | los datos del catálogo (recetas, cajas por zona, el Fogón, retratos) contra la base |
| `v83test`, `v90test`, `casinotest`, `pettest`, `fogontest`, `fntest`, `oficiostest`, `typedtest`, `storytest` | una versión o sistema cada uno: banco/polvo/encantamientos, casino, mascotas, Fogón, Fuego Nuevo, oficios, comandos escritos, historia |
| `remindertest`, `deepshoptest`, `bosshptest`, `votetest`, `dailytest`, `v0184test`, `partybosstest`, `v0193test`, `v0194test`, `v0210test`, `v0220test`, `v0222test`, `v0230test`, `v0231test`, `v0232test` | recordatorios, el Tabernero del Inframundo, la vida de los jefes, `/vote`, el daily, la raid cancelada y las mascotas en `/cd`, el jefe en equipo (invitaciones, turnos, reloj, rechazo por zona máxima), el emoji propio de cada ítem en las pantallas que lo muestran por nombre más el ranking por Fuego Nuevo, los íconos propios de la interfaz (FN, LV, XP y las barras de emojis, con su fallback), el minibuff de oro del banco (la curva, el servicio, `/bank` y la ayuda), y los encantamientos con efecto (catálogo, perfil de combate solo contra monstruos, la cura de Sangre, el repositorio con la confirmación de cambiar de efecto, los botones), y el emoji propio de cada ítem en las opciones de los desplegables (taberna, herrero, curación en combate, Inframundo) solo si la aplicación lo tiene, y los títulos (el catálogo de 113, cuándo se desbloquea cada uno, los avisos, la base real, las pantallas, el encabezado del perfil y los comandos), y los atajos de texto (`aa sell`, `aa deposit`, `aa withdraw`, `aa cf`) y los montos con k y m, y la Arena con azar (la compresión de poder, la forma del día, el milagro, lo que gana el de más nivel y el anuncio golpe por golpe) |

## Herramientas de calibración (`tools/`)

`tools/balance2` es el simulador con el que se calibró el combate (partes A–K; la K mide el RAID por zona, nivel y cantidad de jugadores, y acepta un factor de vida del jefe para probar otro: la matriz de combate, el examen de cada jefe con el equipo propio o el de la zona anterior, raids, ritmo de recetas...). Usa el
resolutor **real** (`CombatTurnResolver`) y los monstruos de la base local. Ejemplos: `dotnet run --project tools/balance2 -- J` (cada jefe con el equipo de la zona anterior),
`-- J 2 1.0,1.25 1.0,1.15 13,18,25` (el jefe de la zona 3 con vida ×1,0/1,25, daño ×1,0/1,15 y esos niveles). Cuando cambia una clase, una habilidad, un monstruo o un amuleto, hay que volver a medir.

## Para sumar un arnés

Una carpeta `tests/<nombre>/` con `<nombre>.csproj` mínimo (`<Project Sdk="Microsoft.NET.Sdk"></Project>`: lo demás viene de `tests/Directory.Build.props`) y un `Program.cs` con `Check(bool, string)`. Para la
cadena de conexión usá `TestEnv.Connection()` y para archivos del repo `TestEnv.RepoRoot`; nunca rutas absolutas. Los ids de usuarios descartables, en el rango `999000000000xxxxx`, sin repetir el de otro arnés.
