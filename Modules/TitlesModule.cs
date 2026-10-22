using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// La pantalla de /titles: una página por tema con los títulos (✅ los que tenés, 🔒 los que faltan y cómo conseguirlos), un desplegable para ponerte uno en el perfil y un botón para sacarlo.
// Embed (Embed null = no hay pantalla y PlainMessage explica por qué, por ejemplo que todavía no tiene cuenta).
public sealed record TitlesView(Embed? Embed, MessageComponent? Components, string? PlainMessage);

// Resultado de ponerse o sacarse un título: el mensaje que se muestra arriba de la pantalla y si se pudo.
public sealed record TitleChangeResult(bool Ok, string Message);

// /titles (aa titles, aa title): los títulos son cosméticos y se ven en el perfil como «Nombre - Título (Clase)». Cuáles tenés se calcula de lo que el juego ya cuenta (GameData/TitleCatalog.cs); lo único que se
// guarda es cuál tenés puesto. Los controles llevan el dueño en el id (otro jugador no puede cambiarte el título con tu pantalla) y la página, para volver a la misma.
public class TitlesModule(IUserRepository userRepository, IZoneRepository zoneRepository, ITitleRepository titleRepository)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string PagePrefix = "titles_page";
    private const string EquipPrefix = "titles_equip";
    private const string ClearPrefix = "titles_clear";

    [SlashCommand("titles", "Tus títulos: mirá los que desbloqueaste y ponete uno en el perfil.")]
    public async Task HandleTitlesAsync()
    {
        await DeferAsync();

        try
        {
            var view = await BuildViewAsync(userRepository, zoneRepository, titleRepository, Context.User.Id);
            await FollowupAsync(view.PlainMessage, embed: view.Embed, components: view.Components, ephemeral: view.Embed is null);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude mostrar tus títulos, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction($"{PagePrefix}:*")]
    public async Task HandlePageAsync(string ownerRaw, string[] selected)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Esos títulos son de otra persona: usá **/titles** para ver los tuyos.", ephemeral: true);
                return;
            }

            var view = await BuildViewAsync(userRepository, zoneRepository, titleRepository, Context.User.Id, selected.FirstOrDefault());
            await ReplaceAsync(view);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude abrir esa página de títulos, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction($"{EquipPrefix}:*:*")]
    public async Task HandleEquipAsync(string ownerRaw, string pageKey, string[] selected)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Esos títulos son de otra persona: usá **/titles** para ver los tuyos.", ephemeral: true);
                return;
            }

            var result = await ExecuteEquipAsync(userRepository, zoneRepository, titleRepository, Context.User.Id, selected.FirstOrDefault() ?? string.Empty);
            var view = await BuildViewAsync(userRepository, zoneRepository, titleRepository, Context.User.Id, pageKey, result.Message);
            await ReplaceAsync(view);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude ponerte ese título, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction($"{ClearPrefix}:*:*")]
    public async Task HandleClearAsync(string ownerRaw, string pageKey)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Esos títulos son de otra persona: usá **/titles** para ver los tuyos.", ephemeral: true);
                return;
            }

            var result = await ExecuteClearAsync(userRepository, titleRepository, Context.User.Id);
            var view = await BuildViewAsync(userRepository, zoneRepository, titleRepository, Context.User.Id, pageKey, result.Message);
            await ReplaceAsync(view);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude sacarte el título, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    private Task ReplaceAsync(TitlesView view) =>
        ModifyOriginalResponseAsync(p =>
        {
            p.Embed = view.Embed;
            p.Components = view.Components ?? new ComponentBuilder().Build();
            p.Content = view.PlainMessage ?? string.Empty;
        });

    // ---- Lógica compartida con "aa titles" y "aa title" (sin Context) ----

    // note: una línea que se muestra arriba (lo que pasó al ponerte o sacarte un título).
    public static async Task<TitlesView> BuildViewAsync(
        IUserRepository userRepository, IZoneRepository zoneRepository, ITitleRepository titleRepository, ulong discordId, string? pageKey = null, string? note = null)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new TitlesView(null, null, "Todavía no tenés cuenta: empezá con **/start** y después mirá tus títulos.");
        }

        var context = await LoadContextAsync(zoneRepository, titleRepository, discordId);
        var page = TitleCatalog.PageOf(pageKey);
        return new TitlesView(BuildEmbed(page, context, player.EquippedTitle, note), BuildComponents(discordId, page, context, player.EquippedTitle), null);
    }

    // Ponerse un título: tiene que existir y estar desbloqueado.
    public static async Task<TitleChangeResult> ExecuteEquipAsync(
        IUserRepository userRepository, IZoneRepository zoneRepository, ITitleRepository titleRepository, ulong discordId, string titleKey)
    {
        if (await userRepository.GetByDiscordIdAsync(discordId) is null)
        {
            return new TitleChangeResult(false, "Todavía no tenés cuenta: empezá con **/start**.");
        }

        if (TitleCatalog.Find(titleKey) is not { } title)
        {
            return new TitleChangeResult(false, "No conozco ese título: elegilo de la lista.");
        }

        var context = await LoadContextAsync(zoneRepository, titleRepository, discordId);
        if (!TitleCatalog.IsUnlocked(title, context))
        {
            return new TitleChangeResult(false, $"Todavía no tenés **{title.Name}**: {title.Hint}.");
        }

        await titleRepository.SetEquippedAsync(discordId, title.Key);
        return new TitleChangeResult(true, $"🏷️ Ahora en tu perfil sos **{title.Name}**.");
    }

    public static async Task<TitleChangeResult> ExecuteClearAsync(IUserRepository userRepository, ITitleRepository titleRepository, ulong discordId)
    {
        if (await userRepository.GetByDiscordIdAsync(discordId) is null)
        {
            return new TitleChangeResult(false, "Todavía no tenés cuenta: empezá con **/start**.");
        }

        await titleRepository.SetEquippedAsync(discordId, null);
        return new TitleChangeResult(true, $"🏷️ Te sacaste el título: en tu perfil vuelve a salir **{TitleCatalog.Default.Name}**.");
    }

    private static async Task<TitleContext> LoadContextAsync(IZoneRepository zoneRepository, ITitleRepository titleRepository, ulong discordId) =>
        await titleRepository.GetContextAsync(discordId, ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync()));

    // La página: arriba lo que tenés puesto, y después cada título del tema (✅ lo tenés, 🔒 cómo conseguirlo y cuánto llevás). Pública y pura para probarla.
    public static Embed BuildEmbed(TitlePage page, TitleContext context, string? equippedKey, string? note = null)
    {
        var equipped = TitleCatalog.Find(equippedKey);
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(note))
        {
            lines.Add(note);
            lines.Add(string.Empty);
        }

        lines.Add(equipped is null
            ? $"Puesto: _ninguno_ (en tu perfil sale «{TitleCatalog.Default.Name}»)"
            : $"Puesto: **{equipped.Name}**");
        lines.Add(string.Empty);

        foreach (var title in TitleCatalog.OfPage(page.Key))
        {
            if (TitleCatalog.IsUnlocked(title, context))
            {
                lines.Add($"✅ **{title.Name}**" + (equipped?.Key == title.Key ? " ◀ puesto" : string.Empty));
            }
            else
            {
                string? progress = TitleCatalog.Progress(title, context);
                lines.Add($"🔒 {title.Name} — {title.Hint}" + (progress is null ? string.Empty : $" _({progress})_"));
            }
        }

        return new EmbedBuilder()
            .WithTitle($"🏷️ Títulos · {page.Emoji} {page.Name}")
            .WithColor(Color.Gold)
            .WithDescription(string.Join("\n", lines))
            .WithFooter($"Tenés {TitleCatalog.UnlockedCount(context)} de {TitleCatalog.All.Count} · el que te pongas se ve en tu perfil, al lado de tu nombre")
            .Build();
    }

    // Los controles: el desplegable de páginas, el de los títulos que podés ponerte de esta página (sin él si no hay ninguno) y el botón para sacarlo. Sin emojis propios en las opciones.
    public static MessageComponent BuildComponents(ulong discordId, TitlePage page, TitleContext context, string? equippedKey)
    {
        var pageMenu = new SelectMenuBuilder()
            .WithCustomId($"{PagePrefix}:{discordId}")
            .WithPlaceholder("Elegí una página de títulos")
            .WithMinValues(1)
            .WithMaxValues(1);
        foreach (var other in TitleCatalog.Pages)
        {
            var inPage = TitleCatalog.OfPage(other.Key);
            int have = inPage.Count(t => TitleCatalog.IsUnlocked(t, context));
            pageMenu.AddOption($"{other.Emoji} {other.Name}", other.Key, $"{have} de {inPage.Count} desbloqueados", isDefault: other.Key == page.Key);
        }

        var builder = new ComponentBuilder().WithSelectMenu(pageMenu, row: 0);

        var owned = TitleCatalog.OfPage(page.Key).Where(t => TitleCatalog.IsUnlocked(t, context)).Take(25).ToList();
        if (owned.Count > 0)
        {
            var equipMenu = new SelectMenuBuilder()
                .WithCustomId($"{EquipPrefix}:{discordId}:{page.Key}")
                .WithPlaceholder("Ponete un título de esta página")
                .WithMinValues(1)
                .WithMaxValues(1);
            foreach (var title in owned)
            {
                equipMenu.AddOption(title.Name, title.Key, equippedKey == title.Key ? "Puesto ahora" : null);
            }

            builder.WithSelectMenu(equipMenu, row: 1);
        }

        builder.WithButton("Quitar título", $"{ClearPrefix}:{discordId}:{page.Key}", ButtonStyle.Secondary, new Emoji("🏷️"), disabled: TitleCatalog.Find(equippedKey) is null, row: 2);
        return builder.Build();
    }
}
