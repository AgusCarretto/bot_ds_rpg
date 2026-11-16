using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// La libreta de códigos (v0.56.4; /codelist, aa codelist, aa code list). El dueño (2026-11-16): «ver los códigos como los títulos, páginas de códigos ya desbloqueados y los que
// no poner ??? sin pista, solo saber cuántos pudieron sacar». Están TODOS los códigos que existieron (también los vencidos y apagados), del más viejo al más nuevo: los que canjeaste
// con su nombre y la fecha; los que no, «???». De cada uno se ve cuántas personas lo canjearon. Solo mira: no crea cuentas.
public class CodeListModule(IRedeemCodeRepository redeemCodeRepository) : InteractionModuleBase<SocketInteractionContext>
{
    private const string PagePrefix = "codes_page";
    public const int PageSize = 10;

    [SlashCommand("codelist", "Tus códigos: los que canjeaste y los que te faltan (??? sin pista).")]
    public async Task HandleCodeListAsync([Summary("pagina", "Qué página mirar (sin nada, la primera)")] int? pagina = null)
    {
        // Solo para quien la pide (el dueño, 2026-11-16): si la viera todo el canal, se sabría qué códigos sacó cada uno.
        await DeferAsync(ephemeral: true);

        try
        {
            var view = await BuildAsync(redeemCodeRepository, Context.User.Id, (pagina ?? 1) - 1);
            await FollowupAsync(embed: view.Embed, components: view.Components, ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude abrir tus códigos, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    [ComponentInteraction($"{PagePrefix}:*:*")]
    public async Task HandlePageAsync(string ownerRaw, string pageRaw)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Esa libreta es de otra persona: abrí la tuya con **/codelist**."), ephemeral: true);
                return;
            }

            var view = await BuildAsync(redeemCodeRepository, ownerId, int.TryParse(pageRaw, out int page) ? page : 0);
            await ModifyOriginalResponseAsync(p => { p.Embed = view.Embed; p.Components = view.Components; });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude abrir tus códigos, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    public sealed record CodeListView(Embed Embed, MessageComponent? Components = null);

    // page: desde 0. Una página fuera de rango muestra la más cercana.
    public static async Task<CodeListView> BuildAsync(IRedeemCodeRepository repository, ulong discordId, int page)
    {
        var book = await repository.GetCodeBookAsync(discordId);
        var embed = new EmbedBuilder().WithTitle(Loc.T("🎟️ Tus códigos")).WithColor(Color.Orange);
        if (book.Count == 0)
        {
            embed.WithDescription(Loc.T("Todavía no salió ningún código. Atento a los anuncios: se canjean con **aa code CODIGO**."));
            return new CodeListView(embed.Build());
        }

        int pages = (book.Count + PageSize - 1) / PageSize;
        int current = Math.Clamp(page, 0, pages - 1);
        int have = book.Count(c => c.ClaimedAt is not null);

        var lines = book.Skip(current * PageSize).Take(PageSize).Select(c => c.ClaimedAt is { } claimed
            ? Loc.F($"✅ **{c.Code}** · lo canjeaste el {(DateTime.SpecifyKind(claimed, DateTimeKind.Utc) + UruguayCalendar.Offset):dd/MM} · {Redeemers(c.Uses)}")
            : Loc.F($"❓ **???** · {Redeemers(c.Uses)}"));
        embed.WithDescription(string.Join("\n", lines));
        embed.WithFooter(Loc.F($"Página {current + 1}/{pages} · Tenés {have} de {book.Count}"));

        MessageComponent? components = null;
        if (pages > 1)
        {
            components = new ComponentBuilder()
                .WithButton(Loc.T("Anterior"), $"{PagePrefix}:{discordId}:{current - 1}", ButtonStyle.Secondary, new Emoji("◀️"), disabled: current == 0)
                .WithButton(Loc.T("Siguiente"), $"{PagePrefix}:{discordId}:{current + 1}", ButtonStyle.Secondary, new Emoji("▶️"), disabled: current >= pages - 1)
                .Build();
        }

        return new CodeListView(embed.Build(), components);
    }

    // «nadie lo sacó» / «lo sacó 1 persona» / «lo sacaron 37»: una oración entera por variante.
    public static string Redeemers(int uses) => uses switch
    {
        0 => Loc.T("nadie lo sacó todavía"),
        1 => Loc.T("lo sacó 1 persona"),
        _ => Loc.F($"lo sacaron {GameHistory.Number(uses)}"),
    };
}
