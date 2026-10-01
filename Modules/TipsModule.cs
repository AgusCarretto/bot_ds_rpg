using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /tips (aa tips): el consejo de "¿qué hago ahora?" — qué receta de tu zona te conviene tener en la mira, qué te falta para ella y por qué
// comando se consigue (GameData/FarmAdvisor.cs elige, Services/FarmAdviceService.cs junta los datos). Antes el consejo se pegaba como un
// campo más al final de /chop, /mine y cada victoria, y comía mucho espacio en mensajes que ya tienen bastante: ahora es un comando aparte,
// y esos mensajes quedan cortos. Solo lo ve quien lo pide (es sobre SU mochila).
public class TipsModule(IFarmAdvisor farmAdvisor) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("tips", "Un consejo: qué te falta para tu próxima forja y de dónde sacarlo.")]
    public async Task HandleTipsAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            await FollowupAsync(embed: await BuildTipsEmbedAsync(farmAdvisor, Context.User.Id), ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude armar el consejo ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Público y sin Context para que "aa tips" muestre exactamente lo mismo.
    public static async Task<Embed> BuildTipsEmbedAsync(IFarmAdvisor advisor, ulong discordId)
    {
        var advice = await advisor.AdviceAsync(discordId);

        if (advice is null)
        {
            // El consejo es null si ya forjaste todo lo que ves en tu zona, si tu zona no tiene recetas, o si algo falló (se registra y no rompe nada).
            return new EmbedBuilder()
                .WithTitle("💡 Consejo")
                .WithColor(Color.Gold)
                .WithDescription(
                    "Por ahora no tengo nada para aconsejarte: o ya forjaste todo lo de tu zona, o todavía no hay recetas para ella.\n\n" +
                    "Mirá a dónde seguir con `/zonas`.")
                .Build();
        }

        var (title, value) = FarmAdvisor.ToField(advice);

        return new EmbedBuilder()
            .WithTitle(title)
            .WithColor(Color.Gold)
            .WithDescription(value)
            .WithFooter("El consejo sale de lo que tenés en la mochila ahora mismo. Todas las recetas, en /forge.")
            .Build();
    }
}
