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
    [SlashCommand("info", "Mostrá la lista completa de comandos, agrupados por categoría.")]
    public async Task HandleInfoAsync()
    {
        try
        {
            await RespondAsync(embed: BuildInfoEmbed());
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await RespondAsync("No pude cargar la ayuda ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Estáticos (sin dependencia de Context) para que Modules/TextCommandModule.cs arme los
    // mismos embeds en "aa tutorial"/"aa info".
    public static Embed BuildTutorialEmbed()
    {
        return new EmbedBuilder()
            .WithTitle("📖 Cómo jugar Asado y Acero RPG")
            .WithColor(OnboardingModule.BrandColor)
            .WithDescription("El loop básico del juego, en 5 pasos:")
            .AddField("🪓 Recolección", "Usá `/chop` o `/mine` para conseguir recursos básicos.", false)
            .AddField("⚒️ Herrería", "Usá `/forge` para ver recetas y crear armas más fuertes.", false)
            .AddField(
                "⚔️ Combate",
                "Usá `/hunt` (manual) o `aa ah` (automático) para ganar Oro, XP y drops de monstruos. " +
                "En la pelea manual cada clase tiene una habilidad especial (botón verde, ver `/profile`); " +
                "el modo automático solo ataca normal.",
                false)
            .AddField(
                "🥩 Supervivencia",
                "Usá `/shop` para comprar comida y `/heal` para curarte (¡cuidado, curarte en combate le da un turno extra al enemigo!).",
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
                "`/daily` — Recompensa diaria\n`/shop` — Comprar comida y cajas, y vender (view/buy/sell/sellall)\n`/open` — Abrir cajas de tu inventario (`aa open <caja>`)\n`/play` — Casino (slots/coinflip)\n`/give` — Dale monedas a otro jugador (`aa give @jugador 100`)",
                false)
            .AddField(
                "⚔️ Aventura",
                "`/hunt` / `aa ah` — Pelear en tu zona actual (manual/automático)\n`/travel` — Monstruo élite de tu zona: más difícil, recompensa x10\n`/boss` — Enfrentar al Jefe de tu zona actual\n`/raid` — Jefe de zona cooperativo (2 a 6 jugadores)\n`/zona` — Viajar a otra zona del mundo\n`/zonas` — Ver todas las zonas y sus niveles\n`/drops` — Qué suelta cada monstruo de cada zona\n`/chop` — Recolectar madera\n`/mine` — Recolectar piedra/minerales",
                false)
            .AddField(
                "📈 Progresión",
                "`/forge` (`aa herrero`) — Pasá por la herrería: elegí de la lista qué forjar\n`/taberna` — Comé, comprá comida y cajas y vendé con el tabernero\n`/equip` — Equipar arma/amuleto\n`/heal` — Curarte con un consumible del inventario (no en combate)\n`/use` — Curarte con un consumible (en combate: en `/travel` y `/boss` una sola vez por pelea, también desde el desplegable). Los banquetes Míticos suman +15% de ataque por 30 min\n`/missions` — Misiones diarias y semanales (reclamás los premios ahí)\n`/achievements` — Tus logros por tramos\n`/trade` — Cambiá 1 material por 1 de la misma rareza con otro jugador\n`/leaderboard` — Ranking del server",
                false)
            .AddField(
                "🛠️ Utilidad",
                "`/start` — Empezar tu aventura\n`/class` — Elegir/cambiar de clase\n`/profile` — Ver tu ficha\n`/inventory` — Ver tu inventario\n`/cd` — Ver tus cooldowns\n`/tutorial` — Este loop básico\n`/info` — Esta lista de comandos",
                false)
            .WithFooter($"Asado y Acero RPG v{BotVersion.Current}")
            .Build();
    }
}
