using Discord;
using Discord.Interactions;

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
        catch (Exception)
        {
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
        catch (Exception)
        {
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
            .WithDescription("El loop básico del juego, en 4 pasos:")
            .AddField("🪓 Recolección", "Usá `/chop` o `/mine` para conseguir recursos básicos.", false)
            .AddField("⚒️ Herrería", "Usá `/forge` para ver recetas y crear armas más fuertes.", false)
            .AddField("⚔️ Combate", "Usá `/hunt` (manual) o `aa ah` (automático) para ganar Oro, XP y drops de monstruos.", false)
            .AddField(
                "🥩 Supervivencia",
                "Usá `/shop` para comprar comida y `/heal` para curarte (¡cuidado, curarte en combate le da un turno extra al enemigo!).",
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
                "`/daily` — Recompensa diaria\n`/shop` — Comprar/vender consumibles (view/buy/sell/sellall)\n`/play` — Casino (slots/coinflip)",
                false)
            .AddField(
                "⚔️ Aventura",
                "`/hunt` / `aa ah` — Pelear en tu zona actual (manual/automático)\n`/travel` — Viaje más difícil, mejores recompensas\n`/boss` — Enfrentar al Jefe de tu zona actual\n`/zona` — Viajar a otra zona del mundo\n`/zonas` — Ver todas las zonas y sus niveles\n`/chop` — Recolectar madera\n`/mine` — Recolectar piedra/minerales",
                false)
            .AddField(
                "📈 Progresión",
                "`/forge` — Fabricar equipo\n`/equip` — Equipar arma/amuleto\n`/heal` — Curarte con un consumible del inventario (no en combate)\n`/use` — Curarte con un consumible (sí en combate)\n`/leaderboard` — Ranking del server",
                false)
            .AddField(
                "🛠️ Utilidad",
                "`/start` — Empezar tu aventura\n`/class` — Elegir/cambiar de clase\n`/profile` — Ver tu ficha\n`/inventory` — Ver tu inventario\n`/cd` — Ver tus cooldowns\n`/tutorial` — Este loop básico\n`/info` — Esta lista de comandos",
                false)
            .Build();
    }
}
