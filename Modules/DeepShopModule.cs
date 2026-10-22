using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// AttachmentPath: el archivo de la foto del tabernero que hay que adjuntar al mensaje (null si no hay foto o es una URL).
public sealed record DeepShopScene(Embed? Embed, MessageComponent? Components, string? PlainMessage = null, string? AttachmentPath = null);

// Exactamente uno de los dos campos viene con valor (mismo patrón que DustModule.DustResult).
public sealed record DeepShopResult(string? PlainMessage, Embed? Embed);

// /deepshop (aa deepshop, alias aa inframundo): el Tabernero del Inframundo (v0.17.0). Se conoce al vencer al jefe de la última zona (GameData/DeepShop.cs) y vende con Polvo, a 3.000 cada uno, la Corteza del Árbol
// de Vida y el Fragmento de Meteorito: los dos Míticos de recolección que piden el Trinche y la Brasa del Fogón y que salen al 0,5 % por /chop y /mine. La escena sigue el patrón de la taberna: su frase, la carta,
// una lista desplegable (de a UNA unidad; para más cantidad está /deepshop item:… cantidad:…) y, tras comprar, la escena se refresca y la respuesta sale en un mensaje aparte. El id de la lista lleva al dueño.
public class DeepShopModule(
    IUserRepository userRepository, IZoneRepository zoneRepository, IItemRepository itemRepository, IDustRepository dustRepository, IGameEvents gameEvents)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string MenuPrefix = "deepshop_buy";

    [SlashCommand("deepshop", "El Tabernero del Inframundo: comprá la Corteza y el Meteorito con Polvo.")]
    public async Task HandleDeepShopAsync(
        [Summary("item", "Qué comprás. Sin elegir ves la carta.")] [Choice("Corteza del Árbol de Vida", "corteza"), Choice("Fragmento de Meteorito", "meteorito")] string? item = null,
        [Summary("cantidad", "Cuántos comprás (por defecto 1).")] [MinValue(1)] [MaxValue(DeepShop.MaxPerPurchase)] int quantity = 1)
    {
        await DeferAsync();

        try
        {
            if (item is null)
            {
                var scene = await BuildSceneAsync(userRepository, zoneRepository, dustRepository, itemRepository, Context.User.Id, null);
                await SendSceneAsync(scene);
                return;
            }

            var result = await ExecuteBuyAsync(userRepository, zoneRepository, itemRepository, dustRepository, gameEvents, Context.User.Id, item, quantity);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: result.Embed is null);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! El Tabernero del Inframundo no te escucha ahora, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Elegir de la lista = comprar UNA unidad. El Tabernero contesta en un mensaje aparte y la escena se refresca con tu Polvo al día.
    [ComponentInteraction($"{MenuPrefix}:*")]
    public async Task HandleBuyAsync(string ownerRaw, string[] selected)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Esa charla es de otra persona: pasá por el inframundo con **/deepshop**.", ephemeral: true);
                return;
            }

            var result = await ExecuteBuyAsync(userRepository, zoneRepository, itemRepository, dustRepository, gameEvents, Context.User.Id, selected.FirstOrDefault() ?? string.Empty, 1);
            var scene = await BuildSceneAsync(userRepository, zoneRepository, dustRepository, itemRepository, Context.User.Id, null);

            if (scene.Embed is not null)
            {
                await ModifyOriginalResponseAsync(p =>
                {
                    p.Embed = scene.Embed;
                    p.Components = scene.Components ?? new ComponentBuilder().Build();
                });
            }

            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: result.Embed is null);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude hacer la compra, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    private async Task SendSceneAsync(DeepShopScene scene)
    {
        if (scene.Embed is null)
        {
            await FollowupAsync(scene.PlainMessage, ephemeral: true);
        }
        else if (scene.AttachmentPath is { } file)
        {
            await FollowupWithFileAsync(file, embed: scene.Embed, components: scene.Components);
        }
        else
        {
            await FollowupAsync(embed: scene.Embed, components: scene.Components);
        }
    }

    // Lo que ve alguien que todavía no lo conoce: no dice dónde ni cómo (se descubre jugando), solo que existe.
    public const string NotOpenMessage = "🕯️ No sabés de qué hablan. Dicen que, pasado el último guardián del fondo del mapa, hay alguien que vende lo que no se consigue. Primero llegá hasta ahí.";

    private const string NoAccount = "Todavía no tenés cuenta: empezá con **/start**.";

    // La carta: la frase del tabernero, tu Polvo y lo que vende con su precio. null en Embed = todavía no lo conocés (PlainMessage explica). Pública y sin Context para probarla.
    public static async Task<DeepShopScene> BuildSceneAsync(
        IUserRepository userRepository, IZoneRepository zoneRepository, IDustRepository dustRepository, IItemRepository itemRepository, ulong discordId, string? talk, Random? rng = null)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new DeepShopScene(null, null, NoAccount);
        }

        var zones = ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync());
        if (!DeepShop.IsOpen(zones, player))
        {
            return new DeepShopScene(null, null, NotOpenMessage);
        }

        var menu = new SelectMenuBuilder()
            .WithCustomId($"{MenuPrefix}:{discordId}")
            .WithPlaceholder($"Comprar UNA unidad ({GameHistory.Number(DeepShop.PricePerUnit)} de Polvo)")
            .WithMinValues(1)
            .WithMaxValues(1);

        var embed = new EmbedBuilder()
            .WithTitle("🕯️ El Tabernero del Inframundo")
            .WithColor(Color.DarkPurple)
            .WithDescription((talk ?? NpcDialogue.DeepShopkeeper(DeepShopkeeperLine.Greeting, rng)) + $"\n\n✨ Tu Polvo: **{GameHistory.Number(player.Dust)}**");

        foreach (var entry in DeepShop.Stock)
        {
            var item = await itemRepository.GetByNameAsync(entry.ItemName);
            if (item is null)
            {
                continue;
            }

            int have = await dustRepository.GetQuantityAsync(discordId, item.ItemId);
            embed.AddField(
                $"{ItemDisplay.Format(item.Emoji, item.Name)} — {GameHistory.Number(DeepShop.PricePerUnit)} de Polvo",
                $"{entry.UseLine}\nTenés **{GameHistory.Number(have)}**.",
                false);
            menu.AddItemOption(item.Name, item.Name, entry.Key, $"{GameHistory.Number(DeepShop.PricePerUnit)} de Polvo · tenés {GameHistory.Number(have)}");
        }

        embed.WithFooter($"El Polvo sale de /dismantle. Para varias de una: /deepshop item:… cantidad:… (hasta {DeepShop.MaxPerPurchase}).");
        var decorated = NpcImages.Decorate(embed.Build(), NpcImages.DeepShopkeeper);
        return new DeepShopScene(decorated, new ComponentBuilder().WithSelectMenu(menu).Build(), null, NpcImages.AttachmentPathFor(decorated));
    }

    // Compra `quantity` unidades de lo que el jugador pidió (clave, nombre o palabra suelta). Estático y sin Context: "aa deepshop" hace exactamente lo mismo.
    public static async Task<DeepShopResult> ExecuteBuyAsync(
        IUserRepository userRepository, IZoneRepository zoneRepository, IItemRepository itemRepository, IDustRepository dustRepository, IGameEvents gameEvents,
        ulong discordId, string itemText, int quantity, Random? rng = null)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new DeepShopResult(NoAccount, null);
        }

        var zones = ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync());
        if (!DeepShop.IsOpen(zones, player))
        {
            return new DeepShopResult(NotOpenMessage, null);
        }

        if (DeepShop.Find(itemText) is not { } entry)
        {
            return new DeepShopResult(NpcDialogue.DeepShopkeeper(DeepShopkeeperLine.NotFound, rng), null);
        }

        if (quantity < 1 || quantity > DeepShop.MaxPerPurchase)
        {
            return new DeepShopResult($"Se compran de 1 a {DeepShop.MaxPerPurchase} por vez.", null);
        }

        var item = await itemRepository.GetByNameAsync(entry.ItemName);
        if (item is null)
        {
            return new DeepShopResult("El Tabernero del Inframundo no tiene eso en el catálogo todavía.", null);
        }

        int cost = DeepShop.Cost(quantity);
        var outcome = await dustRepository.BuyWithDustAsync(discordId, item.ItemId, quantity, cost);
        switch (outcome.Status)
        {
            case DustSpendStatus.NoAccount:
                return new DeepShopResult(NoAccount, null);
            case DustSpendStatus.NotEnoughDust:
                return new DeepShopResult(
                    NpcDialogue.DeepShopkeeper(DeepShopkeeperLine.NoDust, rng) + $"\n\n**{quantity}×** {ItemDisplay.Format(item.Emoji, item.Name)} cuesta **{GameHistory.Number(cost)}** de Polvo y tenés **{GameHistory.Number(player.Dust)}**.", null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.DeepShopBuy, amount: quantity, detail: item.Name);

        var embed = new EmbedBuilder()
            .WithColor(Color.DarkPurple)
            .WithItemThumbnail(item.Emoji)
            .WithDescription(
                NpcDialogue.DeepShopkeeper(DeepShopkeeperLine.BuySuccess, rng) +
                $"\n\nComprás **{quantity}×** {ItemDisplay.Format(item.Emoji, item.Name)} por **{GameHistory.Number(cost)}** de Polvo.")
            .AddField("✨ Tu Polvo", $"**{GameHistory.Number(outcome.DustAfter)}**", false)
            .Build();
        return new DeepShopResult(null, embed);
    }
}
