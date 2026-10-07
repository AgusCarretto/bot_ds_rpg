# Cofres parejos (v0.14.0) — diseño

Pedido del dueño (2026-10-07): revisar la economía de cofres y de la Zona 0 «pensando en los reset», «emparejar cosas o ver si hay algo muy over power» y aplicar los cambios que propuse. Esto es el resultado de medir con `Database/report_box_economy.sql` contra la base real (números en oro equivalente de farmeo).

## Lo que se midió

| Caja | Precio | Valor | Precio / valor |
|---|---|---|---|
| Cajón de Pino (Z1) | 2.300 | ~0,75k | 306 % |
| Baúl de Roble (Z2) | 14.000 | ~8,2k | 170 % |
| Arcón de Hierro (Z3) | 42.000 | ~43k | 97 % |
| Cofre de Oro (Z4) | 120.000 | ~211k | 57 % |
| Arca del Soberano (Mítica, solo logros) | — | ~594k | — |

Una sesión de 2 h de farmeo perfecto (cacería + viaje) vale ~3,4k / 14k / 28k / 52k / 90k en las zonas 1 a 5. Las cajas **gratis** (misión diaria, jefe, semanal) seguían la escalera completa: en la Zona 4 la caja diaria valía 4 sesiones y el jefe (40 % de un Cofre de Oro por victoria) otra más. En la Zona 1 y 2 eran casi nada. La escalera de cajas se cortaba en la Zona 4: la 5 y la 0 no tenían cofre propio, y el Asador (el combate más duro) pagaba lo mismo que el jefe de la 5 pero sin cofre, sin drops y sin huevo.

## Qué cambia

1. **Cada zona tiene su tabla de cajas, en la base** (`zone_boxes`, una fila por zona y rol): `repeat` (el cofre de cada victoria repetida sobre el jefe, con su chance), `daily` y `weekly` (el premio por completar las misiones del período), `prize` y `prize_top` (los tramos de logros y el campeón de la Arena, igual que antes). Una zona nueva necesita sus filas y nada más; el arranque avisa en el log si a una zona le falta alguna.
2. **Jefe**: la primera vez de cada vuelta sigue siendo el cofre de la zona al 100 %. Las repeticiones ya no son «40 % del cofre grande»: dan **una caja más chica casi siempre** (Z1 Pino, Z2 Roble, Z3 Roble, Z4 Arcón 60 %, Z5 Arcón, Z0 Arcón). El Cofre de Oro deja de caer por repetir el jefe: cuesta menos valor (≈ −50 %) y se ve un cofre más seguido.
3. **Misiones**: diaria Z1 Pino · Z2 Roble · Z3 Roble · Z4 Arcón · Z5 Arcón (antes hasta Cofre de Oro por día); semanal Z1 Roble · Z2 Arcón · Z3 Arcón · Z4 Cofre · Z5 Cofre (solo cambia la Z3: antes Cofre de Oro).
4. **Cofre propio para la Zona 5 y para la Zona 0**: *Cofre de Escoria* (jefe de la Z5, primera vez por vuelta; 35-65 ítems, con drops de las 5 zonas y los trofeos de la Z5; ~2× el valor del Cofre de Oro) y *Brasero del Fogón* (el Asador, primera vez por vuelta). Los dos son solo premio (no se compran ni se venden).
5. **Zona 0 con premio que sobrevive al reset**: el Fuego Nuevo borra nivel, XP, materiales y cajas, así que casi todo lo que daba el Asador se perdía. Ahora: **oro** (bonus 3.000 en vez de 1.500: ~46k por victoria), **poca XP** (bonus 300 en vez de 1.500), el **Brasero** (40-80k de oro seguro y mucha Comida para Mascotas) y **una 6.ª mascota**, la Chispa del Asador (+10 % de cantidad en `/chop` y `/mine` al máximo), que llega con un huevo la primera vez.
6. **Script de economía** (`Database/report_economy.sql`): oro equivalente por hora de cada fuente por zona, para correrlo al agregar contenido.

## Fuera de esta versión

El nivel de las zonas (ver la conversación: subir el mínimo a ~10 niveles por zona y recalibrar HP/daño de los monstruos) es un cambio de calibración entero y se decide aparte.
