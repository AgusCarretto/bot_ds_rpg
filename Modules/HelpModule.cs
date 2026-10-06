using Discord;
using Discord.Interactions;
using BotDsRpg.Services;

// Contenido puramente informativo (sin DB, sin estado): /tutorial explica el core loop del juego
// para retener a jugadores nuevos justo después de /start (ver OnboardingModule), /info es la
// referencia completa de comandos agrupados por categoría. A diferencia de /start, estos SÍ pasan
// por el gate normal de registro (Program.cs) — no son casos especiales.
public class HelpModule : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /tutorial
    [SlashCommand("tutorial", "Aprendé el loop básico del juego: recolección, herrería, combate y supervivencia.")]
    public async Task HandleTutorialAsync()
    {
        try
        {
            await RespondAsync(embed: BuildTutorialEmbed());
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            // Si algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await RespondAsync("No pude cargar el tutorial ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Comando barra: /info
    [SlashCommand("info", "La lista de comandos, o con un tema te explica cómo funciona algo (enchant, play, bank...).")]
    public async Task HandleInfoAsync(
        [Summary("tema", "Opcional: de qué querés saber cómo funciona (elegí de la lista o escribilo).")] [Autocomplete(typeof(InfoTopicAutocompleteHandler))] string? topic = null)
    {
        try
        {
            await RespondAsync(embed: BuildTopicEmbed(topic));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await RespondAsync("No pude cargar la ayuda ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // /info y "aa info": la lista de comandos sin tema; con un tema ("aa info enchant", "aa info play", "/info tema:bank"), la explicación de cómo funciona (Modules/HelpTopics.cs);
    // con un tema que no existe, la lista de temas avisando que no lo encontró (nunca queda sin respuesta).
    public static Embed BuildTopicEmbed(string? topic) => HelpTopics.Resolve(topic);

    // Estáticos (sin dependencia de Context) para que Modules/TextCommandModule.cs arme los
    // mismos embeds en "aa tutorial"/"aa info".
    public static Embed BuildTutorialEmbed()
    {
        return new EmbedBuilder()
            .WithTitle("📖 Cómo jugar Asado y Acero RPG")
            .WithColor(OnboardingModule.BrandColor)
            .WithDescription("El loop básico del juego, en 5 pasos:")
            .AddField("🪓 Recolección", "Usá `/chop` o `/mine` para conseguir recursos básicos.", false)
            .AddField("⚒️ Herrería", "Usá `/forge` para ver las recetas de cada zona y crear armas y amuletos: se equipan solos (para cambiar, vendé el que llevás en `/taberna`). ¿No sabés qué farmear? `/tips` te dice qué te falta.", false)
            .AddField(
                "⚔️ Combate",
                "Usá `/hunt` (manual) o `aa ah` (automático) para ganar Oro, XP y drops de monstruos. " +
                "En la pelea manual cada clase tiene una habilidad especial (botón verde, ver `/profile`); " +
                "el modo automático solo ataca normal.",
                false)
            .AddField(
                "🥩 Supervivencia",
                "Usá `/shop` para comprar comida y `/heal` para curarte (¡cuidado, curarte en combate le da un turno extra al enemigo!). " +
                "Perder un combate te cuesta la EXP del nivel y el 5 % del oro que llevás: lo que guardes en el banco (`/bank`) no se toca.",
                false)
            .AddField(
                "📋 Misiones y logros",
                "Cada día hay 3 misiones y cada semana 2 (`/missions`), y hay logros por tramos (`/achievements`). Se cuentan solas mientras jugás " +
                "y dan oro, XP y cajas para abrir con `/open`. Las diarias se reinician a medianoche, hora de Uruguay.",
                false)
            .WithFooter("Usá /info para ver la lista completa de comandos.")
            .Build();
    }

    public static Embed BuildInfoEmbed()
    {
        return new EmbedBuilder()
            .WithTitle("📜 Comandos de Asado y Acero RPG")
            .WithColor(OnboardingModule.BrandColor)
            .WithDescription("Todo comando de barra tiene su versión de texto con el prefijo `aa ` (ej. `aa hunt`). Usá `/tutorial` para el loop básico.")
            .AddField(
                "💰 Economía y Suerte",
                "`/daily` — Recompensa diaria\n`/shop` — Comprar comida y cajas, y vender (view/buy/sell/sellall)\n`/open` — Abrir cajas de tu inventario (`aa open <caja>`)\n`/play` — Casino (slots/coinflip)\n`/give` — Dale monedas a otro jugador (`aa give @jugador 100`)\n`/exchange` — El tabernero te cambia 3 drops de una zona por 1 de la misma (`aa exchange \"drop\" \"otro drop\"`)\n`/bank` — Tu cuenta del banco (se abre con 1.000 de oro): guardá oro a salvo de la penalidad por morir (`aa bank`)",
                false)
            .AddField(
                "⚔️ Aventura",
                "`/hunt` / `aa ah` — Pelear en tu zona actual (manual/automático; el automático pide al menos 65 % de vida)\n`/travel` — Monstruo élite de tu zona: más difícil, recompensa x30 (cada 30 min)\n`/boss` — Enfrentar al Jefe de tu zona actual (cada 5 h)\n`/raid` — Jefe de zona cooperativo (2 a 6 jugadores)\n`/fight` — Duelo amistoso con otro jugador (no se gana ni se pierde nada)\n`/arena` — Torneo PvP diario: `join`, `listplayers` y `results` (se juega a las 00:00, hora de Uruguay)\n`/zona` — Viajar a otra zona del mundo\n`/zonas` — Ver todas las zonas y sus niveles\n`/drops` — Qué suelta cada monstruo de cada zona\n`/chop` — Recolectar madera\n`/mine` — Recolectar piedra/minerales",
                false)
            .AddField(
                "📈 Progresión",
                "`/forge` (`aa herrero`) — Pasá por la herrería: forjá armas y amuletos (quedan equipados solos) y mirá las recetas de cada zona\n`/taberna` — Comé, comprá comida y cajas y vendé con el tabernero (también tu arma o amuleto equipados, para poder forjar otro)\n`/heal` — Curarte toda la vida de una con lo necesario de tu inventario (no en combate; podés elegir la comida)\n`/use` — Curarte con un consumible (en combate: en `/travel` y `/boss` una sola vez por pelea, también desde el desplegable). Los banquetes Míticos suman +15% de ataque por 30 min\n`/missions` — Misiones diarias y semanales (reclamás los premios ahí)\n`/achievements` — Tus logros por tramos\n`/trade` — Cambiá 1 material por 1 de la misma rareza con otro jugador\n`/leaderboard` — Ranking del server",
                false)
            .AddField(
                "✨ Polvo y encantamientos",
                "`/dismantle` — Desarmá de 1 a 100 materiales y quedate con su Polvo (`aa desmantelar 30 Piedra`)\n" +
                "`/enchant` — Gastá Polvo y oro para encantar tu arma o tu amuleto: sale un tier al azar (Tibio ➜ Soberano) y solo reemplaza al actual si es mejor (`aa encantar arma`). **`/enchant info`** (o `aa info enchant`) muestra los tiers con su chance y su bonus, y lo que cuesta cada intento\n" +
                "💀 Si perdés un combate, la EXP del nivel vuelve a 0 y perdés el 5 % del oro de la billetera (el del banco no).",
                false)
            .AddField(
                "🔥 El Fogón Eterno",
                "`/zona 0` — La puerta al final del mundo: se abre al vencer al jefe de la última zona y pide el equipo del Fogón puesto. Adentro solo hay `/boss` contra el Asador Eterno; ganarle habilita el Fuego Nuevo (`/info tema:fogon`)",
                false)
            .AddField(
                "🐾 Mascotas",
                "`/pet view` — Tus mascotas: una por zona, y dan su bonus **todas a la vez** (`aa pet`)\n" +
                "`/pet feed` — Dales Comida para Mascotas (se compra en la `/taberna`), una vez por hora cada una. El huevo llega la primera vez que vencés al jefe de cada zona: abrilo con `/open`",
                false)
            .AddField(
                "🛠️ Utilidad",
                "`/start` — Empezar tu aventura\n`/class` — Elegir/cambiar de clase\n`/profile` — Ver tu ficha\n`/history` — Tu historial por juego: cuántas veces jugaste, ganaste y perdiste\n`/duels` — Tu récord de duelos y los últimos rivales\n`/inventory` — Ver tu inventario\n`/cd` — Ver tus cooldowns (y si ya te anotaste en la Arena de hoy)\n`/tips` — Qué te falta para tu próxima forja y de dónde sacarlo\n`/tutorial` — Este loop básico\n`/info` — Esta lista de comandos",
                false)
            .AddField(
                "📚 Cómo funciona cada cosa",
                $"**/info tema:<tema>** o `aa info <tema>` explica un tema con sus números. Temas: {HelpTopics.KeysLine()}.",
                false)
            .WithFooter($"Asado y Acero RPG v{BotVersion.Current}")
            .Build();
    }
}
