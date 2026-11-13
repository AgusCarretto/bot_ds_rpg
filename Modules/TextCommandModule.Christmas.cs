using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

public partial class TextCommandModule
{
    // "aa navidad" — misma pantalla que /navidad (ChristmasModule): tus Regalos y el calendario de adviento con el botón del día.
    // "aa navidad tienda" abre la tienda; "aa navidad comprar campana 10" compra sin pasar por el menú (la cantidad solo vale para las Campanas).
    [Command("navidad")]
    [Alias("xmas", "christmas")]
    [Summary("Navidad en el Fogón: tus Regalos, el calendario de adviento y la tienda (aa navidad tienda / aa navidad comprar campana 10).")]
    public async Task ChristmasAsync([Remainder] string? args = null)
    {
        try
        {
            var words = (args ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string verb = words.Length > 0 ? words[0].ToLowerInvariant() : string.Empty;

            if (verb is "tienda" or "shop")
            {
                var shop = await ChristmasModule.BuildShopAsync(christmasRepository, christmasService, Context.User.Id, DateTime.UtcNow);
                await ReplyAsync(embed: shop.Embed, components: shop.Components);
                return;
            }

            if (verb is "comprar" or "buy")
            {
                int quantity = words.Length > 2 && int.TryParse(words[2], out int n) ? n : 1;
                var bought = await ChristmasModule.BuyAsync(
                    userRepository, zoneRepository, christmasRepository, christmasService, Context.User.Id, words.ElementAtOrDefault(1), quantity, DateTime.UtcNow);
                await ReplyAsync(bought.Error, embed: bought.Embed);
                return;
            }

            var view = await ChristmasModule.BuildAsync(christmasRepository, christmasService, Context.User.Id, DateTime.UtcNow);
            await ReplyAsync(embed: view.Embed, components: view.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude abrir la Navidad, intentá de nuevo en un momento."));
        }
    }

    // "aa adviento" — abre la caja de hoy del calendario sin pasar por el botón.
    [Command("adviento")]
    [Alias("calendario", "advent")]
    [Summary("Abrí la caja de hoy del calendario de adviento (del 1 al 24 de diciembre).")]
    public async Task AdventAsync()
    {
        try
        {
            var result = await ChristmasModule.ClaimAsync(userRepository, zoneRepository, christmasRepository, christmasService, Context.User.Id, DateTime.UtcNow);
            await ReplyAsync(result.Error, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude abrir la caja de hoy, intentá de nuevo en un momento."));
        }
    }

    // "aa campana" — usa una Campana de la tienda de Navidad para reiniciar /hunt (si ya está listo, no la gasta).
    [Command("campana")]
    [Alias("bell", "campanas")]
    [Summary("Usá una Campana de Navidad para reiniciar la espera de /hunt.")]
    public async Task BellAsync()
    {
        try
        {
            await ReplyAsync(await ChristmasModule.UseBellAsync(christmasRepository, christmasService, Context.User.Id));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude usar la Campana, intentá de nuevo en un momento."));
        }
    }
}
