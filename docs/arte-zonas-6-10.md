# Arte pendiente: Zonas 6 a 10 y la pesca (prompts para Bing Image Creator)

Cada imagen se sube al Developer Portal (la aplicación del bot → Emojis) con el **nombre de emoji** de la tabla. Cuando estén subidas, el bot las conecta: los ítems en `Database/update_item_emojis.sql` y las caras de los monstruos en `Database/update_monster_portraits.sql` (después se corre `Database/apply_emojis.sql` en Railway).

Consejos: pedí 4 variantes y elegí la que se lea mejor en chiquito (Discord la muestra a 22 px en el texto). Recortala cuadrada y, en los ítems, si podés sacale el fondo antes de subirla (los monstruos NO: llevan su escenario, no se lo saques).

## Monstruos (prioridad) — ✅ cargados el 2026-11-04

Las 10 caras ya están conectadas en `Database/update_monster_portraits.sql` con los nombres de emoji que subió el dueño (`guanaco_de_sal`, `coloso_de_sal`, `yaguarate_de_humo`, `Sombra_del_Monte`, `sapo_de_brea`, `bestia_del_pantano`, `lobo_de_hielo`, `gigante_de_hielo`, `condor_rayo`, `titan_de_la_tormenta`; no coinciden con la columna «Emoji» de abajo y no hace falta cambiarlos). Los prompts quedan acá por si hay que rehacer alguno: para reemplazar una cara se sube la nueva, se edita su línea en el script y se corre `apply_emojis.sql`.

Los monstruos llevan **fondo con su escenario**, como los de las zonas 1 a 5 (el volcán, la mina con el farol, el trigal): cada prompt trae el suyo al final, después de `background:`. Si la IA te devuelve el monstruo sin fondo, repetí el escenario y agregá `fully painted background, no white or empty areas`. La imagen sale apaisada (3:2) con el monstruo en el centro, para recortarla cuadrada.

Base común (va al principio de cada prompt):
`A single fantasy RPG monster portrait, wide 3:2 illustration with the monster centered so it can be cropped square, head and shoulders, facing the viewer, bold clean outlines, vibrant saturated colors, soft rim light, a detailed scenic background of its habitat filling the whole image (never a plain or empty background), no text, no frame,`

| Emoji | Monstruo | Prompt (después de la base) |
|---|---|---|
| `guanaco_sal` | Guanaco de Sal (Zona 6, cacería) | a fierce guanaco made of white salt crystals, crusty salt fur, glowing pale blue eyes, background: a vast white salt flat with shallow pink and pale blue mirror pools, distant Andean peaks and a pale cold sky |
| `coloso_sal` | Coloso de Sal (Zona 6, viaje) | a massive golem of stacked white and pink salt crystal blocks, cracked glowing seams, heavy brow, ancient and imposing, background: a glittering salt cavern with huge white and pink crystal formations and a soft pale blue light from above |
| `yaguarete_humo` | Yaguareté de Humo (Zona 7, cacería) | a jaguar made of dark grey swirling smoke, spotted pattern drawn in embers, burning orange eyes, wisps of smoke trailing, background: a dark rocky volcanic valley with glowing lava cracks, jagged black cliffs and drifting smoke |
| `sombra_monte` | Sombra del Monte (Zona 7, viaje) | a tall shadow creature from a burning forest, hooded silhouette of black smoke, branches like antlers, two glowing ember eyes, background: a forest on fire at night with charred black trunks, falling embers and an orange glow between the trees |
| `sapo_brea` | Sapo de Brea (Zona 8, cacería) | a huge toad covered in glossy black tar, dripping tar, yellow bulging eyes, bubbles of tar on its back, background: a field of tar pits with bubbling black tar, dead twisted trees and a dim yellow-green sky |
| `bestia_pantano` | Bestia del Pantano (Zona 8, viaje) | a hulking swamp beast of mud, roots and black tar, moss hanging from its shoulders, sickly green glowing eyes, background: a dark swamp at dusk with tangled roots, moss-covered dead trees, black tar pools and thick sickly green fog |
| `lobo_hielo` | Lobo de Hielo (Zona 9, cacería) | a wolf with fur made of ice shards, frost breath, pale cyan glowing eyes, icicles hanging from its jaw, background: a frozen tundra at dusk with snowdrifts, jagged ice spikes and a pale blue sky with a faint aurora |
| `gigante_escarcha` | Gigante Escarchado (Zona 9, viaje) | a frost giant with blue skin covered in hoarfrost, beard of icicles, cracked ice armor, cold white glowing eyes, background: a glacier canyon with towering blue ice walls, falling snow and a cold blue light |
| `condor_rayo` | Cóndor del Rayo (Zona 10, cacería) | an andean condor with storm-dark feathers, lightning crackling along its wings, electric yellow eyes, background: a stormy mountain sky with dark thunderclouds and lightning bolts striking behind jagged peaks |
| `titan_tormenta` | Titán de Tormenta (Zona 10, viaje) | a colossal storm titan made of dark thunderclouds, body veined with lightning, crown of electricity, glowing white eyes, background: a barren rocky plain under a huge thunderstorm with swirling dark clouds and lightning striking the ground in the distance |

## Drops (uno por zona) — ✅ cargados el 2026-11-04

Los 5 están conectados en `Database/update_item_emojis.sql` con los nombres que subió el dueño: `drop_sal` (Cristal de Sal), `colmillo_humo`, `gota_brea`, `escama_hielo1` (Escama de Hielo) y `pluma_rayo`. Los prompts quedan por si hay que rehacer alguno.

Base:
`A single game item icon for a fantasy RPG, centered, bold clean outlines, vibrant colors, soft glow, transparent background, square, no text,`

| Emoji | Ítem | Prompt |
|---|---|---|
| `cristal_sal` | Cristal de Sal | a jagged cluster of white and pale pink salt crystals, sparkling |
| `colmillo_humo` | Colmillo de Humo | a large curved feline fang, smoky grey, wisps of smoke curling around it, ember glow at the tip |
| `gota_brea` | Gota de Brea | a big glossy drop of black tar with an oily rainbow sheen |
| `escama_hielo` | Escama de Hielo | a single translucent ice-blue scale, frosted edges, cold sparkle |
| `pluma_rayo` | Pluma de Rayo | a dark storm-grey feather crackling with yellow lightning |

## Reliquias (la llave de cada mazmorra)

Misma base que los drops. Las cinco comparten forma (un medallón antiguo de piedra con una llama en el centro) y cambian el elemento, para que se reconozcan como una familia.

| Emoji | Ítem | Prompt |
|---|---|---|
| `reliquia_sal` | Reliquia de Sal | an ancient round stone relic medallion with a small flame in the center, encrusted with white salt crystals, runes glowing pale blue |
| `reliquia_humo` | Reliquia de Humo | an ancient round stone relic medallion with a small flame in the center, wrapped in dark smoke, runes glowing orange |
| `reliquia_brea` | Reliquia de Brea | an ancient round stone relic medallion with a small flame in the center, dripping black tar, runes glowing sickly green |
| `reliquia_hielo` | Reliquia de Hielo | an ancient round stone relic medallion with a small flame in the center, covered in frost and icicles, runes glowing cyan |
| `reliquia_rayo` | Reliquia de Rayo | an ancient round stone relic medallion with a small flame in the center, crackling with lightning, runes glowing electric yellow |

## Equipo (25 piezas: 4 armas de clase y 1 amuleto por zona)

Misma base que los drops. Se arma el prompt con **la pieza + el tema de la zona**:

| Pieza | Texto |
|---|---|
| Espada | a fantasy longsword |
| Dagas | a pair of crossed fantasy daggers |
| Arco | a fantasy recurve bow |
| Grimorio | a fantasy spellbook grimoire with an ornate cover |
| Amuleto | a fantasy amulet pendant on a chain |

| Zona | Sufijo del emoji | Tema |
|---|---|---|
| 6 | `_sal` | made of white salt crystal and pale stone, sparkling pink and white highlights |
| 7 | `_humo` | made of blackened wood and dark iron, wreathed in grey smoke, glowing ember accents |
| 8 | `_brea` | made of black obsidian coated in glossy dripping tar, sickly green glow |
| 9 | `_hielo` | made of translucent blue ice and frosted silver, icicles, cold cyan glow |
| 10 | `_rayo` | made of dark storm-forged steel, crackling with yellow lightning, electric glow |

Ejemplo: **Espada de Sal**, emoji `espada_sal`:
`A single game item icon for a fantasy RPG, centered, bold clean outlines, vibrant colors, soft glow, transparent background, square, no text, a fantasy longsword made of white salt crystal and pale stone, sparkling pink and white highlights`

Nombres de emoji: `espada_sal`, `dagas_sal`, `arco_sal`, `grimorio_sal`, `amuleto_sal`, y lo mismo con `_humo`, `_brea`, `_hielo`, `_rayo`. Cada zona va un poco más brillante que la anterior: si querés que se note la escalera, sumale a las de la 9 y la 10 `legendary, intricate details`.

## Pesca (v0.48.0): 3 peces y 2 cañas

Hoy salen con un emoji común de relleno (🐟 🐠 👑 los peces, 🎣 las cañas) y andan bien así. Cuando se suba el arte: se agregan los `UPDATE` por nombre de ítem en `Database/update_item_emojis.sql`, se corre `Database/apply_emojis.sql` en Railway y se sacan los cinco nombres de la lista `provisional` de `tests/portraittest/Program.cs`.

Estas cinco usan el estilo que prefiere el dueño desde el 2026-10-28 (ilustración limpia, **no pixel art**, **fondo blanco liso** para recortarlo después; las bases de arriba piden fondo transparente: si Bing lo dibuja raro, usá esta). Subirlas a 128×128 con el **nombre de emoji** de la tabla.

Base:
`A single game item icon for a fantasy RPG, clean digital illustration (not pixel art), soft rounded edges, bold clean outlines, vibrant saturated colors, plain solid white background (#FFFFFF), no shadow, no gradient, centered and large, square, no text,`

| Emoji | Ítem | Prompt (después de la base) |
|---|---|---|
| `mojarra` | Mojarra (Común) | a small slim silvery river fish, side view facing right, shiny silver-blue scales with a darker blue-grey back, round black eye, small transparent fins, simple and friendly, no glow |
| `dorado` | Dorado (Raro) | a powerful golden river fish, side view facing right, shiny golden-orange scales, strong jaw, red-orange tail and fins, body slightly arched as if leaping out of the water, a few small blue sparkles around it |
| `surubi` | Surubí (Épico) | a big river catfish, long flat head with a wide mouth and long whiskers, cream belly and a grey-brown back with bold dark tiger stripes and spots, side view facing right, a few small violet sparkles around it |
| `cana_hierro` | Caña de Hierro Forjado (Zona 3, ×2) | a fishing rod placed diagonally from bottom-left to top-right, brown wooden handle with visible grain, blued forged steel reel and line guides, a thin line with a small steel hook hanging from the tip, same blue steel and same wood as the iron axe and pickaxe |
| `cana_escoria` | Caña de Escoria Templada (Zona 5, ×3) | a fishing rod placed diagonally from bottom-left to top-right, rod made of black-violet volcanic slag like obsidian with glowing orange lava cracks, scorched dark wood handle, a glowing orange reel, a few small embers around it, same materials as the slag axe and pickaxe, clearly more powerful than the iron rod |

La rareza se lee en un detalle chico, como en la madera: los Raros con chispas azules y los Épicos con chispas violetas; el Común va sin brillo. Que las dos cañas compartan forma y que la de la Zona 5 se vea más poderosa sin dejar de ser la misma herramienta.

Las hachas y los picos del Carpintero tienen sus prompts en `docs/tool-emojis/README.md` (están pedidos en pixel art; si el dueño los quiere en este estilo, se cambia la base).
