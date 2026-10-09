# Cómo desplegar Asado y Acero RPG (v0.6)

El bot es **un solo proceso** que se conecta a Discord (no escucha ningún puerto) y una **base de Postgres**. Sirve
cualquier host que corra contenedores Docker; no está atado a ningún proveedor. Importante:

- **Una sola instancia**: las peleas y los raids viven en memoria. No escalar a 2 réplicas.
- **Un solo bot, UNA instancia corriendo a la vez**: se puede usar el mismo bot (mismo token) para desarrollar y para jugar,
  pero nunca dos copias prendidas al mismo tiempo (tu PC y el servidor): las dos responderían cada comando y se pisarían.
  Si querés probar cambios en tu PC mientras el servidor sigue abierto para tus amigos, hace falta un bot aparte (o apagar el
  del servidor un rato). Mientras todo corra en tu PC no hay problema.
- **Una sola base = los datos de tus amigos son los de desarrollo.** Antes de probar un script de base nuevo, hacé un backup
  (punto 6) y no corras cosas destructivas: los seeds son re-ejecutables, pero un experimento a medias lo sufren todos.
- **Ramas**: `main` es lo que corre (y lo que se despliega); `develop` es donde se trabaja. Para liberar una versión: se
  mergea `develop` en `main` y se le pone el tag (`v0.5.1`). El hosting siempre apunta a `main` o a un tag.
- Cada actualización **corta las peleas y raids en curso** (están en memoria). La base no se pierde nada: el HP se guarda al
  resolver cada pelea. Avisá antes de actualizar.

## 1. Discord (una vez)

En el [Developer Portal](https://discord.com/developers/applications):

1. **New Application** → pestaña **Bot** → **Reset Token** (copialo, es el `DISCORD_TOKEN`; no se vuelve a mostrar).
2. En esa misma pestaña, **Privileged Gateway Intents**: activá **Server Members Intent** y **Message Content Intent**.
   Sin esos dos el bot no conecta (Discord lo rechaza) — el bot lo avisa en el log y se corta a los 90 s.
3. **OAuth2 → URL Generator**: scopes `bot` y `applications.commands`; permisos: View Channels, Send Messages, Embed Links,
   Read Message History, **Use External Emojis**. Abrí la URL y agregalo a tu servidor.
4. Activá el **Modo Desarrollador** (Ajustes → Avanzado), click derecho al servidor → **Copiar ID** (`DISCORD_GUILD_ID`).
   Con ese ID los comandos de barra aparecen al instante; sin él tardan hasta 1 hora.

> Los emojis de los ítems son **emojis de la aplicación** (Developer Portal → tu aplicación → *Emojis*; `<:nombre:id>`): se ven en cualquier
> servidor donde esté el bot, pero **pertenecen a la aplicación**. Si el bot de producción es una aplicación DISTINTA a la de desarrollo,
> sus ids no existen ahí: hay que volver a subir las imágenes en esa aplicación y regenerar `Database/update_item_emojis.sql` con los ids nuevos
> (con la misma aplicación para desarrollo y producción no hay que tocar nada). Las 4 maderas comunes siguen siendo emojis de servidor.

## 2. Opción A — un VPS con Docker Compose (recomendada)

Cualquier VPS con Ubuntu y ~1 GB de RAM alcanza (el bot usa ~150 MB y Postgres ~150-250 MB).

```bash
# En el VPS (Docker y git instalados)
git clone <url-del-repo> bot_ds_rpg && cd bot_ds_rpg
git checkout v0.5.0                         # o la versión que quieras desplegar
cp deploy/prod.env.example .env && nano .env   # completá DISCORD_TOKEN, DISCORD_GUILD_ID y POSTGRES_PASSWORD
docker compose up -d --build
docker compose logs -f bot
```

La primera vez, Postgres carga solo el esquema y todo el catálogo (`Database/run_fresh_install.sql`, 18 scripts). En el log
del bot tenés que ver, en este orden:

```
[INFO] Asado y Acero RPG v0.5.0 arrancando...
[INFO] Base de datos OK (tcp://db:5432 / asado-y-acero), 5 zonas cargadas.
[ÉXITO] ¡Asado y Acero RPG (<nombre del bot>) está en línea!
[INFO] Comandos de barra registrados en el servidor de pruebas (<id>).
```

- Si dice **"la base está vacía"**: la carga inicial falló. Mirá `docker compose logs db`, y para empezar de cero
  (**borra los datos**): `docker compose down -v && docker compose up -d --build`.
- Si el bot se corta con **"No pude usar la base de datos"**: revisá `POSTGRES_PASSWORD` (solo letras y números).
- Si se corta con **"no llegó a conectarse a Discord en 90 segundos"**: token mal o intents sin activar (punto 1).
- Reiniciar: `docker compose restart bot` · Apagar: `docker compose down` (conserva los datos; `-v` los borra).
- Por si el servidor se reinicia, todo está con `restart: unless-stopped` y vuelve solo.

## 3. Opción B — Railway (y otros hostings con Dockerfile)

Railway construye el `Dockerfile` del repo y lo deja corriendo 24 h; `railway.toml` ya trae la configuración (worker sin puerto, una sola
réplica, reinicio automático). Despliega **la rama `main`**: cada push a `main` redespliega solo (y corta las peleas en curso), por eso
el trabajo diario va a `develop` y a `main` solo llegan los releases.

### Paso a paso en Railway

1. **La base.** railway.com → *New Project* → *Database* → *Add PostgreSQL*. Dejale el nombre **Postgres** (las variables del bot lo usan).
2. **Pasar tus datos** (una vez, desde tu PC; si preferís empezar de cero, saltealo y mirá "Base vacía" más abajo):
   - Servicio Postgres → *Settings* → *Networking* → **Enable Public Networking** → pestaña *Variables* → copiá `DATABASE_PUBLIC_URL`.
   - En PowerShell, desde la raíz del repo:
     `.\deploy\migrate-to-railway.ps1 -TargetUrl "<la URL>"`
     Saca una copia de tu base real (queda un respaldo en tu carpeta temporal), la carga en Railway y compara cuántas filas hay en cada
     tabla. Se niega a pisar una base que ya tenga tablas. Necesita las herramientas de PostgreSQL (`pg_dump`, `pg_restore`, `psql`) que ya tenés.
   - Cuando diga "Listo": volvé a **desactivar Public Networking** (el tráfico por ahí se cobra y deja la base expuesta).
3. **El bot.** En el mismo proyecto → *New* → *GitHub Repo* → elegí `bot_ds_rpg` → en *Settings* → *Source* poné la rama **`main`**.
   Railway lee `railway.toml` y usa el `Dockerfile`.
4. **Variables del bot.** Servicio del bot → *Variables* → *Raw Editor* → pegá el contenido de **`.env.railway`** (en la raíz del repo; está
   en `.gitignore`, no se sube nunca; la plantilla sin secretos es `deploy/railway.env.example`) → *Update Variables*.
5. **Mirá el log** (*Deployments* → el deploy → *View logs*): tiene que decir `Base de datos OK … 5 zonas cargadas` y `está en línea`.
6. **Apagá el bot de tu PC.** Es el mismo token: con los dos prendidos cada comando se contesta dos veces. Para desarrollar en tu PC,
   primero parás el servicio en Railway (*Deployments* → los tres puntos del deploy activo → *Remove*; para volver, *Redeploy*).

**Base vacía (sin los datos de tus amigos):** con Public Networking activado, desde la carpeta `Database/`:
`psql "<DATABASE_PUBLIC_URL>" -v ON_ERROR_STOP=1 -f run_fresh_install.sql` (con `$env:PGCLIENTENCODING="UTF8"` antes, en PowerShell).

**Si el bot no arranca:** `No pude usar la base de datos` → revisá que el servicio de la base se llame exactamente `Postgres`; `no llegó a
conectarse a Discord en 90 segundos` → token mal o intents sin activar (sección 1).

### Cuánto cuesta, y qué pasa si pasás los US$5

*(Datos de docs.railway.com consultados el 2026-10-05; verificá en tu cuenta.)*

- El plan **Hobby cuesta US$5/mes e incluye US$5 de uso**. Si el uso pasa de eso **no se corta nada: se cobra la diferencia**
  (ejemplo de Railway: uso de US$7 → factura de US$7 = US$5 del plan + US$2 de exceso).
- Se paga lo que realmente usa: RAM US$10 por GB al mes, CPU US$20 por vCPU al mes, disco US$0,15 por GB al mes, tráfico de salida
  US$0,05 por GB. El uso es de **todo el workspace** (si tenés otros proyectos ahí, suman).
- Estimación para este bot: el bot usa ~150 MB de RAM y casi nada de CPU (~US$1,5-2), Postgres ~150-250 MB (~US$1,5-2,5) y el disco de la base
  casi nada: **~US$4-5/mes en total**. Con 4 jugadores casi no cambia (lo que cuesta es tenerlo prendido). Mirá *Usage* de la primera semana y ajustá.
- Para no llevarte sorpresas: *Workspace* → *Usage* → *Set Usage Limits*. La **alerta por mail** (límite blando) no corta nada; el **límite
  duro** (mínimo US$10) apaga TODOS los servicios cuando se alcanza. Sugerencia: alerta en US$5-6 y, si querés techo, límite duro en US$10.

### Otros hostings con Dockerfile (Fly.io, Koyeb, etc.)

Usan el mismo `Dockerfile` (servicio tipo *worker*, sin puerto ni healthcheck) y una base de Postgres del mismo proveedor. Variables del servicio:

| Variable | Valor |
|---|---|
| `Discord__Token` | el token del bot |
| `Discord__TestGuildId` | el ID del servidor |
| `Postgres__ConnectionString` | `Server=<host>;Port=<puerto>;Database=<base>;User Id=<usuario>;Password=<clave>` (formato clave=valor, no URL) |
| `Raid__MinParticipants` | no la pongas (por defecto 2) |

La base hay que cargarla **una vez desde tu PC** (necesita `psql`), parado en la carpeta `Database/`:

```bash
cd Database
PGCLIENTENCODING=UTF8 psql "host=<host> port=<puerto> dbname=<base> user=<usuario> password=<clave>" -v ON_ERROR_STOP=1 -f run_fresh_install.sql
```

(En Windows/PowerShell: `$env:PGCLIENTENCODING="UTF8"` antes del comando.) **Solo contra una base vacía.** Para llevar tus datos actuales
en vez de empezar de cero, `deploy/migrate-to-railway.ps1` sirve con cualquier Postgres (le pasás la URL `postgresql://…` de la base de destino).

## 4. Opción C — tu PC mientras jugás (gratis)

`dotnet run` con tu `.env` (Postgres local): el bot está online solo cuando tu PC está prendida y con internet. Sirve para
arrancar, no para dejarlo todo el día. Si tus amigos juegan en un horario fijo, puede alcanzar.

## 5. Actualizar a una versión nueva

```bash
git fetch --tags && git checkout v0.5.1       # la nueva versión
docker compose up -d --build                  # reconstruye y reinicia el bot (la base no se toca)
```

Si la versión trae cambios de base, `MEJORAS.md` (sección "Migración de una base existente") dice qué scripts correr:

```bash
docker compose exec -T db psql -U postgres -d asado-y-acero -v ON_ERROR_STOP=1 -f /seed/<script>.sql
```

Para pasar de v0.5.0 a v0.6.0 (eventos, cajas, banquetes, misiones y logros) el orden exacto de los 7 scripts está en la sección
"Eventos de juego, /give, cajas, banquetes, misiones y logros" de `MEJORAS.md`; hacé backup antes (ver sección 6).

**v0.7.0 → v0.8.0 (cajas v2):** ANTES de arrancar el bot nuevo corré `Database/rework_boxes.sql` (agrega las columnas `boxes.min_items/max_items`
que el código lee; si arrancás primero, `/open`, `/shop` y la taberna fallan). Es re-ejecutable y se verifica solo. Después reiniciá el bot
(los pools de materiales y drops de las cajas se cachean 5 minutos). En Railway: corrélo contra la URL pública de la base con
`psql "<DATABASE_PUBLIC_URL>" -v ON_ERROR_STOP=1 -f rework_boxes.sql` (con `$env:PGCLIENTENCODING="UTF8"` en PowerShell) y recién ahí desplegá `main`.

**v0.8.1 → v0.8.2 (caras de los enemigos y Maderas nuevas):** ANTES de arrancar el bot nuevo corré, en este orden, `Database/update_item_emojis.sql` (las 10 armas de Zona 4 y 5
y las 4 Maderas nuevas) y `Database/update_monster_portraits.sql` (agrega la columna `monsters.portrait_emoji`, que el código lee: si arrancás primero, `/hunt`, `/travel`,
`/boss` y `/raid` fallan). Los dos son re-ejecutables y el segundo se verifica solo. Esos emojis son de la **aplicación** del bot: si usás otro bot (otra aplicación), no los tiene y
hay que resubirlos y regenerar los scripts. Después reiniciá el bot.

**v0.8.4 → v0.9.0 (banco, Polvo y encantamientos):** ANTES de arrancar el bot nuevo corré `Database/add_bank_dust_enchants.sql` (agrega a `users` las columnas `has_bank`, `bank_gold`, `dust`, `weapon_enchant` y
`amulet_enchant`, que el código lee en CADA consulta de jugador: si arrancás primero, casi todos los comandos fallan). Es re-ejecutable y se verifica solo; una instalación nueva ya las trae en `schema.sql`. En Railway:
`psql "<DATABASE_PUBLIC_URL>" -v ON_ERROR_STOP=1 -f add_bank_dust_enchants.sql` y recién ahí desplegá `main`. Hacé un backup antes (sección 6).

**v0.9.7 → v0.10.0 (mascotas):** ANTES de arrancar el bot nuevo corré `Database/add_pets.sql` (crea las tablas `pet_species` y `player_pets`, carga los 5 huevos, la Comida para Mascotas y las 5 especies con `seed_pets.sql`,
y le entrega un huevo a quien ya había vencido al jefe de alguna zona). El código lee esas tablas en CADA pelea y en `/profile`: si arrancás primero, las peleas no pueden armar el bono de mascotas (lo registran y siguen sin bono) pero `/pet`,
`/open` de un huevo fallan, y **vencer a un jefe también** (el huevo se entrega en la misma transacción que el cofre, así que la victoria se revierte). Es re-ejecutable y se verifica solo (aborta si no quedan 5 especies, 5 huevos y la comida); una instalación nueva ya las trae (`schema.sql` + `seed_pets.sql` en
`run_fresh_install.sql`). Corrélo desde la carpeta `Database/` (usa `\ir`, así que necesita `psql`): `psql "<DATABASE_PUBLIC_URL>" -v ON_ERROR_STOP=1 -f add_pets.sql` y recién ahí desplegá `main`. Hacé un backup antes (sección 6).

**v0.10.2 → v0.11.0 (El Fogón Eterno):** ANTES de arrancar el bot nuevo corré `Database/add_fogon.sql` (agrega `zones.kind`, `users.in_gate` y `users.gate_cleared`, que el código lee en CADA consulta de jugador o de zona: si arrancás primero, casi todos los comandos fallan; y carga la zona 0, el equipo del Fogón, sus recetas y el jefe con `seed_fogon.sql`).
Es re-ejecutable y se verifica solo; una instalación nueva ya lo trae (`schema.sql` + `seed_fogon.sql` en `run_fresh_install.sql`). Mismo comando que el anterior desde `Database/`: `psql "<DATABASE_PUBLIC_URL>" -v ON_ERROR_STOP=1 -f add_fogon.sql` y recién ahí desplegá `main`. Hacé un backup antes (sección 6).

**v0.11.0 → v0.12.0 (Fuego Nuevo):** ANTES de arrancar el bot nuevo corré `Database/add_fuego_nuevo.sql` (agrega `users.fuego_nuevo` y `users.run_started_at`, que el código lee en CADA consulta de jugador, y las tablas `fuego_nuevo_history`, `player_blessings` y `blessing_offers`, que se leen en cada pelea y en `/profile`: si arrancás primero, casi todos los comandos fallan).
Es re-ejecutable y una instalación nueva ya lo trae (`schema.sql`; no hace falta correrlo en `run_fresh_install.sql`). Mismo comando que los anteriores desde `Database/`: `psql "<DATABASE_PUBLIC_URL>" -v ON_ERROR_STOP=1 -f add_fuego_nuevo.sql` y recién ahí desplegá `main`. Hacé un backup antes (sección 6).
Ojo: `/class` deja de poder cambiar la clase de quien ya pasó del nivel 1 (la clase se vuelve a elegir con cada Fuego Nuevo); avisale a los jugadores.

**v0.13.0 → v0.14.0 (cofres parejos):** ANTES de arrancar el bot nuevo corré `Database/add_zone_boxes.sql` (crea la tabla `zone_boxes` que el código lee en CADA premio de misión, logro y Arena y en cada repetición de jefe, permite el bono `gather` en `pet_species`, y carga con `seed_zone_boxes.sql` las cajas Cofre de Escoria y Brasero del Fogón, la sexta mascota con su huevo y las 26 filas de `zone_boxes`; cambia además lo que paga el Asador a 3000 de oro / 300 de XP). Es re-ejecutable y se verifica solo; una instalación nueva ya lo trae (`schema.sql` + `seed_zone_boxes.sql` al final de `run_fresh_install.sql`). Mismo comando que los anteriores desde `Database/`: `psql "<DATABASE_PUBLIC_URL>" -v ON_ERROR_STOP=1 -f add_zone_boxes.sql` y recién ahí desplegá `main`. Hacé un backup antes (sección 6). Después de editar `zone_boxes` a mano, el bot tarda hasta 5 minutos en notarlo (caché).

**v0.14.1 → v0.14.2 (arma general más barata):** no cambia el esquema ni hace falta reiniciar el bot, pero **corré `Database/rebalance_general_weapons.sql`** en la base (`psql "<DATABASE_PUBLIC_URL>" -v ON_ERROR_STOP=1 -f rebalance_general_weapons.sql`, desde `Database/`): baja a 2 de cada ingrediente las cuatro armas generales de las zonas 2 a 5. Es re-ejecutable y se verifica solo; una instalación nueva ya lo trae. Backup antes (sección 6).

**v0.14.3 → v0.14.4 (trofeos de las cajas):** corré **`Database/retire_box_trophies.sql`** en la base (`psql "<DATABASE_PUBLIC_URL>" -v ON_ERROR_STOP=1 -f retire_box_trophies.sql`, desde `Database/`; en pgAdmin sirve el Query Tool porque no usa comandos de `psql`). Borra los 17 trofeos de las cajas, **reembolsa en oro** (a su precio de venta) a quien tenga alguno y reparte el peso liberado en cada caja. Es re-ejecutable y se verifica solo; una instalación nueva ya lo trae. Backup antes (sección 6). Podés correrlo antes o después de subir el código: los dos funcionan con la base vieja o la nueva.

**v0.15.0 → v0.15.1 (crafteo, amuletos y Polvo):** ANTES de arrancar el bot nuevo corré **`Database/apply_v0151.sql`** (un solo comando que ejecuta, en orden, `rebalance_dust.sql` — agrega la columna `items.dust_value`, que el binario nuevo lee en CADA consulta de ítems —, `rebalance_crafting.sql` y `rebalance_amulets.sql`): `psql "<DATABASE_PUBLIC_URL>" -v ON_ERROR_STOP=1 -f apply_v0151.sql`, desde `Database/` (necesita `psql`, usa `\ir`). Es re-ejecutable y se verifica solo; una instalación nueva ya lo trae. Backup antes (sección 6).

**v0.15.1 → v0.16.0 (recordatorios):** ANTES de arrancar el bot nuevo corré **`Database/add_reminders.sql`** (crea `reminders` y `reminder_settings`, que el código lee y escribe después de CADA comando: si arrancás primero, los comandos salen igual pero no hay avisos, `/reminders` falla y el log se llena de errores cada 5 segundos). `psql "<DATABASE_PUBLIC_URL>" -v ON_ERROR_STOP=1 -f add_reminders.sql`, desde `Database/` (en pgAdmin sirve el Query Tool: no usa comandos de `psql`). Re-ejecutable; una instalación nueva ya lo trae (`schema.sql`).

La carpeta `Database/` del repo está montada en `/seed`, así que los scripts nuevos aparecen con el `git checkout`. Los seeds son
re-ejecutables; **nunca** corras `run_fresh_install.sql` sobre una base con datos.

## 6. Backups (hacelos antes de cada actualización y una vez por semana)

```bash
docker compose exec -T db pg_dump -U postgres -F c asado-y-acero > backup-$(date +%F).dump
# Restaurar (reemplaza lo que haya):
docker compose exec -T db pg_restore -U postgres -d asado-y-acero --clean --if-exists < backup-AAAA-MM-DD.dump
```

Copiá el archivo **fuera del servidor** (a tu PC). Para automatizarlo: `crontab -e` con
`0 4 * * 0 cd /ruta/bot_ds_rpg && docker compose exec -T db pg_dump -U postgres -F c asado-y-acero > /ruta/backups/$(date +\%F).dump`.

## 7. Qué mirar en los logs

Todo sale por la consola (`docker compose logs -f bot`): `[ERROR]` lleva el archivo y el método donde pasó y el stack completo
(antes los errores de los comandos se respondían con "¡Upa!" y no quedaban registrados); `[AVISO]` son fallas esperables (un
mensaje que ya no se puede editar); `[INFO]` es el arranque. Cuando un amigo reporte "me salió ¡Upa! Algo falló", buscá el
`[ERROR]` de esa hora.

## 8. Dónde alojarlo (precios consultados el 2026-10-01; verificá antes de contratar)

| Opción | Costo aprox. | Notas |
|---|---|---|
| Tu PC | 0 | Solo online con la PC prendida. |
| Oracle Cloud *Always Free* | 0 | Hoy 2 OCPU / 12 GB ([recortado a la mitad](https://terminalbytes.com/oracle-cloud-free-tier-changes-2026/)); registro exigente y a veces sin capacidad. |
| VPS Hetzner CX23 (UE) | ~€6/mes | [2 vCPU, 4 GB](https://www.cloudhim.com/cloud-costs/hetzner-cx22-pricing-2026); en EE.UU. cuesta bastante más. Entran este bot **y** otros proyectos. |
| Railway | US$5/mes (incluye US$5 de uso); si te pasás se cobra la diferencia | [Planes](https://docs.railway.com/reference/pricing/plans); bot + base ≈ US$4-5 estimados. Paso a paso y límites de gasto: sección 3. |
| Fly.io | desde ~US$2/mes la máquina | [Sin plan gratis para cuentas nuevas](https://www.saaspricepulse.com/blog/flyio-free-tier-2026); la base de Postgres aparte. |

Con 4 jugadores el consumo casi no cambia: lo que se paga es tener el bot y la base prendidos, no la cantidad de gente.

## 9. Estado de verificación (v0.5.0)

Probado en la PC de desarrollo: arranque **solo con variables de entorno** (sin `.env`), chequeo de base (OK, y corte con código 1
si falta o no responde), corte a los 90 s con token inválido, instalación limpia de los 18 scripts en una base nueva (idéntica a la
real) y todas las pruebas de juego.
**Railway (v0.6):** `railway.toml`, la plantilla de variables, la cadena de conexión (Npgsql 10 + `SSL Mode=Prefer`) y `deploy/migrate-to-railway.ps1` se probaron contra una base local
(copia idéntica a la real: mismas tablas, columnas, restricciones y catálogo, y la instalación limpia con `run_fresh_install.sql` también da lo mismo). No se pudo probar el build real en
Railway ni con Docker: el primer deploy es esa prueba.

**No se pudo probar** (no había Docker en esa PC): construir la imagen y el `docker-compose.yml`; el primer `docker compose up`
es esa prueba. Tampoco el apagado limpio por SIGTERM ni clickear los botones y el desplegable en un Discord real.

## 10. Checklist antes de invitar amigos

- [ ] Bot de producción creado, intents activados, invitado al servidor.
- [ ] El log muestra "Base de datos OK … 5 zonas cargadas" y "está en línea".
- [ ] Probar `/start`, `/hunt`, `/travel` con el desplegable de curar, `/forge recipes`, `/drops`.
- [ ] Un backup hecho. Anotado dónde está el token (solo en el `.env` del servidor).
- [ ] `Raid__MinParticipants` sin tocar (2). Tus amigos arrancan con `/start`.
