using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

// /exchange (aa exchange, y la lista "Cambiar drops" de la taberna): el tabernero te cambia 3 drops de una zona por 1 DISTINTO de la MISMA zona (GameData/DropExchange.cs), para
// compensar la mala racha ("tengo de más de uno y me falta el otro"). Las reglas están en el repositorio (una transacción con guarda), esto es la cara del comando: pedir, contestar
// con la voz del tabernero y registrar el evento. La taberna llama exactamente a ExecuteExchangeAsync (con 1 cambio), así que valida y cobra igual que el comando.
public class ExchangeModule(IUserRepository userRepository, IItemRepository itemRepository, IDropExchangeRepository exchangeRepository, IGameEvents gameEvents)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("exchange", "El tabernero te cambia 3 drops de una zona por 1 de la misma zona.")]
    public async Task HandleExchangeAsync(
        [Summary("dar", "El drop que entregás, de a 3 por cambio. Elegí de la lista.")] [Autocomplete(typeof(ExchangeGiveAutocompleteHandler))] string give,
        [Summary("recibir", "El drop de la MISMA zona que querés recibir, de a 1 por cambio.")] [Autocomplete(typeof(ExchangeGetAutocompleteHandler))] string get,
        [Summary("veces", "Cuántos cambios hacés juntos (cada uno es 3 por 1). Por defecto 1.")] [MinValue(1)] [MaxValue(DropExchange.MaxTimes)] int times = 1)
    {
        await DeferAsync();

        try
        {
            var result = await ExecuteExchangeAsync(userRepository, itemRepository, exchangeRepository, gameEvents, Context.User.Id, give, get, times);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: result.Embed is null);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! El tabernero no pudo hacer el cambio ahora, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Exactamente uno de los dos campos viene con valor (mismo patrón que DustModule.DustResult).
    public sealed record ExchangeResult(string? PlainMessage, Embed? Embed);

    // Público y sin Context: lo usan /exchange, "aa exchange" y la lista de la taberna.
    public static async Task<ExchangeResult> ExecuteExchangeAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IDropExchangeRepository exchangeRepository, IGameEvents gameEvents,
        ulong discordId, string giveName, string getName, int times)
    {
        if (await userRepository.GetByDiscordIdAsync(discordId) is null)
        {
            return new ExchangeResult("Todavía no tenés cuenta: empezá con **/start**.", null);
        }

        if (times < 1 || times > DropExchange.MaxTimes)
        {
            return new ExchangeResult($"Se hacen de 1 a {DropExchange.MaxTimes} cambios por vez (cada uno es {DropExchange.GiveAmount} por {DropExchange.GetAmount}).", null);
        }

        var give = await itemRepository.GetByNameAsync(giveName);
        var get = await itemRepository.GetByNameAsync(getName);
        if (give is null || get is null)
        {
            return new ExchangeResult($"No conozco **{(give is null ? giveName : getName).Trim()}**: elegilo de la lista.", null);
        }

        var outcome = await exchangeRepository.ExchangeAsync(discordId, give.ItemId, get.ItemId, times);
        int giveTotal = DropExchange.GiveAmount * times;

        switch (outcome.Status)
        {
            case ExchangeStatus.NoAccount:
                return new ExchangeResult("Todavía no tenés cuenta: empezá con **/start**.", null);
            case ExchangeStatus.SameItem:
                return new ExchangeResult("Entregás y recibís el mismo ítem: elegí un drop **distinto** de la misma zona.", null);
            case ExchangeStatus.NotExchangeable:
                return new ExchangeResult("El tabernero solo cambia **drops de monstruos** (los de cacería y de viaje): no madera ni minerales, ni cajas.", null);
            case ExchangeStatus.DifferentZones:
            {
                var zones = (await exchangeRepository.GetExchangeableDropsAsync()).ToDictionary(d => d.ItemId, d => d.ZoneName);
                return new ExchangeResult(
                    $"**{give.Name}** es de {zones.GetValueOrDefault(give.ItemId, "otra zona")} y **{get.Name}** de {zones.GetValueOrDefault(get.ItemId, "otra zona")}: el cambio es dentro de la **misma** zona.", null);
            }

            case ExchangeStatus.NotEnough:
                return new ExchangeResult(
                    $"Para {times} cambio{(times == 1 ? string.Empty : "s")} hacen falta **{giveTotal}** de **{give.Name}** y tenés **{outcome.GiveLeft}** ({DropExchange.GiveAmount} por cada {DropExchange.GetAmount} que recibís).", null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.DropExchange, amount: times, detail: $"{give.Name}->{get.Name}");

        var embed = new EmbedBuilder()
            .WithTitle("🔁 ¡Cambio hecho!")
            .WithColor(Color.Gold)
            .WithItemThumbnail(get.Emoji)
            .WithDescription(
                $"{NpcDialogue.Shopkeeper(ShopkeeperLine.Exchange)}\n\n" +
                $"Le diste **{giveTotal}×** {ItemDisplay.Format(give.Emoji, give.Name)} y te llevás **{DropExchange.GetAmount * times}×** {ItemDisplay.Format(get.Emoji, get.Name)}.")
            .AddField("🎒 Te quedan", $"**{GameHistory.Number(outcome.GiveLeft)}** de {give.Name}", false)
            .AddField("🎁 Ahora tenés", $"**{GameHistory.Number(outcome.GetNow)}** de {get.Name}", false)
            .Build();
        return new ExchangeResult(null, embed);
    }
}

// Las listas del trueque, puras (sin Discord ni base): se prueban con datos armados a mano.
public static class ExchangeChoices
{
    // Los drops que el jugador PUEDE entregar ahora (tiene al menos 3), con cuántos tiene, por zona y nombre.
    public static IReadOnlyList<(ExchangeDrop Drop, int Have)> Givable(IEnumerable<InventoryEntry> inventory, IReadOnlyList<ExchangeDrop> drops)
    {
        var owned = inventory.Where(e => e.Quantity >= DropExchange.GiveAmount).ToDictionary(e => e.ItemName, e => e.Quantity, StringComparer.Ordinal);
        return drops.Where(d => owned.ContainsKey(d.Name)).Select(d => (d, owned[d.Name])).OrderBy(x => x.d.MinLevel).ThenBy(x => x.d.Name, StringComparer.Ordinal).ToList();
    }

    // La lista de "dar": lo que tiene al menos 3 y se puede escribir.
    public static IReadOnlyList<AutocompleteResult> ForGive(IEnumerable<InventoryEntry> inventory, IReadOnlyList<ExchangeDrop> drops, string typed) =>
        Givable(inventory, drops)
            .Where(x => AutocompleteText.FitsAsValue(x.Drop.Name) && AutocompleteText.Matches(x.Drop.Name, typed))
            .OrderBy(x => AutocompleteText.Relevance(x.Drop.Name, typed))
            .Take(AutocompleteText.MaxChoices)
            .Select(x => new AutocompleteResult(AutocompleteText.Truncate($"{x.Drop.Name} — tenés {x.Have} · {x.Drop.ZoneName}"), x.Drop.Name))
            .ToList();

    // Lo que se puede recibir a cambio de "giveName": los OTROS drops de su misma zona. Si todavía no eligió qué da (o no es un drop), todos los drops con su zona.
    public static IReadOnlyList<ExchangeDrop> Receivable(IReadOnlyList<ExchangeDrop> drops, string? giveName)
    {
        var give = string.IsNullOrWhiteSpace(giveName) ? null : drops.FirstOrDefault(d => AutocompleteText.SameName(d.Name, giveName));
        return give is null ? drops : drops.Where(d => d.ZoneId == give.ZoneId && d.ItemId != give.ItemId).ToList();
    }

    public static IReadOnlyList<AutocompleteResult> ForGet(IEnumerable<InventoryEntry> inventory, IReadOnlyList<ExchangeDrop> drops, string? giveName, string typed)
    {
        var have = inventory.ToDictionary(e => e.ItemName, e => e.Quantity, StringComparer.Ordinal);
        return Receivable(drops, giveName)
            .Where(d => AutocompleteText.FitsAsValue(d.Name) && AutocompleteText.Matches(d.Name, typed))
            .OrderBy(d => AutocompleteText.Relevance(d.Name, typed))
            .Take(AutocompleteText.MaxChoices)
            .Select(d => new AutocompleteResult(AutocompleteText.Truncate($"{d.Name} — tenés {have.GetValueOrDefault(d.Name)} · {d.ZoneName}"), d.Name))
            .ToList();
    }
}

public sealed class ExchangeGiveAutocompleteHandler : SafeAutocompleteHandler
{
    protected override async Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services)
    {
        var inventory = await services.GetRequiredService<IInventoryRepository>().GetByDiscordIdAsync(userId);
        var drops = await services.GetRequiredService<IDropExchangeRepository>().GetExchangeableDropsAsync();
        return ExchangeChoices.ForGive(inventory, drops, typed);
    }
}

// La lista de "recibir" depende de lo que ya elegiste en "dar" (los otros drops de esa zona), así que mira esa otra opción además de lo que se está escribiendo.
// Misma red de seguridad que SafeAutocompleteHandler: si algo falla se registra y la lista queda vacía.
public sealed class ExchangeGetAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        try
        {
            string typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;
            string? give = autocompleteInteraction.Data.Options.FirstOrDefault(o => o.Name == "dar")?.Value?.ToString();

            var inventory = await services.GetRequiredService<IInventoryRepository>().GetByDiscordIdAsync(context.User.Id);
            var drops = await services.GetRequiredService<IDropExchangeRepository>().GetExchangeableDropsAsync();
            return AutocompletionResult.FromSuccess(ExchangeChoices.ForGet(inventory, drops, give, typed));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            return AutocompletionResult.FromSuccess([]);
        }
    }
}
