using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using static AutocompleteText;

// Las expediciones de las mascotas (v0.39.0, reglas en GameData/PetExpeditions.cs): una mascota de NIVEL 10 sale por 1, 4 u 8 horas (cuesta 2 / 10 / 25 Comidas para Mascotas) y, cuando vuelve, el mismo comando
// (/pet expedition, o «aa pet expedition») recoge el premio: un oro del rango de esa duración y un % de tu nivel en EXP —igual para todos, sin importar la zona— más, con chance, un hallazgo de su especie.
// De a una por jugador. Todo el armado es estático y sin Context (como el resto de PetModule) para que el comando de texto, el slash y los componentes hagan exactamente lo mismo; salir y volver son
// transacciones guardadas en IPetExpeditionRepository.
public partial class PetModule
{
    [SlashCommand("expedition", "Mandá a una mascota nivel 10 de expedición (1, 4 u 8 h) y recogé el premio cuando vuelva.")]
    public Task HandleExpeditionAsync(
        [Summary("mascota", "Cuál mandás. Sin elegir, te muestro las opciones.")] [Autocomplete(typeof(ExpeditionPetAutocompleteHandler))] string? pet = null,
        [Summary("duracion", "Cuántas horas: más tiempo, más premio y más chance de hallazgo.")] [Choice("1 hora", 1)] [Choice("4 horas", 4)] [Choice("8 horas", 8)] int? hours = null) =>
        RunAsync(() => ExecuteExpeditionAsync(userRepository, petRepository, expeditionRepository, inventoryRepository, gameEvents, Context.User.Id, pet, hours), ephemeral: false);

    // Lo que se ve al entrar sin parámetros y sin nada en curso: qué pasa en cada duración y el menú para elegir mascota y horas.
    public sealed record ExpeditionMenuData(int Food, IReadOnlyList<OwnedPet> Eligible, IReadOnlyList<OwnedPet> TooLow);

    // «aa pet expedition [mascota] [horas]» y /pet expedition. Con una expedición en curso: si ya volvió la cobra, si no dice cuánto falta. Sin nada en curso: si vienen la mascota y las horas, sale; si no, muestra el menú.
    public static async Task<PetResult> ExecuteExpeditionAsync(
        IUserRepository userRepository, IPetRepository petRepository, IPetExpeditionRepository expeditionRepository, IInventoryRepository inventoryRepository,
        IGameEvents gameEvents, ulong discordId, string? petName, int? hours, Random? rng = null)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new PetResult(NoAccount, null);
        }

        var active = await expeditionRepository.GetAsync(discordId);
        if (active is not null)
        {
            return active.IsReady
                ? await ClaimExpeditionAsync(expeditionRepository, gameEvents, player, rng)
                : new PetResult(null, BuildOutEmbed(active));
        }

        var owned = await petRepository.GetOwnedAsync(discordId);
        if (owned.Count == 0)
        {
            return new PetResult(NoPets, null);
        }

        if (!string.IsNullOrWhiteSpace(petName) && hours is { } chosenHours)
        {
            var match = owned.FirstOrDefault(p => SameName(p.Species.Name, petName!));
            if (match is null)
            {
                return new PetResult($"No tenés una mascota que se llame **{petName!.Trim()}**. Mirá las tuyas con **/pet view**.", null);
            }

            return await StartExpeditionAsync(expeditionRepository, gameEvents, player, match.Species.SpeciesId, chosenHours);
        }

        var inventory = await inventoryRepository.GetByDiscordIdAsync(discordId);
        int food = inventory.Where(e => e.ItemName == PetRules.FoodItemName).Sum(e => e.Quantity);
        var data = new ExpeditionMenuData(food, owned.Where(PetExpeditions.IsEligible).ToList(), owned.Where(p => !PetExpeditions.IsEligible(p)).ToList());
        if (data.Eligible.Count == 0)
        {
            return new PetResult(NoEligibleText(data), null);
        }

        return new PetResult(null, BuildExpeditionMenuEmbed(data), BuildExpeditionMenu(discordId, data));
    }

    // Manda a la mascota (la usan el comando y el menú desplegable). Devuelve la pantalla de salida o el motivo del rechazo.
    public static async Task<PetResult> StartExpeditionAsync(
        IPetExpeditionRepository expeditionRepository, IGameEvents gameEvents, User player, int speciesId, int hours)
    {
        ulong discordId = (ulong)player.DiscordId;
        var option = PetExpeditions.OptionFor(hours);
        if (option is null)
        {
            return new PetResult($"Las expediciones duran {string.Join(", ", PetExpeditions.Options.Select(o => o.Hours.ToString()))} horas: elegí una de esas.", null);
        }

        var outcome = await expeditionRepository.StartAsync(discordId, speciesId, hours);

        switch (outcome.Status)
        {
            case ExpeditionStartStatus.Ok:
                break;
            case ExpeditionStartStatus.BadDuration:
                return new PetResult($"Las expediciones duran {string.Join(", ", PetExpeditions.Options.Select(o => o.Hours.ToString()))} horas: elegí una de esas.", null);
            case ExpeditionStartStatus.AlreadyOut:
                return outcome.Expedition is { } already
                    ? new PetResult(null, BuildOutEmbed(already))
                    : new PetResult("Ya tenés una mascota de expedición: mirá cuándo vuelve con **/pet expedition**.", null);
            case ExpeditionStartStatus.NotOwned:
                return new PetResult(NoPets, null);
            case ExpeditionStartStatus.NotMaxLevel:
                return new PetResult($"**{outcome.Species!.Name}** todavía no está en el nivel **{PetExpeditions.RequiredLevel}**: las expediciones son para las mascotas al máximo. Alimentala con **/pet feed**.", null);
            case ExpeditionStartStatus.NoFood:
                return new PetResult(
                    $"{ItemEmojiRegistry.IconOf(PetRules.FoodItemName, "🦴")} Una expedición de {PetExpeditions.HoursText(hours)} cuesta **{option.FoodCost}** {PetRules.FoodItemName} y tenés **{outcome.FoodHave}**: se compra en la **/taberna**.", null);
            default:
                return new PetResult(NoAccount, null);
        }

        var started = outcome.Expedition!;
        await gameEvents.RecordAsync(discordId, GameEventKinds.PetExpeditionStart, player.CurrentZoneId, hours, started.Species.Name);

        var embed = new EmbedBuilder()
            .WithTitle($"🧭 {started.Species.Emoji} {started.Species.Name} salió de expedición")
            .WithColor(Color.Teal)
            // A propósito NO dice qué va a traer (ni el oro, ni la EXP, ni el hallazgo de la especie): el dueño quiere que se descubra jugando.
            .WithDescription(
                $"Va por **{PetExpeditions.HoursText(hours)}** y vuelve {FormatWhen(started.EndsUtc)}.\n" +
                $"Gastó **{option.FoodCost}** {PetRules.FoodItemName} (te quedan **{outcome.FoodHave}**).\n\n" +
                "Cuando vuelva, recogé lo que trajo con **/pet expedition**.")
            .WithFooter("Te aviso cuando vuelva si tenés los avisos prendidos (/reminders).")
            .Build();
        return new PetResult(null, embed);
    }

    // Cobra la expedición que ya volvió y cuenta qué trajo.
    public static async Task<PetResult> ClaimExpeditionAsync(
        IPetExpeditionRepository expeditionRepository, IGameEvents gameEvents, User player, Random? rng = null)
    {
        ulong discordId = (ulong)player.DiscordId;
        var outcome = await expeditionRepository.ClaimAsync(discordId, rng);

        switch (outcome.Status)
        {
            case ExpeditionClaimStatus.Ok:
                break;
            case ExpeditionClaimStatus.NotReady:
                return outcome.Expedition is { } waiting
                    ? new PetResult(null, BuildOutEmbed(waiting))
                    : new PetResult("Tu mascota todavía no volvió: mirá cuándo con **/pet expedition**.", null);
            case ExpeditionClaimStatus.NoExpedition:
                return new PetResult("No tenés ninguna mascota de expedición (o ya recogiste el premio). Mandá una con **/pet expedition**.", null);
            default:
                return new PetResult(NoAccount, null);
        }

        var done = outcome.Expedition!;
        var receipt = outcome.Receipt!;
        await gameEvents.RecordAsync(discordId, GameEventKinds.PetExpeditionClaim, player.CurrentZoneId, done.Hours, outcome.Find is null ? done.Species.Name : done.Species.Name + ":hallazgo");
        if (receipt.LevelsGained > 0)
        {
            await gameEvents.RecordAsync(discordId, GameEventKinds.LevelUp, player.CurrentZoneId, receipt.LevelsGained, receipt.NewLevel.ToString());
        }

        var lines = new List<string>
        {
            $"Estuvo **{PetExpeditions.HoursText(done.Hours)}** afuera y trajo: {MissionRewards.Describe(receipt.Reward)}",
        };
        if (outcome.Find is { } find)
        {
            lines.Add(FindResultText(find));
        }
        else
        {
            lines.Add("No encontró nada especial esta vez.");
        }

        var embed = new EmbedBuilder()
            .WithTitle($"🎉 {done.Species.Emoji} {done.Species.Name} volvió de la expedición")
            .WithColor(outcome.Find is null ? Color.Teal : Color.Gold)
            .WithDescription(string.Join("\n\n", lines))
            .WithFooter("Mandala de nuevo con /pet expedition.")
            .Build();
        return new PetResult(null, embed);
    }

    // Lo que dice el hallazgo, ya con las cantidades que salieron.
    public static string FindResultText(ExpeditionFindResult find)
    {
        string items = string.Join(", ", find.Items.Select(i => $"{ItemEmojiRegistry.IconOf(i.ItemName, string.Empty)} {i.Quantity}× **{i.ItemName}**".Trim()));
        return find.Kind switch
        {
            ExpeditionFindKind.GoldBag => $"✨ ¡Encontró una bolsa extra! El **oro** de la expedición subió un {PetExpeditions.FindBonusPercent} %.",
            ExpeditionFindKind.XpBoost => $"✨ ¡Tuvo una inspiración! La **EXP** de la expedición subió un {PetExpeditions.FindBonusPercent} %.",
            ExpeditionFindKind.Dust => $"✨ ¡Encontró **{find.Amount}** de Polvo!",
            ExpeditionFindKind.HuntDrops => find.Items.Count > 0 ? $"✨ ¡Encontró un drop de cacería!: {items}" : "✨ Encontró algo, pero tu zona no tiene drops cargados.",
            ExpeditionFindKind.Gather => find.Items.Count > 0 ? $"✨ ¡Volvió con materiales!: {items}" : "✨ Volvió con las manos vacías de materiales.",
            _ => string.Empty,
        };
    }

    // «<t:1234:R>» — Discord lo muestra como «en 3 horas» en el reloj de quien lo lee.
    public static string FormatWhen(DateTime utc) => $"<t:{new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds()}:R>";

    // La mascota está afuera: a qué hora vuelve.
    public static Embed BuildOutEmbed(ActiveExpedition expedition) =>
        new EmbedBuilder()
            .WithTitle($"🧭 {expedition.Species.Emoji} {expedition.Species.Name} está de expedición")
            .WithColor(Color.Teal)
            .WithDescription(expedition.IsReady
                ? "¡Ya volvió! Recogé el premio con **/pet expedition**."
                : $"Salió por **{PetExpeditions.HoursText(expedition.Hours)}** y vuelve {FormatWhen(expedition.EndsUtc)} (en **{TimeFormat.Remaining(expedition.Remaining)}**).\n" +
                  "Cuando vuelva, recogé el premio con **/pet expedition**. De a una mascota por vez.")
            .Build();

    private static string NoEligibleText(ExpeditionMenuData data)
    {
        string levels = string.Join(", ", data.TooLow.Select(p => $"{p.Species.Name} nivel {PetRules.LevelFor(p.FeedPoints)}"));
        return $"Las expediciones son para las mascotas en el **nivel {PetExpeditions.RequiredLevel}** y todavía ninguna llegó ({levels}). Alimentalas con **/pet feed**.";
    }

    // El menú: cuánto dura y cuánta comida cuesta cada salida, y qué mascotas pueden salir. A propósito NO muestra el premio (oro, EXP, chance de hallazgo) ni qué trae cada especie: el dueño
    // (2026-10-29) quiere que lo descubran de a poco, y el costo sí hay que decirlo porque se paga al salir.
    public static Embed BuildExpeditionMenuEmbed(ExpeditionMenuData data)
    {
        var durations = PetExpeditions.Options.Select(o =>
            $"**{PetExpeditions.HoursText(o.Hours)}** — {ItemEmojiRegistry.IconOf(PetRules.FoodItemName, "🦴")} {o.FoodCost} comidas");

        var embed = new EmbedBuilder()
            .WithTitle("🧭 Expediciones")
            .WithColor(Color.Teal)
            .WithDescription(
                $"Mandá a una mascota **nivel {PetExpeditions.RequiredLevel}** a explorar mientras hacés otra cosa. Sale de a una por vez y gasta {PetRules.FoodItemName} (tenés **{data.Food}**). " +
                "Cuando vuelve, recogés lo que trajo. ¿Qué encontrará? Descubrilo.")
            .AddField("⏱️ Cuánto dura y cuánto cuesta", string.Join("\n", durations), false)
            .AddField("🐾 Pueden salir", string.Join("\n", data.Eligible.Select(p => $"{p.Species.Emoji} **{p.Species.Name}**")), false);

        if (data.TooLow.Count > 0)
        {
            embed.AddField("⏳ Todavía no", string.Join("\n", data.TooLow.Select(p => $"{p.Species.Emoji} {p.Species.Name} — nivel {PetRules.LevelFor(p.FeedPoints)}/{PetRules.MaxLevel}")), false);
        }

        return embed.WithFooter("Elegí mascota y horas abajo, o escribilo: /pet expedition mascota:Ñandusito duracion:8 horas").Build();
    }

    // Un solo desplegable con mascota × duración (6 mascotas × 3 = 18 opciones, entra en las 25 de Discord). Va desactivado si no alcanza ni para la más barata. El valor es «especie:horas».
    public static MessageComponent BuildExpeditionMenu(ulong owner, ExpeditionMenuData data)
    {
        int cheapest = PetExpeditions.CheapestFoodCost;
        var menu = new SelectMenuBuilder()
            .WithCustomId($"pet_exp_go:{owner}")
            .WithPlaceholder(data.Food >= cheapest ? "Elegí mascota y duración…" : $"Te faltan comidas ({data.Food}/{cheapest})")
            .WithMinValues(1)
            .WithMaxValues(1)
            .WithDisabled(data.Food < cheapest);

        foreach (var pet in data.Eligible)
        {
            foreach (var option in PetExpeditions.Options)
            {
                menu.AddOption(
                    $"{pet.Species.Name} · {PetExpeditions.HoursText(option.Hours)}",
                    $"{pet.Species.SpeciesId}:{option.Hours}",
                    $"🍖 Cuesta {option.FoodCost} comidas");
            }
        }

        return new ComponentBuilder().WithSelectMenu(menu).Build();
    }

    // «Ñandusito 8», «8 Ñandusito», «Cachorro de Puma 4h»: separa el nombre de la mascota de las horas (cualquier palabra que sea un número, con «h» o «horas» pegado o suelto, cuenta como las horas).
    public static (string? PetName, int? Hours) ParseExpeditionRequest(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, null);
        }

        var nameParts = new List<string>();
        int? hours = null;
        foreach (string token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // «h», «hora» y «horas» sueltas acompañan al número («8 horas»): no son parte del nombre. Pegadas al número («8h», «8horas») se le sacan.
            string lower = token.ToLowerInvariant();
            if (lower is "h" or "hora" or "horas")
            {
                continue;
            }

            string digits = lower.EndsWith("horas", StringComparison.Ordinal) ? lower[..^5] : lower.EndsWith("hora", StringComparison.Ordinal) ? lower[..^4] : lower.TrimEnd('h');
            if (int.TryParse(digits, out int parsed) && parsed > 0)
            {
                hours = parsed;
            }
            else
            {
                nameParts.Add(token);
            }
        }

        return (nameParts.Count == 0 ? null : string.Join(' ', nameParts), hours);
    }
}

// El menú desplegable de /pet expedition y su lista de autocompletado. Van en clases propias (sin [Group]) para que el id del componente sea exactamente «pet_exp_go:{dueño}»: en un módulo con [Group]
// Discord.Net le antepone el nombre del grupo. El id lleva el dueño, así nadie más usa tu pantalla.
public class PetExpeditionButtonsModule(IUserRepository userRepository, IPetExpeditionRepository expeditionRepository, IGameEvents gameEvents) : InteractionModuleBase<SocketInteractionContext>
{
    [ComponentInteraction("pet_exp_go:*")]
    public async Task HandleGoAsync(string ownerRaw, string[] selected)
    {
        if (!ulong.TryParse(ownerRaw, out ulong owner) || owner != Context.User.Id)
        {
            await RespondAsync("Ese menú no es tuyo: abrí el tuyo con **/pet expedition**.", ephemeral: true);
            return;
        }

        await DeferAsync();

        try
        {
            string[] parts = (selected.FirstOrDefault() ?? string.Empty).Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int speciesId) || !int.TryParse(parts[1], out int hours))
            {
                await FollowupAsync("No entendí esa opción: abrí el menú de nuevo con **/pet expedition**.", ephemeral: true);
                return;
            }

            var player = await userRepository.GetByDiscordIdAsync(owner);
            if (player is null)
            {
                await FollowupAsync("Todavía no tenés cuenta: empezá con **/start**.", ephemeral: true);
                return;
            }

            var result = await PetModule.StartExpeditionAsync(expeditionRepository, gameEvents, player, speciesId, hours);

            if (result.Embed is not null)
            {
                // Salió (o ya estaba afuera): el menú se reemplaza por la pantalla de salida, sin componentes (ya no hay nada que elegir).
                await ModifyOriginalResponseAsync(props =>
                {
                    props.Embed = result.Embed;
                    props.Components = new ComponentBuilder().Build();
                });
            }
            else
            {
                await FollowupAsync(result.PlainMessage, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude mandar a la mascota, intentá de nuevo en un momento.", ephemeral: true);
        }
    }
}

public static class ExpeditionChoices
{
    // La lista de /pet expedition: solo las mascotas que pueden salir (nivel 10).
    public static IReadOnlyList<AutocompleteResult> ForExpedition(IEnumerable<OwnedPet> owned, string typed) =>
        owned
            .Where(p => PetExpeditions.IsEligible(p) && FitsAsValue(p.Species.Name) && Matches(p.Species.Name, typed))
            .OrderBy(p => Relevance(p.Species.Name, typed))
            .ThenBy(p => p.Species.ZoneId == FogonRules.GateZoneId ? int.MaxValue : p.Species.ZoneId)
            .Take(MaxChoices)
            .Select(p => new AutocompleteResult(Truncate($"{p.Species.Emoji} {p.Species.Name} — nivel {PetRules.LevelFor(p.FeedPoints)}"), p.Species.Name))
            .ToList();
}

public sealed class ExpeditionPetAutocompleteHandler : SafeAutocompleteHandler
{
    protected override async Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services) =>
        ExpeditionChoices.ForExpedition(await services.GetRequiredService<IPetRepository>().GetOwnedAsync(userId), typed);
}
