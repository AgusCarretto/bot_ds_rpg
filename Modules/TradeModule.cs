using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using static AutocompleteText;

// Resultado de proponer un cambio: un rechazo simple (PlainMessage) o la propuesta con sus botones (Content = la mención del otro).
public sealed record TradeOfferResult(string? PlainMessage, string? Content, Embed? Embed, MessageComponent? Components);

// /trade: cambiar 1 material por 1 de otro material de la MISMA rareza con otro jugador (solo lo que dropean /chop y /mine; reglas en
// GameData/TradeRules.cs). Uno propone, el otro acepta o rechaza con un botón (la propuesta vence a los 2 minutos), y el cambio se
// hace en UNA transacción (ITransferRepository.SwapItemsAsync): o se mueven los dos materiales o no se mueve ninguno.
public class TradeModule(
    IUserRepository userRepository, IItemRepository itemRepository, IInventoryRepository inventoryRepository,
    ITransferRepository transferRepository, ITradeOfferService offers, ICombatSessionService combatSessions, IGameEvents gameEvents)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("trade", "Cambiá 1 material por 1 de otro material de la misma rareza con otro jugador.")]
    public async Task HandleTradeAsync(
        [Summary("player", "Con quién querés cambiar.")] IUser player,
        [Summary("give", "El material que das (1 unidad).")] [Autocomplete(typeof(TradeGiveAutocompleteHandler))] string give,
        [Summary("get", "El material que querés a cambio (misma rareza).")] [Autocomplete(typeof(TradeGetAutocompleteHandler))] string get)
    {
        await DeferAsync();

        try
        {
            var result = await ExecuteOfferAsync(
                userRepository, itemRepository, inventoryRepository, offers, combatSessions, Context.User.Id, player, give, get);
            await FollowupAsync(result.PlainMessage ?? result.Content, embed: result.Embed, components: result.Components, ephemeral: result.Embed is null);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude armar el cambio, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction("trade_accept:*")]
    public async Task HandleAcceptAsync(string offerIdRaw)
    {
        await DeferAsync();

        try
        {
            if (!Guid.TryParse(offerIdRaw, out var offerId) || offers.Peek(offerId) is not { } offer)
            {
                await FollowupAsync("Esa propuesta ya venció o ya se resolvió.", ephemeral: true);
                return;
            }

            if (Context.User.Id != offer.ToId)
            {
                await FollowupAsync("Esta propuesta no es para vos.", ephemeral: true);
                return;
            }

            // Sacarla de forma atómica: si dos clicks llegan a la vez, solo uno la recibe y el cambio no se hace dos veces.
            if (offers.TryTake(offerId) is not { } taken)
            {
                await FollowupAsync("Esa propuesta ya venció o ya se resolvió.", ephemeral: true);
                return;
            }

            var status = await transferRepository.SwapItemsAsync(taken.FromId, taken.GiveItemId, taken.ToId, taken.GetItemId);
            if (status == SwapStatus.Ok)
            {
                await gameEvents.RecordAsync(taken.FromId, GameEventKinds.Trade, detail: $"{taken.GiveName}->{taken.GetName}");
                await gameEvents.RecordAsync(taken.ToId, GameEventKinds.Trade, detail: $"{taken.GetName}->{taken.GiveName}");
            }

            var embed = BuildResultEmbed(taken, status);
            await ModifyOriginalResponseAsync(p =>
            {
                p.Content = string.Empty;
                p.Embed = embed;
                p.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude hacer el cambio, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Rechazar (el que recibe) o cancelar (el que propuso).
    [ComponentInteraction("trade_decline:*")]
    public async Task HandleDeclineAsync(string offerIdRaw)
    {
        await DeferAsync();

        try
        {
            if (!Guid.TryParse(offerIdRaw, out var offerId) || offers.Peek(offerId) is not { } offer)
            {
                await FollowupAsync("Esa propuesta ya venció o ya se resolvió.", ephemeral: true);
                return;
            }

            if (Context.User.Id != offer.ToId && Context.User.Id != offer.FromId)
            {
                await FollowupAsync("Esta propuesta no es tuya.", ephemeral: true);
                return;
            }

            if (offers.TryTake(offerId) is null)
            {
                await FollowupAsync("Esa propuesta ya venció o ya se resolvió.", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("❌ Cambio cancelado")
                .WithDescription($"{MentionUtils.MentionUser(Context.User.Id)} canceló el cambio de **{offer.GiveName}** por **{offer.GetName}**.")
                .WithColor(Color.DarkGrey)
                .Build();
            await ModifyOriginalResponseAsync(p =>
            {
                p.Content = string.Empty;
                p.Embed = embed;
                p.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude cancelar el cambio, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // ---- Lógica compartida con "aa trade" (sin Context) ----

    public static async Task<TradeOfferResult> ExecuteOfferAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IInventoryRepository inventoryRepository, ITradeOfferService offers,
        ICombatSessionService combatSessions, ulong fromId, IUser target, string giveName, string getName)
    {
        TradeOfferResult Reject(string message) => new(message, null, null, null);

        if (target.IsBot)
        {
            return Reject("Los bots no cambian materiales 🤖");
        }

        if (target.Id == fromId)
        {
            return Reject("No podés cambiar con vos mismo.");
        }

        if (combatSessions.Peek(fromId) is not null)
        {
            return Reject("No podés armar un cambio en medio de un combate. Terminalo (atacando o huyendo) primero.");
        }

        if (await userRepository.GetByDiscordIdAsync(target.Id) is null)
        {
            return Reject($"{target.Mention} todavía no empezó a jugar: que use **/start** y después cambian.");
        }

        var give = await itemRepository.GetByNameAsync(giveName);
        if (give is null)
        {
            return Reject($"No encontré ningún material llamado **{giveName.Trim()}**.");
        }

        var get = await itemRepository.GetByNameAsync(getName);
        if (get is null)
        {
            return Reject($"No encontré ningún material llamado **{getName.Trim()}**.");
        }

        if (TradeRules.Validate(give, get) is { } broken)
        {
            return Reject(broken);
        }

        if (await inventoryRepository.GetQuantityAsync(fromId, give.ItemId) < 1)
        {
            return Reject($"No tenés **{ItemDisplay.Format(give.Emoji, give.Name)}** para dar.");
        }

        if (await inventoryRepository.GetQuantityAsync(target.Id, get.ItemId) < 1)
        {
            return Reject($"{target.Mention} no tiene **{ItemDisplay.Format(get.Emoji, get.Name)}** para cambiarte.");
        }

        var offer = offers.Create(fromId, target.Id, give, get);

        var embed = new EmbedBuilder()
            .WithTitle("🔄 Propuesta de cambio")
            .WithColor(Color.Blue)
            .WithDescription(
                $"{MentionUtils.MentionUser(fromId)} te propone cambiar **1× {ItemDisplay.Format(give.Emoji, give.Name)}** por " +
                $"**1× {ItemDisplay.Format(get.Emoji, get.Name)}** (los dos son {give.Rarity}).")
            .WithFooter($"{target.Username}: tenés {(int)TradeOfferService.Lifetime.TotalMinutes} minutos para aceptar.")
            .Build();

        var buttons = new ComponentBuilder()
            .WithButton("Aceptar", $"trade_accept:{offer.Id}", ButtonStyle.Success, new Emoji("✅"))
            .WithButton("Rechazar", $"trade_decline:{offer.Id}", ButtonStyle.Danger, new Emoji("❌"))
            .Build();

        return new TradeOfferResult(null, target.Mention, embed, buttons);
    }

    public static Embed BuildResultEmbed(TradeOffer offer, SwapStatus status)
    {
        string from = MentionUtils.MentionUser(offer.FromId);
        string to = MentionUtils.MentionUser(offer.ToId);

        return status switch
        {
            SwapStatus.Ok => new EmbedBuilder()
                .WithTitle("✅ ¡Cambio hecho!")
                .WithColor(Color.Green)
                .WithDescription($"{from} dio **1× {offer.GiveName}** y {to} dio **1× {offer.GetName}**.")
                .Build(),
            SwapStatus.FirstMissing => Failed($"{from} ya no tiene **{offer.GiveName}**."),
            _ => Failed($"{to} ya no tiene **{offer.GetName}**."),
        };

        static Embed Failed(string why) => new EmbedBuilder().WithTitle("⚠️ No se pudo cambiar").WithColor(Color.Red).WithDescription(why).Build();
    }
}

// Listas desplegables de /trade. "give": los materiales de recolección que el jugador TIENE. "get": todos los materiales de recolección
// con su rareza (la regla de la misma rareza la valida el comando, con un mensaje claro).
public static class TradeChoices
{
    public static IReadOnlyList<AutocompleteResult> ForGive(IEnumerable<InventoryEntry> inventory, string typed) =>
        inventory
            .Where(e => e.Type is "Madera" or "Mineral" && e.Quantity > 0 && AutocompleteText.FitsAsValue(e.ItemName) && Matches(e.ItemName, typed))
            .OrderBy(e => Relevance(e.ItemName, typed))
            .ThenBy(e => RarityCatalog.RankOf(e.Rarity))
            .ThenBy(e => e.ItemName, StringComparer.Ordinal)
            .Take(MaxChoices)
            .Select(e => new AutocompleteResult(Truncate($"{e.ItemName} — tenés {e.Quantity} ({e.Rarity})"), e.ItemName))
            .ToList();

    public static IReadOnlyList<AutocompleteResult> ForGet(IEnumerable<Item> gatherables, string typed) =>
        gatherables
            .Where(i => TradeRules.IsTradeable(i) && AutocompleteText.FitsAsValue(i.Name) && Matches(i.Name, typed))
            .OrderBy(i => Relevance(i.Name, typed))
            .ThenBy(i => RarityCatalog.RankOf(i.Rarity))
            .ThenBy(i => i.Name, StringComparer.Ordinal)
            .Take(MaxChoices)
            .Select(i => new AutocompleteResult(Truncate($"{i.Name} ({i.Rarity})"), i.Name))
            .ToList();
}

public sealed class TradeGiveAutocompleteHandler : SafeAutocompleteHandler
{
    protected override async Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services) =>
        TradeChoices.ForGive(await services.GetRequiredService<IInventoryRepository>().GetByDiscordIdAsync(userId), typed);
}

public sealed class TradeGetAutocompleteHandler : SafeAutocompleteHandler
{
    protected override async Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services)
    {
        var items = services.GetRequiredService<IItemRepository>();
        var all = (await items.GetAllByTypeAsync("Madera")).Concat(await items.GetAllByTypeAsync("Mineral"));
        return TradeChoices.ForGet(all, typed);
    }
}
