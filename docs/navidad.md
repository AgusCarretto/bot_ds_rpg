# ❄️ Navidad en el Fogón — diseño (borrador para aprobar)

**Plazo: abre el 1 de diciembre de 2026 (18 días desde el 13-11) y cierra solo el 7 de enero.**

Estado: diseño aprobado en lo general por el dueño (2026-11-13). Los números marcados **(a medir)** son propuesta mía:
se miden con `tools/balance2` antes de cerrar y los decide el dueño.

## Calendario

| Fechas | Qué pasa |
|---|---|
| 1 → 24 de diciembre | **Farmeo**: caen Regalos, calendario de adviento, Krampus y el Muñeco de Nieve Gigante activos. |
| 25 de diciembre → 6 de enero | **Tienda**: ya no caen Regalos; se gastan en la tienda o se venden por oro. |
| 7 de enero | Los Regalos que sobren **desaparecen**. Lo comprado queda para siempre. |

Las fechas van en configuración (`Christmas__Start`, `Christmas__FarmEnd`, `Christmas__End`) para poder probarlo antes.
Tono: Navidad **clásica de nieve**.

## 🎁 Regalos (moneda del evento)

- Sobreviven al Fuego Nuevo; se borran al terminar el evento (7 de enero).
- No son un ítem del inventario: un saldo aparte (no se comercian con `/trade`, no se pierden al morir).
- De dónde salen (solo del 1 al 24) **(a medir)**:

| Fuente | Regalos |
|---|---|
| `/hunt` ganado | 1, con **50 %** de chance (dueño) |
| `/travel` ganado | 5 (dueño) |
| Jefe de zona / en equipo (cada 10 h) | 50 (dueño) |
| Krampus | 30–40 (dueño) |
| `/daily` | 10 (+5 con racha de 7) |
| Evento random del canal | 5 al que lo agarra |
| Calendario de adviento | 20 por día, doble los días 5, 12 y 19, **150 el 24** (+ cajas, oro, Polvo, Campanas) |

Estimado: jugador activo ~330/día (~8.000 en los 24 días; el Reno cerca del día 14), jugador casual ~140/día (~3.400).

## 📅 Calendario de adviento (`/navidad`)

- Una caja por día del 1 al 24; se reclama una vez por día (hora de Uruguay, `UruguayCalendar`). Día salteado = perdido.
- Base: **20 Regalos todos los días** (dueño). Además, días especiales (propuesta; las cajas son las de la zona del jugador):

| Día | Premio |
|---|---|
| Todos | 20 Regalos |
| 3, 10, 17 | + bolsa de oro (escalada a tu zona, como el `/daily`) |
| 5, 12, 19 | **Doble**: 40 Regalos en vez de 20 |
| 7, 14 | + una caja de tu zona |
| 9, 16, 23 | + Polvo |
| 11, 18 | + 5 🔔 Campanas |
| 21 | + **caja grande** de tu zona |
| 24 | **Nochebuena**: 150 Regalos + 🧦 Calcetín + caja grande |

Total del calendario completo: 670 Regalos + 4 cajas + Calcetín + oro, Polvo y 10 Campanas.

## 👹 Krampus (1 → 24)

- Jefe mundial para pelear con tu personaje, como `aa miniboss`, escalado a tu zona.
- Cooldown **propio** de 4 horas **(a medir)** (no gasta el del jefe de zona).
- Suelta 30–40 Regalos + oro.

## ☃️ El Muñeco de Nieve Gigante (1 → 24)

- **Uno solo para todos los servidores**: la vida vive en la base y todos ven el mismo número.
- Panel `/navidad muñeco`: barra de vida, botones para tirar y las últimas tiradas («Fulano tiró un Petardo −600»).
  Se refresca solo cada ~5 s mientras alguien tira.
- Proyectiles (se pagan con Regalos; los caros rinden un poco más por Regalo) **(a medir)**:

| Proyectil | Costo | Daño |
|---|---|---|
| ❄️ Bola de Nieve | 1 | 10 |
| 🍬 Bastón de Caramelo | 10 | 110 |
| 🧨 Petardo | 50 | 600 |
| 🛷 Trineo | 200 | 2.600 |

- Vida inicial 100.000 **(a medir según cuántos jugadores haya; ajustable por admin)**.
- Al morir: premio a **todos los que tiraron**, según su daño (título «Derribé al Muñeco», Caja Navideña, Polvo; el
  top 10 de daño, algo extra). **No da Regalos** (para que tirar no se pague solo).
- Si muere antes del 24 **reaparece con ×1,5 de vida** («volvió más enojado»). Se cuenta cuántas veces murió y cuántas
  lo derribó cada uno.
- Golpe atómico en la base (`UPDATE ... SET hp = hp - x WHERE hp > 0 RETURNING`): el último golpe lo da uno solo.

## 🛒 Tienda navideña (1 → 6 de enero, sin comida)

| Ítem | Precio | Qué hace |
|---|---|---|
| 🔔 Campana | 3 | Reinicia el cooldown de `/hunt`. Se compra las veces que quieras (y de a varias: `aa buy campana 10`), queda en el inventario y se usa cuando quieras. NO reinicia `/travel` (el dueño: «muy op») |
| 🧦 Calcetín (Caja Navideña) | 50 | Caja con oro, Polvo, materiales de tus zonas y chance chica de huevo |
| 🕯️ Vela | 70 | +50 % Regalos por 1 hora (solo hasta el 24) |
| Título «Elfo del Fogón» | 500 | Cosmético, para siempre |
| Banner de nieve del perfil | 1.000 | Cosmético, para siempre |
| Título «Espíritu de la Navidad» | 1.500 | Cosmético, para siempre |
| 🦌 **Reno** | **4.500** | Mascota del evento (abajo) |

- Del 25 al 6: **vender Regalos por oro** a un precio bajo **(a medir)**, para que sobrar no sea perder todo.

## 🦌 El Reno

- Precio: 4.500 Regalos (decidido por el dueño; un jugador activo llega a mitad de mes).
- Bonus **fijo** desde el primer día, sin comer ni evolucionar:
  - **+15 % XP y +15 % oro**
  - **`/travel` baja de 30 a 25 minutos**
- Para siempre (sobrevive al FN y al fin del evento). Como todo bonus, no cuenta en PvP.

## Logros y títulos

- Logro «Espíritu Navideño» (Regalos juntados) y «Bombardero de Nieve» (daño al Muñeco), sin títulos de logro
  (los títulos del evento salen de la tienda y del Muñeco).

## Arte a pedir (fondo blanco liso, sin pixel art)

Regalo, Krampus, Muñeco de Nieve Gigante, Bola de Nieve, Bastón de Caramelo, Petardo, Trineo, Campana, Calcetín, Vela,
Reno, banner de nieve.

## Pendiente de decidir

- Los números **(a medir)**.
- Precio de venta de Regalos por oro (25 → 6).
- Premios exactos al derribar al Muñeco y del calendario día por día.

## Estado de la construcción (rama `feature/navidad`, carpeta `bot_ds_rpg-navidad`)

- **Parte 1 (hecha):** `GameData/Christmas.cs` (fechas, Regalos, calendario), `Database/add_christmas.sql` (tablas `event_gifts` por
  temporada —con `bells` y `candle_until`— y `event_advent_claims`; re-ejecutable; correr ANTES de abrir el evento),
  `Repositories/ChristmasRepository.cs`, `Services/ChristmasService.cs` (+ `IGameEvents.GrantEventGiftsAsync`, enganchado en las victorias de
  `/hunt`, `/autohunt`, `/travel`, jefe, jefe en equipo y `/daily`, con la línea «❄️ Navidad en el Fogón» al pie), `/navidad` y
  `aa navidad` (saldo + grilla del calendario + botón del día + 🛒 Tienda), `aa adviento`. Hunt: **50 %** (dueño).
- **Parte 2 (hecha):** `GameData/ChristmasShop.cs` y la tienda (`/navidad` → 🛒, `aa navidad tienda`, `aa navidad comprar <artículo> [n]`):
  Campana 3 (se acumulan; `aa campana` reinicia `/hunt` y no se gasta si ya estaba listo), Calcetín 50 (se abre al comprarlo), Vela 70
  (+50 % redondeado para arriba por 1 h; se apilan; solo hasta el 24), (la Bolsa de Polvo se sacó: «no me gusta»), títulos «Elfo del Fogón» 500 y «Espíritu de la
  Navidad» 1.500 (página 🎄 Navidad de `aa titles`, quedan en `player_stats`) y el **Reno 4.500** (`player_stats.xmas_reno`: +15 % XP y oro en
  `PlayerBonuses`, `/travel` 25 min en el cobro, `/cd` y los avisos). Calendario: 5 Campanas los días 11 y 18 y un Calcetín el 24.
- **Para probar antes:** `Christmas__Start`, `Christmas__FarmEnd`, `Christmas__End` (ver `.env.example`).
- **Krampus (hecho):** `/krampus` y `aa krampus` (solo del 1 al 24): pelea solo contra el élite de tu zona ×1,5 de vida y ×1,2 de daño, cooldown propio de 4 h
  ganes o pierdas, paga oro y EXP de un viaje + 30–40 Regalos, sin cajas (`KrampusRules` en `GameData/Christmas.cs`; multiplicadores «a medir»). `/navidad` muestra cuánto le falta.
  No tiene aviso de `/cd` ni recordatorio (el cooldown no está en `CooldownCatalog.All`).
- **Muñeco de Nieve Gigante (hecho):** `/munieco`, `aa muñeco` (panel) y `aa muñeco tirar petardo 5`; botón ☃️ en `/navidad`. Uno para todos los servidores
  (`event_snowman` por vuelta + `event_snowman_hits`), vida inicial `Christmas__SnowmanHp` (100.000 sin la variable), proyectiles Bola 1→10, Bastón 10→110,
  Petardo 50→600, Trineo 200→2.600. Al caer, en la misma transacción: todos 150 Regalos + Cofre de Oro + Polvo (daño/10, entre 10 y 500); top 10 +300 Regalos;
  top 3 + Cofre de Escoria (el dueño); contador `snowman_kill` → títulos «Rompenieves» (1) y «Azote del Muñeco» (3). Vuelve con ×1,5 de vida mientras caen Regalos.
  El panel se refresca solo cada 5 s durante 2 min y muestra las últimas 5 tiradas (en memoria). Anuncio público con el top 10 al caer.
- **Falta:** venta de Regalos por oro (25 → 6, precio a decidir); banner de nieve (cuando haya arte);
  logros; arte; `/info` y `docs/topgg/comandos`.
