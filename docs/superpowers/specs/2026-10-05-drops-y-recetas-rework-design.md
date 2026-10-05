# Rework de drops, jefes y recetas — diseño (2026-10-05)

Estado: **diseño aprobado en la charla con el dueño; falta que revise este documento** antes de armar el plan y tocar código o base.
Orden acordado: primero este rework, **después** el deploy en Railway (el repo ya está listo para desplegar en `main`/v0.6.0).

## 1. Por qué

Había demasiadas cosas que seguir: 4 drops por zona (2–3 de hunt, 1 de travel, 1 del jefe) y 26 de las 35 recetas pedían 4 o 5 tipos de
ingrediente distintos (Zona 4 y 5 casi todas 5). El jefe soltaba un material que entraba en una sola receta por zona (1 unidad). Además, en
Zona 1 la general (Hoja de Acero Puro, +15) pegaba más que cualquier arma de clase (+5), y las generales tenían familia de arma
(la Hoja es de Espadas), así que el Guerrero se llevaba el boost de clase gratis.

## 2. Decisiones del dueño

1. **20 mobs**: 4 por zona = 2 de hunt (cada uno con su drop) + 1 de travel (con su drop) + 1 jefe. **Sale el Perro Cimarrón** (Zona 1). No hace falta ningún mob nuevo.
2. **3 drops de material por zona (15 en total)**: 2 de hunt ("a granel") y 1 de travel ("escaso"). Las chances no se tocan (hunt 6%, travel 20%): con 2 mobs en el pool cada drop de hunt sigue saliendo 3% por `/hunt`.
3. **El jefe no suelta material: da un cofre de su zona** — 100% la primera vez que cae y 40% en las siguientes (solitario y raid).
4. **6 recetas por zona (30 en total)**: 4 armas de clase + 1 general + **1 amuleto**. Cada jugador ve 3 por zona: la general, la de su clase y el amuleto.
5. **Armas**: la general es *accesible* y tiene el MISMO ATQ base que las de clase, pero **sin familia** (no recibe el boost de clase ×1,6); la de clase es *más complicada* y con el boost de su clase queda más fuerte. La general es para arrancar.
6. **Amuleto único por zona**, y es la receta más pesada: usa **los 3 drops de la zona + 1 material de recolección**.
7. No quiere que sea fácil: la dificultad se marca por tipo de pieza (más tipos de ingrediente y más tiempo = más potente), con un máximo de 4 tipos por receta.
8. **Todo lo actual de la base es reemplazable** (el dueño ya avisó a sus amigos): sin reembolsos ni conversiones. Los materiales se pierden al forjar (se consumen) y vender un arma solo devuelve el oro de venta.

## 3. Mobs, drops y cofres resultantes

| Zona | Hunt A | Hunt B | Travel | Jefe |
|---|---|---|---|---|
| 1 Praderas del Mate | Jabalí Rabioso → Colmillo de Jabalí | Ñandú Salvaje → Pluma de Ñandú | Toro Bravo → Cuero Grueso | Rey Jabalí → Cajón de Pino |
| 2 Bosque de Cenizas | Puma de las Cenizas → Garra de Puma Cenizo | Espíritu del Monte → Esencia Espectral | Ciervo Sagrado → Ceniza Bendita | Lobisón Alfa → Baúl de Roble |
| 3 Minas del Yunque | Gólem del Yunque → Yunque Fragmentado | Excavador Profundo → Gema en Bruto | Mole de Escoria → Escoria Metálica Densa | Capataz de Hierro → Arcón de Hierro |
| 4 Cordillera del Fuego | Salamandra Infernal → Escama Ígnea | Coloso de Magma → Núcleo de Magma | Dragón de Lava → Aliento de Fuego Eterno | Señor del Volcán → Cofre de Oro |
| 5 Cráter de la Escoria | Titán de Escoria → Corazón de Titán | Devorador de Almas → Fragmento de Alma | Quimera del Abismo → Ceniza del Abismo | Soberano de la Escoria → Cofre de Oro |

- En recetas, el drop de **Hunt A es el "físico"** (lo usan Guerrero y Ninja) y el de **Hunt B el "arcano"** (Arquero y Hechicero); el de travel lo piden todas las armas de clase y el amuleto. Así cada clase tiene su ruta de farmeo y los dos drops de hunt de una zona se pueden intercambiar entre jugadores más adelante (idea del "socio").
- **Cofres**: la escalera ya existente de `MissionRewards.BoxLadder` (Cajón de Pino, Baúl de Roble, Arcón de Hierro, Cofre de Oro; la Zona 5 usa el de la 4). El Arca del Soberano sigue siendo solo de logros.
- **Mecanismo**: el drop del jefe en `monster_drops` pasa a ser el cofre de su zona (un `Caja` entra por la misma ruta que cualquier drop: `ApplyBossVictoryAsync`). Lo que cambia es la chance: hoy `BossDropChancePercent` es 15% fijo; pasa a "100% si es la primera vez que cae ese jefe para ese jugador, 40% después". La primera vez ya se calcula hoy para el texto del jefe (`highest_zone_cleared` leído *antes* de aplicar la victoria); en el raid se evalúa por participante.
- Regla vigente de las cajas: no dan los drops de recetas, así que el jefe deja de dar progreso de drops; da oro, recolección, trofeos y comida. Hay que re-correr `Database/report_box_economy.sql` por el aporte extra de cofres del jefe.

## 4. Recetas nuevas (30)

Reglas:
- **ATQ base por zona: 9 / 20 / 32 / 50 / 80** (el de clase con boost ×1,6 queda ~14 / 32 / 51 / 80 / 128). La Zona 1 pasa a +9 para que, con el boost, quede ~+14: lo que calibró la escalera (se entra a la Zona 2 con +15).
- **DEF del amuleto único: 10 / 18 / 30 / 46 / 75** — los amuletos "bajos" con los que se calibró la escalera de zonas. Con los "altos" el jefe bajaría de ~11% a ~3% de derrota y dejaría de ser el examen.
- **General** = 1 recolección + el drop físico + el drop arcano (3 tipos). **De clase** = 2 recolecciones + el drop de su ruta (físico o arcano) + el de travel (4 tipos). **Amuleto** = 1 recolección + los 3 drops (4 tipos).
- Guerrero y Ninja: Hierro más otra recolección (Guerrero madera, Ninja carbón/oro/piedra). Arquero y Hechicero: madera más otra recolección.
- **Min** = minutos de juego continuo del modelo de `Database/report_recipe_pacing.sql` (el ingrediente más lento; hunt 1/min, travel 1 cada 10, `/chop` y `/mine` 1 cada 5). Objetivo: **general ~100** (Zona 1 ~50), **de clase ~150** (Zona 1 ~100), **amuleto ~200** (Zona 1 ~130). Hoy: generales 143–190, de clase 143–286 (Zonas 2 a 4), amuletos 167–286.
- ⚠️ Las 4 armas de clase de la Zona 5 siguen pidiendo 1 material **mítico** (Meteorito o Corteza, ~1000 min de recolección): es la meta larga de la run 1 y el reset la acorta. Ver "Decisiones abiertas".

**Zona 1 · Praderas del Mate (nivel 1)**

| Receta | Para | Stat | Oro | Recolección | Drops | T | Min |
|---|---|---|---|---|---|---|---|
| Hoja de Acero Puro | General | +9 ATQ | 40 | 2× Hierro | 1× Colmillo de Jabalí, 1× Pluma de Ñandú | 3 | 48 |
| Espada de Madera | Guerrero | +9 ATQ | 100 | 12× Madera de Pino, 4× Hierro | 2× Colmillo de Jabalí, 1× Cuero Grueso | 4 | 95 |
| Daga Oxidada | Ninja | +9 ATQ | 100 | 4× Hierro, 10× Piedra | 2× Colmillo de Jabalí, 1× Cuero Grueso | 4 | 95 |
| Arco Corto de Sauce | Arquero | +9 ATQ | 100 | 15× Madera de Pino, 2× Hierro | 3× Pluma de Ñandú, 1× Cuero Grueso | 4 | 100 |
| Grimorio Desgastado | Hechicero | +9 ATQ | 100 | 12× Madera de Pino, 8× Piedra | 3× Pluma de Ñandú, 1× Cuero Grueso | 4 | 100 |
| Hombreras de Cuero Grueso | Amuleto | +10 DEF | 200 | 25× Piedra | 4× Colmillo de Jabalí, 4× Pluma de Ñandú, 2× Cuero Grueso | 4 | 133 |

**Zona 2 · Bosque de Cenizas (nivel 5)**

| Receta | Para | Stat | Oro | Recolección | Drops | T | Min |
|---|---|---|---|---|---|---|---|
| Cuchilla de Cenizas | General | +20 ATQ | 300 | 4× Carbón | 3× Garra de Puma Cenizo, 3× Esencia Espectral | 3 | 100 |
| Hacha de Hierro MK3 | Guerrero | +20 ATQ | 400 | 6× Hierro, 6× Madera de Roble | 4× Garra de Puma Cenizo, 3× Ceniza Bendita | 4 | 150 |
| Colmillo Nocturno | Ninja | +20 ATQ | 400 | 6× Hierro, 3× Carbón | 4× Garra de Puma Cenizo, 3× Ceniza Bendita | 4 | 150 |
| Arco Élfico Ancestral | Arquero | +20 ATQ | 400 | 12× Madera de Roble, 3× Carbón | 4× Esencia Espectral, 3× Ceniza Bendita | 4 | 150 |
| Grimorio de las Tormentas | Hechicero | +20 ATQ | 400 | 12× Madera de Roble, 2× Oro Puro | 4× Esencia Espectral, 3× Ceniza Bendita | 4 | 150 |
| Talismán de Ceniza Bendita | Amuleto | +18 DEF | 400 | 6× Oro Puro | 4× Garra de Puma Cenizo, 4× Esencia Espectral, 4× Ceniza Bendita | 4 | 214 |

**Zona 3 · Minas del Yunque (nivel 10)**

| Receta | Para | Stat | Oro | Recolección | Drops | T | Min |
|---|---|---|---|---|---|---|---|
| Pico de Minero Reforzado | General | +32 ATQ | 450 | 4× Hierro | 3× Yunque Fragmentado, 3× Gema en Bruto | 3 | 100 |
| Mazo de Escoria | Guerrero | +32 ATQ | 600 | 6× Hierro, 3× Madera de Nogal | 4× Yunque Fragmentado, 3× Escoria Metálica Densa | 4 | 150 |
| Dagas de Garra Maldita | Ninja | +32 ATQ | 600 | 6× Hierro, 3× Oro Puro | 4× Yunque Fragmentado, 3× Escoria Metálica Densa | 4 | 150 |
| Boleadoras de Escoria | Arquero | +32 ATQ | 600 | 4× Madera de Nogal, 3× Hierro | 4× Gema en Bruto, 3× Escoria Metálica Densa | 4 | 150 |
| Báculo de Tizón | Hechicero | +32 ATQ | 600 | 4× Madera de Nogal, 3× Oro Puro | 4× Gema en Bruto, 3× Escoria Metálica Densa | 4 | 150 |
| Peto de Escoria Templada | Amuleto | +30 DEF | 650 | 6× Oro Puro | 4× Yunque Fragmentado, 4× Gema en Bruto, 4× Escoria Metálica Densa | 4 | 214 |

**Zona 4 · Cordillera del Fuego (nivel 15)**

| Receta | Para | Stat | Oro | Recolección | Drops | T | Min |
|---|---|---|---|---|---|---|---|
| Lanza de Magma | General | +50 ATQ | 700 | 4× Hierro | 3× Escama Ígnea, 3× Núcleo de Magma | 3 | 100 |
| Facón de Hueso Añejo | Guerrero | +50 ATQ | 900 | 6× Hierro, 1× Gema de Zafiro | 4× Escama Ígnea, 3× Aliento de Fuego Eterno | 4 | 150 |
| Cuchillos de Ceniza | Ninja | +50 ATQ | 900 | 6× Hierro, 3× Oro Puro | 4× Escama Ígnea, 3× Aliento de Fuego Eterno | 4 | 150 |
| Arco de Caza Mayor | Arquero | +50 ATQ | 900 | 1× Madera de Ébano, 3× Madera de Nogal | 4× Núcleo de Magma, 3× Aliento de Fuego Eterno | 4 | 150 |
| Códice de las Brasas | Hechicero | +50 ATQ | 900 | 1× Gema de Zafiro, 3× Madera de Nogal | 4× Núcleo de Magma, 3× Aliento de Fuego Eterno | 4 | 150 |
| Talismán del Volcán | Amuleto | +46 DEF | 1000 | 1× Gema de Zafiro | 4× Escama Ígnea, 4× Núcleo de Magma, 4× Aliento de Fuego Eterno | 4 | 200 |

**Zona 5 · Cráter de la Escoria (nivel 20)**

| Receta | Para | Stat | Oro | Recolección | Drops | T | Min |
|---|---|---|---|---|---|---|---|
| Martillo del Titán | General | +80 ATQ | 1100 | 4× Hierro | 3× Corazón de Titán, 3× Fragmento de Alma | 3 | 100 |
| Espada del Abismo | Guerrero | +80 ATQ | 1400 | 1× Fragmento de Meteorito, 6× Hierro | 4× Corazón de Titán, 3× Ceniza del Abismo | 4 | 1000 ⚠️ |
| Colmillo del Cráter | Ninja | +80 ATQ | 1400 | 1× Fragmento de Meteorito, 6× Hierro | 4× Corazón de Titán, 3× Ceniza del Abismo | 4 | 1000 ⚠️ |
| Arco del Alma Errante | Arquero | +80 ATQ | 1400 | 1× Corteza del Árbol de Vida, 1× Madera de Ébano | 4× Fragmento de Alma, 3× Ceniza del Abismo | 4 | 1000 ⚠️ |
| Báculo del Árbol de Vida | Hechicero | +80 ATQ | 1400 | 1× Corteza del Árbol de Vida, 1× Madera de Ébano | 4× Fragmento de Alma, 3× Ceniza del Abismo | 4 | 1000 ⚠️ |
| Corazón de Titán Engarzado | Amuleto | +75 DEF | 1500 | 1× Gema de Zafiro | 4× Corazón de Titán, 4× Fragmento de Alma, 4× Ceniza del Abismo | 4 | 200 |


## 5. Qué sale y qué cambia en el catálogo

- **Salen**: el mob Perro Cimarrón; los ítems Colmillo de Cimarrón, Colmillo del Rey Jabalí, Pelaje Plateado del Alfa, Yunque del Capataz, Brasa Eterna y Corazón del Soberano; los amuletos Amuleto del Levantador, Mate Tallado en Cenizas, Casco de Capataz, Coraza de Escamas Ígneas y Égida del Devorador (sus emojis quedan libres para zonas nuevas); y 5 recetas (35 → 30).
- **Quedan los amuletos "grandes"** (Hombreras de Cuero Grueso, Talismán de Ceniza Bendita, Peto de Escoria Templada, Talismán del Volcán, Corazón de Titán Engarzado), con el DEF "bajo" de arriba.
- **Cambian**: ATQ base de las 25 armas, los 5 generales pasan a `weapon_family = NULL`, el oro de cada receta y los ingredientes de todas.
- `AchievementCatalog.TrophyTotal` (17) **no cambia**: ninguno de los ítems que salen es un trofeo.
- Los jugadores actuales pierden lo retirado sin reembolso, por decisión del dueño: 2 usan Amuleto del Levantador (se desequipa), y hay 6 unidades sueltas de drops que salen. Se hace respaldo antes.

## 6. Código y datos que se tocan (para el plan)

- Datos: `finalize_monster_roster.sql` (Zona 1 con 2 hunts; el drop del jefe pasa a ser un cofre), `seed_zone_bosses.sql`, `seed_travel_monsters.sql`, `seed_recipes.sql` y `seed_zone2..5_gear_and_recipes.sql` (recetas y stats nuevos), `update_item_emojis.sql` (sin las líneas de los amuletos que salen), `seed_zones_and_monsters.sql` si nombra al Perro Cimarrón, y un script de migración para la base viva (con respaldo, desequipa y borra lo que sale, sin reembolso).
- Código: `CombatRewardCalculator` (la chance del jefe: primera vez 100%, después 40%) y su uso en `AdventureModule` y `RaidModule`; `DropsCatalog` (`/drops` ya no lista drop del jefe como material y sí el cofre); `FarmAdvisor` y `FarmAdviceService` (la fuente "jefe" de un ingrediente ya no existe); `GameData/RecipeCatalog`, `ForgeChoices`, `ItemStatLabel` (el general ya no muestra boost de clase) y el texto de las recetas (`RenderRecipesEmbed`).
- Pruebas (scratchpad): `choicetest`, `stage6`, `stage7`, `stage13` (recetas por zona y clase), pruebas de jefe y raid (cofre), `abilitytest` sin cambios.
- Docs: `CLAUDE.md` (Zones, Drops, Forge recipes, Boxes), `MEJORAS.md`, `DEPLOY.md` si hace falta.

## 7. Verificación

1. `dotnet build` sin errores ni advertencias y la regresión de las pruebas de juego.
2. `report_recipe_pacing.sql` (`-v ph=0.06 -v pt=0.20`): los minutos de la base real tienen que coincidir con los de las tablas de arriba.
3. Instalación limpia (`run_fresh_install.sql`) contra una base de práctica: idéntica a la real (el mismo resumen de estructura y catálogo usado el 2026-10-05).
4. La escalera: los monstruos no se tocan salvo el Perro Cimarrón; se revisa con el simulador `ladder` que la Zona 1 (arma de clase ~+14) siga entrando a la Zona 2 como está calibrado, y `report_box_economy.sql` por los cofres del jefe.
5. Prueba del jefe: primera vez siempre cofre; repetición ~40% (muestra grande); raid por participante.

## 8. Decisiones abiertas (con recomendación)

1. **Zona 5, material mítico en las armas de clase.** Hoy y en este diseño piden 1 Meteorito o Corteza (≈1000 min de recolección, es la meta larga de la run 1; el reset la acorta). *Recomiendo dejarlo así*; la alternativa es pedir 1 Zafiro y reservar los míticos para el reset.
2. **Zona 5, cofre del jefe**: Cofre de Oro como la Zona 4 (recomendado, no se regala nada del Arca) o 2 cofres.
3. **Nombres de los amuletos que quedan**: dejo los "grandes"; es solo el nombre y su emoji (hay 2 jugadores usando el Amuleto del Levantador, que se retira).
4. **Oro**: la general de la Zona 1 cuesta 40 (se puede forjar el primer día con el oro inicial) y las demás zonas con los valores actuales de cada pieza.

## 9. Fuera de alcance (después de este rework y del deploy)

Caras de los enemigos (una imagen por mob, 20 en total, subida como emoji de la aplicación y usada de miniatura en el combate); el sistema de socio / marriage (intercambio de drops de hunt dentro de la misma zona, entre otras cosas); emojis que faltan (armas de Zonas 4 y 5 y las 3 que hoy no se consiguen; los 5 amuletos que salen dejan libres sus emojis).
