using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// Exactamente uno de los dos campos viene con valor (mismo patrón que DeepShopResult).
public sealed record VoteResult(string? PlainMessage, Embed? Embed);

// /vote (aa vote, alias aa votar y aa voto): votar en top.gg (por el bot o por el servidor, según el token que se cargue) da un Baúl de Roble y resetea el cooldown del jefe/raid (v0.18.0). Un solo comando para todo: si no votaste (o no se
// puede votar todavía) cuenta qué te llevás y dónde votar; si hay un voto vigente sin cobrar, lo cobra. Cómo se sabe que votaste, las reglas del cobro y por qué no hay webhook:
// GameData/Vote.cs. La lógica vive en métodos estáticos para que el comando de texto use exactamente la misma.
public class VoteModule(
    IItemRepository itemRepository, IVoteRepository voteRepository, ICooldownRepository cooldownRepository, ITopGgVoteClient voteClient, IGameEvents gameEvents)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("vote", "Votá en top.gg: te llevás un Baúl de Roble y el jefe se resetea.")]
    public async Task HandleVoteAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var result = await ExecuteVoteAsync(itemRepository, voteRepository, cooldownRepository, voteClient, gameEvents, Context.User.Id);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude revisar tu voto ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    public static async Task<VoteResult> ExecuteVoteAsync(
        IItemRepository itemRepository, IVoteRepository voteRepository, ICooldownRepository cooldownRepository, ITopGgVoteClient voteClient, IGameEvents gameEvents, ulong discordId)
    {
        if (!voteClient.Enabled)
        {
            return new VoteResult(null, BuildInfoEmbed(VoteScreen.NotAvailable, voteClient.VoteUrl, null));
        }

        var check = await voteClient.CheckAsync(discordId);
        switch (check.Status)
        {
            case VoteCheckStatus.Unavailable:
                return new VoteResult("No pude confirmar tu voto con top.gg ahora mismo. Probá de nuevo en un minuto: no se perdió nada.", null);

            case VoteCheckStatus.NoActiveVote:
            {
                // Sin voto vigente: o todavía no votó, o ya cobró uno hace poco y tiene que esperar para votar de nuevo (la fila de cooldown guarda el momento del último voto cobrado).
                var wait = await cooldownRepository.GetRemainingAsync(discordId, CooldownCatalog.Vote.CommandName, CooldownCatalog.Vote.Duration);
                return new VoteResult(null, BuildInfoEmbed(wait is null ? VoteScreen.Ready : VoteScreen.Waiting, voteClient.VoteUrl, wait));
            }

            default:
            {
                var voted = check.CreatedUtc!.Value;
                var outcome = await voteRepository.ClaimAsync(discordId, voted);

                if (outcome.Status == VoteClaimStatus.BoxMissing)
                {
                    BotLog.Error(new InvalidOperationException($"El premio del voto ({VoteRules.RewardBoxName}) no existe en el catálogo: el voto no se cobró."));
                    return new VoteResult("¡Upa! No pude entregarte la caja del voto. No se cobró nada: avisale al administrador y volvé a intentar.", null);
                }

                var nextVote = VoteRules.NextVoteIn(DateTime.UtcNow, voted, check.ExpiresUtc);
                if (outcome.Status == VoteClaimStatus.AlreadyClaimed)
                {
                    return new VoteResult(null, BuildInfoEmbed(VoteScreen.AlreadyClaimed, voteClient.VoteUrl, nextVote));
                }

                await gameEvents.RecordAsync(discordId, GameEventKinds.Vote, amount: VoteRules.RewardBoxes, detail: outcome.BossReset ? "boss_reset" : "boss_ready");

                var box = await itemRepository.GetByNameAsync(VoteRules.RewardBoxName);
                return new VoteResult(null, BuildClaimedEmbed(box, outcome.BossReset, nextVote));
            }
        }
    }

    // Qué muestra la pantalla de información.
    public enum VoteScreen
    {
        NotAvailable,   // todavía no hay token: se cuenta el premio pero no se puede votar
        Ready,          // se puede votar ya
        Waiting,        // cobró un voto hace poco: falta para el próximo
        AlreadyClaimed, // hay un voto vigente, pero ya se cobró
    }

    // Pura (sin red ni base) para probar los textos. wait: lo que falta para poder votar de nuevo (Waiting y AlreadyClaimed).
    public static Embed BuildInfoEmbed(VoteScreen screen, string? voteUrl, TimeSpan? wait)
    {
        string status = screen switch
        {
            VoteScreen.NotAvailable => "Todavía no se puede votar: se habilita muy pronto. ¡Estate atento!",
            VoteScreen.Waiting => $"⏳ Podés volver a votar en **{TimeFormat.Remaining(wait ?? TimeSpan.Zero)}**.",
            VoteScreen.AlreadyClaimed => $"✅ Ya cobraste este voto. Podés volver a votar en **{TimeFormat.Remaining(wait ?? TimeSpan.Zero)}**.",
            _ => (string.IsNullOrWhiteSpace(voteUrl) ? "🔗 Buscá Asado y Acero en **top.gg** y votá ahí." : $"🔗 **Votá acá:** {voteUrl}")
                 + "\n\nDespués escribí **/vote** otra vez para cobrar (el voto vale unas 12 horas).",
        };

        return new EmbedBuilder()
            .WithTitle("🗳️ Votá por Asado y Acero RPG")
            .WithColor(OnboardingModule.BrandColor)
            .WithDescription(
                "Votar en top.gg ayuda a que más gente nos encuentre. Como agradecimiento te da:\n\n" +
                $"📦 **{VoteRules.RewardBoxes}× {VoteRules.RewardBoxName}** — se abre con **/open**\n" +
                "👑 **El jefe se resetea** — si el cooldown de **/boss** o **/raid** estaba corriendo, queda listo al instante\n\n" +
                status)
            .WithFooter($"Se puede votar cada {ReminderCatalog.WaitLabel(VoteRules.Interval)}.")
            .Build();
    }

    // El mensaje de "cobraste el voto". box puede ser null (si el catálogo falla al leerlo después del cobro, ya pagado): se escribe el nombre a secas.
    public static Embed BuildClaimedEmbed(Item? box, bool bossReset, TimeSpan? nextVote)
    {
        string boxText = box is null ? VoteRules.RewardBoxName : ItemDisplay.Format(box.Emoji, box.Name);
        string bossText = bossReset
            ? "👑 **¡Jefe reseteado!** Ya podés enfrentarlo con **/boss** o armar un **/raid**."
            : "👑 Tu jefe ya estaba listo, así que no hizo falta resetearlo. (Tip: votá justo después de ganarle y se resetea.)";
        string nextText = nextVote is { } left ? $"⏳ Podés volver a votar en **{TimeFormat.Remaining(left)}**." : "⏳ Ya podés votar de nuevo.";

        var embed = new EmbedBuilder()
            .WithTitle("🎉 ¡Gracias por votar!")
            .WithColor(Color.Gold)
            .WithDescription($"📦 Te llevás **{VoteRules.RewardBoxes}×** {boxText} — abrilo con **/open**.\n\n{bossText}\n\n{nextText}");

        if (box is not null)
        {
            embed.WithItemThumbnail(box.Emoji);
        }

        return embed.Build();
    }
}
