# Arte de las Zonas 6 a 10 (prompts para Bing Image Creator)

Cada imagen se sube al Developer Portal (la aplicación del bot → Emojis) con el **nombre de emoji** de la tabla. Cuando estén subidas, el bot las conecta: los ítems en `Database/update_item_emojis.sql` y las caras de los monstruos en `Database/update_monster_portraits.sql` (después se corre `Database/apply_emojis.sql` en Railway).

Consejos: pedí 4 variantes y elegí la que se lea mejor en chiquito (Discord la muestra a 22 px en el texto). Recortala cuadrada y, si podés, sacale el fondo antes de subirla.

## Monstruos (prioridad)

Base común (va al principio de cada prompt):
`A single fantasy RPG monster portrait icon, centered, head and shoulders, facing the viewer, bold clean outlines, vibrant saturated colors, soft rim light, simple dark vignette background, square, game asset, no text, no frame,`

| Emoji | Monstruo | Prompt (después de la base) |
|---|---|---|
| `guanaco_sal` | Guanaco de Sal (Zona 6, cacería) | a fierce guanaco made of white salt crystals, crusty salt fur, glowing pale blue eyes, salt flats sparkle around it |
| `coloso_sal` | Coloso de Sal (Zona 6, viaje) | a massive golem of stacked white and pink salt crystal blocks, cracked glowing seams, heavy brow, ancient and imposing |
| `yaguarete_humo` | Yaguareté de Humo (Zona 7, cacería) | a jaguar made of dark grey swirling smoke, spotted pattern drawn in embers, burning orange eyes, wisps of smoke trailing |
| `sombra_monte` | Sombra del Monte (Zona 7, viaje) | a tall shadow creature from a burning forest, hooded silhouette of black smoke, branches like antlers, two glowing ember eyes |
| `sapo_brea` | Sapo de Brea (Zona 8, cacería) | a huge toad covered in glossy black tar, dripping tar, yellow bulging eyes, bubbles of tar on its back |
| `bestia_pantano` | Bestia del Pantano (Zona 8, viaje) | a hulking swamp beast of mud, roots and black tar, moss hanging from its shoulders, sickly green glowing eyes |
| `lobo_hielo` | Lobo de Hielo (Zona 9, cacería) | a wolf with fur made of ice shards, frost breath, pale cyan glowing eyes, icicles hanging from its jaw |
| `gigante_escarcha` | Gigante Escarchado (Zona 9, viaje) | a frost giant with blue skin covered in hoarfrost, beard of icicles, cracked ice armor, cold white glowing eyes |
| `condor_rayo` | Cóndor del Rayo (Zona 10, cacería) | an andean condor with storm-dark feathers, lightning crackling along its wings, electric yellow eyes, storm clouds behind |
| `titan_tormenta` | Titán de Tormenta (Zona 10, viaje) | a colossal storm titan made of dark thunderclouds, body veined with lightning, crown of electricity, glowing white eyes |

## Drops (uno por zona)

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
