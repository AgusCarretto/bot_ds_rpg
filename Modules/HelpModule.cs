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

    // /info sin tema (v0.18.0, achicado a pedido del dueño: «tenemos que achicarlo, poné lo más importante»): solo los NOMBRES de los comandos, agrupados, una línea por grupo. Qué hace cada uno ya lo dice
    // Discord al elegirlo de la lista de "/" y, para entender cómo funciona algo, está "/info tema:<tema>" (HelpTopics). Antes eran 14 campos con una explicación por comando, que se salía de la pantalla.
    // Lo que NO figura acá (/shop, /zonas, /history, /duels, /blessings...) sigue funcionando: está en los temas de ayuda y en la lista de Discord.
    public static Embed BuildInfoEmbed()
    {
        return new EmbedBuilder()
            .WithTitle("📜 Comandos de Asado y Acero RPG")
            .WithColor(OnboardingModule.BrandColor)
            .WithDescription("Todo comando de barra tiene su versión de texto con el prefijo `aa ` (ej. `aa hunt`). Para saber **cómo funciona** algo: `/info tema:<tema>`.")
            .AddField("⚔️ Pelear", "`/hunt` (`aa ah` es el automático) · `/travel` · `/boss` (solo o con amigos) · `/zona` · `/drops`", false)
            .AddField("🪓 Recolectar y forjar", "`/chop` · `/mine` · `/forge` · `/enchant` · `/dismantle` · `/tips` (qué te falta farmear)", false)
            .AddField("🍖 Comida y plata", "`/taberna` (comer, comprar y vender) · `/heal` · `/use` · `/open` · `/daily` · `/bank` · `/play`", false)
            .AddField("📋 Progreso", "`/missions` · `/achievements` · `/pet` · `/professions` · `/story` · `/leaderboard`", false)
            .AddField("👥 Con otros jugadores", "`/fight` · `/arena` · `/boss @amigo` · `/give` · `/trade` · `/exchange`", false)
            .AddField("🔥 Final del juego", "`/forge-special` · `/zona 0` · `/fuegonuevo` · `/deepshop`", false)
            .AddField("🛠️ Tu cuenta", "`/start` · `/class` · `/profile` · `/inventory` · `/cd` · `/reminders` · `/vote` · `/tutorial`", false)
            .AddField("📚 Cómo funciona cada cosa", $"`/info tema:<tema>` o `aa info <tema>`. Temas: {HelpTopics.KeysLine()}.", false)
            .WithFooter($"Asado y Acero RPG v{BotVersion.Current}")
            .Build();
    }
}
