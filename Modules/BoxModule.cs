using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /abrir: abrir las cajas del inventario (se compran en /shop o salen de misiones y logros). El botín de cada caja está en la
// base (box_loot), no en el código, y el sorteo es el puro GameData/BoxLoot.cs: acá solo se valida, se sortea, se aplica de forma
// atómica (IBoxRepository.OpenAsync) y se muestra.
public class BoxModule(
    IItemRepository itemRepository,
    IBoxRepository boxRepository,
    ICombatSessionService combatSessions,
    IGameEvents gameEvents) : InteractionModuleBase<SocketInteractionContext>
{
    public const int MaxOpenAtOnce = 10;

    [SlashCommand("abrir", "Abrí cajas de tu inventario (hasta 10 juntas).")]
    public async Task HandleOpenAsync(
        [Summary("caja", "Elegí la caja que querés abrir.")] [Autocomplete(typeof(BoxAutocompleteHandler))] string boxName,
        [Summary("cantidad", "Cuántas abrís (1 a 10, por defecto 1).")] [MinValue(1)] [MaxValue(MaxOpenAtOnce)] int quantity = 1)
    {
        await DeferAsync();

        try
        {
            var result = await ExecuteOpenAsync(itemRepository, boxRepository, combatSessions, gameEvents, Context.User.Id, boxName, quantity);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: result.Embed is null);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude abrir la caja, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Exactamente uno de los dos campos viene con valor (mismo patrón que ShopModule.ShopActionResult).
    public sealed record BoxActionResult(string? PlainMessage, Embed? Embed);

    // Estático (sin Context) para que "aa abrir" comparta exactamente la misma lógica. rng: solo para poder probarlo con un
    // sorteo fijo; en el juego es el generador compartido.
    public static async Task<BoxActionResult> ExecuteOpenAsync(
        IItemRepository itemRepository, IBoxRepository boxRepository, ICombatSessionService combatSessions, IGameEvents gameEvents,
        ulong discordId, string boxName, int quantity, Random? rng = null)
    {
        // Misma regla que la tienda: en plena pelea no se puede parar a abrir cajas.
        if (combatSessions.Peek(discordId) is not null)
        {
            return new BoxActionResult("No podés abrir cajas en medio de un combate. Terminalo (atacando o huyendo) primero.", null);
        }

        if (quantity < 1 || quantity > MaxOpenAtOnce)
        {
            return new BoxActionResult($"Podés abrir de 1 a {MaxOpenAtOnce} cajas por vez.", null);
        }

        var item = await itemRepository.GetByNameAsync(boxName);
        if (item is null || item.Type != "Caja")
        {
            return new BoxActionResult($"**{boxName}** no es una caja. Mirá las que tenés con **/inventory** o comprá en **/shop view**.", null);
        }

        var box = await boxRepository.GetDefinitionAsync(item.ItemId);
        if (box is null)
        {
            return new BoxActionResult($"**{ItemDisplay.Format(item.Emoji, item.Name)}** todavía no tiene botín cargado, avisale al staff.", null);
        }

        rng ??= Random.Shared;
        int totalGold = 0;
        int goldAfter = 0;
        int opened = 0;
        var totalItems = new Dictionary<int, LootedItem>();

        for (int i = 0; i < quantity; i++)
        {
            var loot = BoxLootRoller.Roll(box, rng);
            var outcome = await boxRepository.OpenAsync(discordId, item.ItemId, loot);

            if (outcome is null)
            {
                break; // ya no le quedaban más
            }

            opened++;
            totalGold += loot.Gold;
            goldAfter = outcome.GoldAfter;

            foreach (var looted in loot.Items)
            {
                totalItems[looted.ItemId] = totalItems.TryGetValue(looted.ItemId, out var existing)
                    ? existing with { Quantity = existing.Quantity + looted.Quantity }
                    : looted;
            }
        }

        if (opened == 0)
        {
            return new BoxActionResult($"No tenés **{ItemDisplay.Format(item.Emoji, item.Name)}** en tu inventario.", null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.BoxOpened, amount: opened, detail: item.Name);

        return new BoxActionResult(null, BuildOpenedEmbed(item, opened, quantity, totalGold, totalItems.Values, goldAfter));
    }

    // Público y puro: se prueba sin Discord. Lo mejor (rareza más alta) arriba, y un ✨ en lo Épico o mejor para que se note.
    public static Embed BuildOpenedEmbed(
        Item box, int opened, int requested, int gold, IEnumerable<LootedItem> items, int goldAfter)
    {
        var lines = new List<string>();

        if (gold > 0)
        {
            lines.Add($"💰 **+{gold}** monedas");
        }

        foreach (var looted in items.OrderByDescending(i => RarityCatalog.RankOf(i.Rarity)).ThenBy(i => i.Name))
        {
            string shine = RarityCatalog.RankOf(looted.Rarity) >= RarityCatalog.RankOf("Épico") ? " ✨" : string.Empty;
            lines.Add($"• **{looted.Quantity}×** {ItemDisplay.Format(looted.Emoji, looted.Name)} _({looted.Rarity})_{shine}");
        }

        if (lines.Count == 0)
        {
            lines.Add("_La caja estaba vacía._");
        }

        string title = opened == 1
            ? $"📦 ¡Abriste {ItemDisplay.Format(box.Emoji, box.Name)}!"
            : $"📦 ¡Abriste {opened}× {ItemDisplay.Format(box.Emoji, box.Name)}!";

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(GatheringModule.RarityColor(box.Rarity))
            .WithDescription(string.Join('\n', lines))
            .AddField("💰 Tu oro ahora", goldAfter.ToString(), true);

        if (opened < requested)
        {
            embed.WithFooter($"Pediste {requested} pero solo tenías {opened}.");
        }

        return embed.Build();
    }
}
