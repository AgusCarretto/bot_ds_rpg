# Comandos para top.gg

**Para cargarlos en top.gg** (panel del bot → *Commands* → *Import from Discord*) se pega el contenido de
[comandos.json](comandos.json): el importador solo acepta el JSON de comandos de Discord, no esta tabla. Ese JSON se
arma desde el código con `dotnet run --project tools/slashjson -- docs/topgg/comandos.json` (no necesita token ni
conectarse a Discord); cuando se agregue o cambie un comando, se vuelve a generar y se reimporta.

Esta tabla es la versión para leer (2026-10-31, v0.41.2).
Los comandos de administrador (`aa admingive`, `aa admincode`) NO van: no son para los jugadores.

Todo comando también se puede escribir con el prefijo `aa` (por ejemplo `aa hunt`, `aa daily`).

## Primeros pasos
| Comando | Descripción |
|---|---|
| `/start` | Empezá tu aventura y elegí tu clase. |
| `/tutorial` | Aprendé lo básico: recolectar, forjar, pelear y sobrevivir. |
| `/info` | La lista de comandos, o cómo funciona un sistema (`/info tema:enchant`). |
| `/profile` | Tu nivel, vida, ataque, defensa y oro (o los de otro jugador). |
| `/stats` | Tu ataque, defensa y vida y cuánto aporta cada cosa (equipo, platos, dragón...); con `jugador:` mirás las de otra persona. |
| `/bestiary` | Tu bestiario: los monstruos de cada zona que ya venciste, con su vida, daño y drop (los que te faltan, como ???). |
| `/inventory` | Tus materiales, comida y cajas, por pestañas. |
| `/language` | Cambiá el idioma: español o English. |
| `/config` | Ajustá cómo ves el bot. |

## Combate
| Comando | Descripción |
|---|---|
| `/hunt` | Salí a cazar monstruos de tu zona. |
| `/autohunt` | Una cacería completa de una vez, sin botones. |
| `/travel` | Enfrentá al élite de tu zona: más difícil, mucho más premio. |
| `/boss` | Enfrentá al jefe de tu zona, solo o con hasta 5 amigos. |
| `/zona` | Viajá a otra zona del mundo. |
| `/zonas` | Todas las zonas y el nivel que piden. |
| `/drops` | Qué suelta cada monstruo y con qué chance. |
| `/heal` | Curate toda la vida comiendo lo justo de tu mochila. |
| `/use` | Usá una comida, también en plena pelea. |

## Recolección y equipo
| Comando | Descripción |
|---|---|
| `/chop` | Talá madera. |
| `/mine` | Miná minerales. |
| `/fish` | Pescá en el río. |
| `/cook` | Cociná platos con tus peces y materiales (sin plato, ves las recetas). |
| `/professions` | Tus oficios (Leñador, Minero, Pescador, Encantador) y lo que dan. |
| `/forge` | La herrería: forjá armas y amuletos. |
| `/forge-special` | El equipo del Fogón Eterno y qué materiales guardar para él. |
| `/tools` | El carpintero: hachas, picos y cañas para recolectar más. |
| `/tips` | Qué te falta para tu próxima forja y de dónde sacarlo. |
| `/dismantle` | Desmantelá materiales para conseguir Polvo. |
| `/enchant` | Encantá tu arma o tu amuleto con oro y Polvo. |

## Mascotas y dragón
| Comando | Descripción |
|---|---|
| `/pet view` | Tus mascotas y el bonus de cada una. |
| `/pet feed` | Alimentá a tus mascotas para que suban de nivel. |
| `/pet expedition` | Mandá a una mascota de expedición y recogé lo que trae. |
| `/pet evolve` | Hacé evolucionar una mascota al siguiente tier. |
| `/open` | Abrí cajas y huevos de mascota. |
| `/dragon` | Tu dragón: tipo, tier y mejoras. |
| `/dragon-train` | Entrená a tu dragón con oro. |
| `/dragon-breed` | Criá tu dragón con un amigo del mismo tier, o solo. |
| `/dragon-board` | Buscá pareja de cría de tu tier. |
| `/dragon-name` | Ponele nombre a tu dragón. |

## Economía
| Comando | Descripción |
|---|---|
| `/taberna` | La taberna: comé, comprá comida y cajas, y vendé. |
| `/shop view` | La tienda: comida y cajas en venta. |
| `/shop buy` | Comprá en la tienda. |
| `/shop sell` | Vendé ítems a cambio de oro. |
| `/shop sellall` | Vendé tu mochila y el equipo que llevás puesto de una vez (te pregunta antes). |
| `/bank` | Guardá oro a salvo de la penalidad por morir. |
| `/give` | Dale oro a otro jugador. |
| `/trade` | Cambiá un material por otro de la misma rareza con otro jugador. |
| `/exchange` | Cambiá 3 drops de una zona por 1 que te falte. |
| `/deepshop` | El Tabernero del Inframundo: materiales míticos por Polvo. |
| `/play` | El casino: moneda, tragamonedas, blackjack, ruleta y Elementos (fuego, hielo y agua contra otro jugador, el que gana se lleva la apuesta del otro). |

## Con otros jugadores
| Comando | Descripción |
|---|---|
| `/fight` | Desafiá a otro jugador a un duelo amistoso. |
| `/arena join` | Anotate en el torneo PvP de hoy. |
| `/arena listplayers` | Quiénes están anotados hoy. |
| `/arena results` | La llave y el campeón del último torneo. |
| `/miniboss` | Anotate al jefe mundial de hoy, contra todo el servidor (uno por día, no gasta el cooldown del jefe). |
| `/invitedby` | ¿Te invitó un amigo? Decí quién y los dos se llevan oro. |
| `/leaderboard` | El ranking del servidor: los 50 primeros, de a 10 por página. |
| `/duels` | Tu récord de duelos. |
| `/history` | Cuántas veces jugaste, ganaste y perdiste cada juego. |

## Progreso y premios
| Comando | Descripción |
|---|---|
| `/daily` | Tu premio diario, más grande con la racha. |
| `/vote` | Votá en top.gg: un Baúl de Roble y el jefe se reinicia. |
| `/missions` | Tus misiones del día y de la semana. |
| `/achievements` | Tus logros y sus premios. |
| `/titles` | Tus títulos: elegí uno para tu perfil. |
| `/code` | Canjeá un código por premios. |
| `/cd` | Tus cooldowns de un vistazo (o los de otro jugador). |
| `/reminders` | Elegí qué avisos querés recibir cuando termina una espera. |

## Fuego Nuevo e historia
| Comando | Descripción |
|---|---|
| `/fuegonuevo` | Volvé a empezar más fuerte, con bonos permanentes. |
| `/blessings` | Tus bendiciones, una por cada Fuego Nuevo. |
| `/story` | Las Crónicas del Fogón, capítulo por capítulo. |
| `/class` | Elegí tu clase (solo al empezar o tras un Fuego Nuevo). |
