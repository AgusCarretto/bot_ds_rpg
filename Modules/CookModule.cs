using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /cook (aa cook, aa cook recipes) — la cocina (v0.49.0, diseño: docs/superpowers/specs/2026-11-05-cocina-design.md). Un plato se cocina y se come en el momento; los permanentes suman su stat y se apilan hasta el
// próximo Fuego Nuevo (GameData/CookingCatalog.cs). Las reglas y el descuento están en CookingRepository (una transacción); esto es la cara del comando: pedir, contestar y registrar el evento. El comando de texto
// llama exactamente a ExecuteCookAsync y a BuildRecipesEmbedAsync.
public sealed record CookReply(string? PlainMessage, Embed? Embed);

public class CookModule(ICookingRepository cooking, IInventoryRepository inventory, IGameEvents gameEvents) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("cook", "Cociná un plato con tus pescados y materiales (sin plato, mirá las recetas).")]
    public async Task HandleAsync(
        [Summary("plato", "Qué cocinar (si lo dejás vacío ves las recetas).")] [Autocomplete(typeof(CookDishAutocompleteHandler))] string? plato = null,
        [Summary("veces", "Cuántos platos (de 1 a 10).")] [MinValue(1)] [MaxValue(CookingCatalog.MaxTimes)] int veces = 1)
    {
        await DeferAsync();

        try
        {
            if (string.IsNullOrWhiteSpace(plato))
            {
                await FollowupAsync(embed: await BuildRecipesEmbedAsync(inventory, cooking, Context.User.Id));
                return;
            }

            var reply = await ExecuteCookAsync(cooking, gameEvents, Context.User.Id, plato, veces);
            await FollowupAsync(reply.PlainMessage, embed: reply.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude cocinar ahora mismo, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // La cocina entera de un pedido: busca el plato, cocina `times` veces y arma la respuesta (o dice qué falta). Estático y sin Discord: lo usa también «aa cook».
    public static async Task<CookReply> ExecuteCookAsync(ICookingRepository cooking, IGameEvents events, ulong discordId, string dishText, int times)
    {
        var dish = CookingCatalog.Find(dishText);
        if (dish is null)
        {
            return new CookReply(Loc.T("No conozco ese plato: mirá las recetas con **aa cook recipes** (o **/cook** sin plato)."), null);
        }

        times = Math.Clamp(times, 1, CookingCatalog.MaxTimes);
        var outcome = await cooking.CookAsync(discordId, dish, times);
        switch (outcome.Status)
        {
            case CookStatus.NoAccount:
                return new CookReply(Loc.T("Primero creá tu personaje con **/start**."), null);

            case CookStatus.Missing:
                string missing = string.Join(" · ", outcome.Missing.Select(m => $"{ItemEmojiRegistry.Label(m.ItemName, string.Empty)} {m.Have}/{m.Need}"));
                return new CookReply(Loc.F($"Te falta para cocinar {times}× **{dish.Name}**: {missing}"), null);
        }

        await events.RecordAsync(discordId, GameEventKinds.Cook, null, times, dish.Key);

        var embed = new EmbedBuilder()
            .WithTitle($"{dish.Icon} {dish.Name}")
            .WithColor(Color.Orange)
            .WithDescription(
                Loc.F($"Cocinaste y te comiste **{times}×** {dish.Name}: {CookingCatalog.EffectLine(dish, times)}.") + "\n\n" +
                Loc.F($"Llevás **{outcome.NewCount}** de este plato en esta vuelta: **{CookingCatalog.EffectLine(dish, outcome.NewCount)}** en total, hasta el próximo Fuego Nuevo."))
            .WithFooter(Loc.T("Solo cuenta contra monstruos"));
        return new CookReply(null, embed.Build());
    }

    // Las recetas: cada plato con su efecto y los ingredientes con lo que tenés y lo que pide (✅/❌), el combustible aparte, y lo que ya cocinaste esta vuelta.
    public static async Task<Embed> BuildRecipesEmbedAsync(IInventoryRepository inventory, ICookingRepository cooking, ulong discordId)
    {
        var have = (await inventory.GetByDiscordIdAsync(discordId)).GroupBy(e => e.ItemName).ToDictionary(g => g.Key, g => g.Sum(e => e.Quantity), StringComparer.Ordinal);
        var cooked = await cooking.GetCookedAsync(discordId);

        string Line(DishIngredient ing)
        {
            int owned = have.GetValueOrDefault(ing.ItemName);
            return $"{(owned >= ing.Quantity ? "✅" : "❌")} {ItemEmojiRegistry.Label(ing.ItemName, string.Empty)} {owned}/{ing.Quantity}";
        }

        var embed = new EmbedBuilder()
            .WithTitle(Loc.T("🍳 La cocina"))
            .WithColor(Color.Orange)
            .WithFooter(Loc.T("Los platos duran hasta el próximo Fuego Nuevo y solo cuentan contra monstruos"));

        int hp = CookingCatalog.Sum(cooked, d => d.MaxHp), def = CookingCatalog.Sum(cooked, d => d.Defense), atk = CookingCatalog.Sum(cooked, d => d.Attack);
        string total = string.Join(" · ", new[] { hp > 0 ? Loc.F($"+{hp} de vida máxima") : null, def > 0 ? Loc.F($"+{def} de DEF") : null, atk > 0 ? Loc.F($"+{atk} de ATQ") : null }.Where(t => t is not null));
        embed.WithDescription(
            Loc.T("Cada plato se **cocina y se come en el momento**: **/cook** plato:… (o **aa cook <plato> [veces]**). Se apilan: cuantos más cocinás, más sumás.") +
            (total.Length == 0 ? string.Empty : "\n\n" + Loc.F($"Llevás cocinado en esta vuelta: **{total}**.")));

        foreach (var dish in CookingCatalog.All)
        {
            int count = cooked.GetValueOrDefault(dish.Key);
            string lines = string.Join("\n", dish.Ingredients.Select(Line)) + "\n" +
                Loc.F($"🔥 Combustible: {Line(new DishIngredient(CookingCatalog.FuelItemName, dish.Fuel))}") +
                (count > 0 ? "\n" + Loc.F($"🍽️ Llevás {count} cocinados") : string.Empty);
            embed.AddField($"{dish.Icon} {dish.Name} · {CookingCatalog.EffectLine(dish)}", lines, false);
        }

        return embed.Build();
    }
}

// La lista de platos del autocompletado de /cook: el nombre y lo que suma, sin tocar la base (los platos son datos del código).
public sealed class CookDishAutocompleteHandler : SafeAutocompleteHandler
{
    protected override Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services)
    {
        IReadOnlyList<AutocompleteResult> choices = CookingCatalog.All
            .Where(d => AutocompleteText.Matches(d.Name, typed))
            .OrderBy(d => AutocompleteText.Relevance(d.Name, typed))
            .Select(d => new AutocompleteResult(AutocompleteText.Truncate($"{d.Name} ({CookingCatalog.EffectLine(d)})"), d.Name))
            .Take(AutocompleteText.MaxChoices)
            .ToList();
        return Task.FromResult(choices);
    }
}
