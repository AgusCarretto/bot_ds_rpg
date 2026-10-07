using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using static AutocompleteText;

// /pet: las mascotas (v0.10.0, reglas en GameData/PetRules.cs). Cada zona tiene la suya: llega como HUEVO la primera vez que vencés a su jefe y nace al abrirlo con /open.
// Todas las que tengas valen A LA VEZ (oro, EXP, defensa, drop de monstruos). Suben de nivel comiendo "Comida para Mascotas", y cada una puede comer UNA vez por hora.
// Las reglas viven en los métodos estáticos de abajo (sin Context) para que "aa pet" haga exactamente lo mismo; alimentar es una transacción guardada en IPetRepository.
[Group("pet", "Tus mascotas: mirá sus bonus y alimentalas (una vez por hora cada una).")]
public class PetModule(
    IUserRepository userRepository, IPetRepository petRepository, IInventoryRepository inventoryRepository, IGameEvents gameEvents, IPlayerBonusService bonusService)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("view", "Mirá tus mascotas, el bonus que da cada una y cuándo pueden comer.")]
    public Task HandleViewAsync() => RunAsync(() => ExecuteViewAsync(userRepository, petRepository, inventoryRepository, Context.User.Id, bonusService: bonusService), ephemeral: false);

    [SlashCommand("feed", "Dale Comida para Mascotas: a la que elijas o, sin elegir, a todas las que puedan comer.")]
    public Task HandleFeedAsync(
        [Summary("mascota", "Cuál alimentás. Si no elegís ninguna, comen todas las que estén listas.")] [Autocomplete(typeof(PetAutocompleteHandler))] string? pet = null) =>
        RunAsync(() => ExecuteFeedAsync(userRepository, petRepository, gameEvents, Context.User.Id, pet, bonusService), ephemeral: false);

    private async Task RunAsync(Func<Task<PetResult>> action, bool ephemeral)
    {
        await DeferAsync();

        try
        {
            var result = await action();
            await FollowupAsync(result.PlainMessage, embed: result.Embed, components: result.Components, ephemeral: ephemeral || result.Embed is null);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude con tus mascotas ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Exactamente uno de PlainMessage / Embed viene con valor (mismo patrón que BankModule.BankResult); Components solo acompaña a un Embed.
    public sealed record PetResult(string? PlainMessage, Embed? Embed, MessageComponent? Components = null);

    private const string NoAccount = "Todavía no tenés cuenta: empezá con **/start**.";
    private const string NoPets = "Todavía no tenés ninguna mascota. La primera vez que vencés al jefe de una zona (**/boss**) —y al Asador Eterno del Fogón— te llega un **huevo** además del cofre: abrilo con **/open** y nace.";

    // Lo que hace falta para dibujar la pantalla de mascotas (todo ya leído: el armado es puro).
    // FeedCooldown: la espera entre comidas de ESTE jugador (null = la hora de siempre; la bendición Buen Pienso la baja). PackMultiplier: lo que multiplica la bendición Manada a los bonus (1 = nada).
    public sealed record PetViewData(
        IReadOnlyList<PetSpecies> Species, IReadOnlyList<OwnedPet> Owned, int Food, IReadOnlySet<string> EggNamesHeld, DateTime NowUtc,
        TimeSpan? FeedCooldown = null, double PackMultiplier = 1.0);

    public static async Task<PetResult> ExecuteViewAsync(
        IUserRepository userRepository, IPetRepository petRepository, IInventoryRepository inventoryRepository, ulong discordId, string? headline = null,
        IPlayerBonusService? bonusService = null)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new PetResult(NoAccount, null);
        }

        var species = await petRepository.GetSpeciesAsync();
        var owned = await petRepository.GetOwnedAsync(discordId);
        var inventory = await inventoryRepository.GetByDiscordIdAsync(discordId);

        int food = inventory.Where(e => e.ItemName == PetRules.FoodItemName).Sum(e => e.Quantity);
        var eggNames = inventory.Where(e => e.Quantity > 0 && e.Type == "Huevo").Select(e => e.ItemName).ToHashSet();

        var bonuses = bonusService is null ? null : await bonusService.GetAsync(discordId, player.FuegoNuevo);
        var data = new PetViewData(species, owned, food, eggNames, DateTime.UtcNow, bonuses?.PetFeedCooldown, bonuses?.PetsBlessingMultiplier ?? 1.0);
        return new PetResult(null, BuildViewEmbed(data, headline), BuildViewButtons(discordId, data));
    }

    // Abrir un huevo (lo llama /open cuando el ítem es de tipo "Huevo"): el huevo se gasta y nace la mascota de esa zona, en UNA transacción (IPetRepository.HatchAsync).
    // Un huevo es una sola mascota: aunque se pida abrir varios, nace uno. Si ya tenés esa mascota el huevo NO se gasta.
    public static async Task<PetResult> ExecuteHatchAsync(IPetRepository petRepository, IGameEvents gameEvents, ulong discordId, Item egg)
    {
        var outcome = await petRepository.HatchAsync(discordId, egg.ItemId);
        string eggText = ItemDisplay.Format(egg.Emoji, egg.Name);

        switch (outcome.Status)
        {
            case HatchStatus.Ok:
                break;
            case HatchStatus.NoEgg:
                return new PetResult($"No tenés **{eggText}** en tu inventario.", null);
            case HatchStatus.AlreadyOwned:
                return new PetResult($"Ya tenés a **{outcome.Species!.Name}**: no puede haber dos de la misma. El huevo se queda en tu mochila (no se gastó).", null);
            case HatchStatus.NotAnEgg:
                return new PetResult($"**{eggText}** no se puede abrir.", null);
            default:
                return new PetResult(NoAccount, null);
        }

        var species = outcome.Species!;
        await gameEvents.RecordAsync(discordId, GameEventKinds.PetHatched, species.ZoneId, detail: species.Name);

        var embed = new EmbedBuilder()
            .WithTitle($"🐣 ¡Nació {species.Emoji} {species.Name}!")
            .WithColor(Color.Gold)
            .WithDescription(
                $"Del huevo salió **{species.Name}**. Te acompaña siempre y ya te da **{PetRules.PercentText(PetRules.BonusPercent(species, 1))} de {PetRules.KindName(species.BonusKind)}**; " +
                $"si la cuidás llega a **{PetRules.PercentText(species.MaxBonusPercent)}**.\n\n" +
                $"Alimentala con **/pet feed**: come **{PetRules.FoodItemName}** (se compra en la taberna), una vez por hora. Mirá todas con **/pet view**.")
            .Build();

        return new PetResult(null, embed);
    }

    // "💰 +0,5 % de oro · 📊 +15 % de EXP ...": solo lo que de verdad da algo. Vacío si no hay bonus.
    public static string BonusSummary(PetBonuses bonuses)
    {
        var parts = new List<string>();
        if (bonuses.GoldPercent > 0) parts.Add($"💰 {PetRules.PercentText(bonuses.GoldPercent)} de oro");
        if (bonuses.XpPercent > 0) parts.Add($"📊 {PetRules.PercentText(bonuses.XpPercent)} de EXP");
        if (bonuses.DefensePercent > 0) parts.Add($"🛡️ {PetRules.PercentText(bonuses.DefensePercent)} de defensa");
        if (bonuses.DropPercent > 0) parts.Add($"🎲 {PetRules.PercentText(bonuses.DropPercent)} de drop de monstruos");
        if (bonuses.GatherPercent > 0) parts.Add($"🪓 {PetRules.PercentText(bonuses.GatherPercent)} de recolección (/chop y /mine)");
        return string.Join(" · ", parts);
    }

    // Público y puro: se prueba sin Discord. Una mascota por campo (apiladas, nada en columnas) y lo que todavía no tenés al final.
    public static Embed BuildViewEmbed(PetViewData data, string? headline = null)
    {
        var embed = new EmbedBuilder().WithTitle("🐾 Tus mascotas").WithColor(Color.Gold);
        string lead = string.IsNullOrWhiteSpace(headline) ? string.Empty : headline + "\n\n";

        if (data.Owned.Count == 0)
        {
            embed.WithDescription(lead + NoPets);
        }
        else
        {
            string pack = data.PackMultiplier > 1.0 ? $" (con tu bendición 🐾 Manada: {FuegoNuevoRules.PercentText(data.PackMultiplier)})" : string.Empty;
            embed.WithDescription(
                $"{lead}Todas suman **a la vez**: {BonusSummary(PetRules.Total(data.Owned).Scaled(data.PackMultiplier))}{pack}\n\n" +
                $"Alimentalas con **/pet feed** (una vez cada {PetRules.CooldownText(data.FeedCooldown)} cada una).");
        }

        foreach (var pet in data.Owned)
        {
            int level = PetRules.LevelFor(pet.FeedPoints);
            var species = pet.Species;
            string bonus = $"{PetRules.PercentText(PetRules.BonusPercent(pet))} de {PetRules.KindName(species.BonusKind)}" +
                (level >= PetRules.MaxLevel ? " — ¡al máximo!" : $" (tope {PetRules.PercentText(species.MaxBonusPercent)})");

            string state;
            if (PetRules.ProgressToNextLevel(pet.FeedPoints) is { } progress)
            {
                var remaining = PetRules.RemainingCooldown(pet, data.NowUtc, data.FeedCooldown);
                state = $"🍖 Comida para el próximo nivel: {progress.Have}/{progress.Need}\n" +
                    (remaining == TimeSpan.Zero ? "✅ Lista para comer" : $"⏳ Vuelve a comer en {TimeFormat.Remaining(remaining)}");
            }
            else
            {
                state = "🏆 Nivel máximo";
            }

            embed.AddField($"{species.Emoji} {species.Name} · nivel {level}/{PetRules.MaxLevel}", $"{bonus}\n{state}", false);
        }

        var missing = data.Species.Where(s => data.Owned.All(o => o.Species.SpeciesId != s.SpeciesId)).ToList();
        if (missing.Count > 0)
        {
            string lines = string.Join('\n', missing.Select(s =>
                $"{s.Emoji} **{s.Name}** ({PetRules.PercentText(s.MaxBonusPercent)} de {PetRules.KindName(s.BonusKind)} al máximo) — " +
                (data.EggNamesHeld.Contains(s.EggName) ? "ya tenés su huevo: abrilo con **/open**" : $"vencé al jefe de {s.ZoneName}")));
            embed.AddField("🥚 Por descubrir", lines, false);
        }

        return embed.WithFooter($"🦴 {PetRules.FoodItemName} en tu mochila: {data.Food} · se compra en la taberna").Build();
    }

    // El botón "Alimentar" de la pantalla: deshabilitado si no hay mascotas listas o no tenés comida (el rechazo real lo decide la base, esto es solo ayuda visual).
    public static MessageComponent BuildViewButtons(ulong owner, PetViewData data)
    {
        if (data.Owned.Count == 0)
        {
            return new ComponentBuilder().Build();
        }

        bool canFeed = data.Food > 0 && data.Owned.Any(p => PetRules.CanEat(p, data.NowUtc, data.FeedCooldown));
        return new ComponentBuilder()
            .WithButton("Alimentar", $"pet_feed:{owner}", ButtonStyle.Success, new Emoji("🍖"), disabled: !canFeed)
            .Build();
    }

    // /pet feed [mascota]: con nombre, esa; sin nombre, TODAS las que puedan comer ahora (una comida cada una, hasta donde alcance la comida).
    public static async Task<PetResult> ExecuteFeedAsync(
        IUserRepository userRepository, IPetRepository petRepository, IGameEvents gameEvents, ulong discordId, string? petName, IPlayerBonusService? bonusService = null)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new PetResult(NoAccount, null);
        }

        // La espera entre comidas de ESTE jugador (la bendición Buen Pienso la baja de la hora); null = la hora de siempre.
        TimeSpan? cooldown = bonusService is null ? null : (await bonusService.GetAsync(discordId, player.FuegoNuevo)).PetFeedCooldown;

        var owned = await petRepository.GetOwnedAsync(discordId);
        if (owned.Count == 0)
        {
            return new PetResult(NoPets, null);
        }

        bool chosen = !string.IsNullOrWhiteSpace(petName);
        var now = DateTime.UtcNow;
        List<OwnedPet> targets;

        if (chosen)
        {
            var match = owned.FirstOrDefault(p => SameName(p.Species.Name, petName!));
            if (match is null)
            {
                return new PetResult($"No tenés una mascota que se llame **{petName!.Trim()}**. Mirá las tuyas con **/pet view**.", null);
            }

            targets = [match];
        }
        else
        {
            targets = owned.Where(p => PetRules.CanEat(p, now, cooldown)).ToList();
            if (targets.Count == 0)
            {
                return new PetResult(NoOneCanEatText(owned, now, cooldown), null);
            }
        }

        var blocks = new List<string>();
        var fedNames = new List<string>();
        int foodLeft = -1;
        bool outOfFood = false;

        foreach (var target in targets)
        {
            var outcome = await petRepository.FeedAsync(discordId, target.Species.SpeciesId, cooldown);
            var species = target.Species;

            if (outcome.Status == FeedStatus.NoFood)
            {
                outOfFood = true;
                break;
            }

            switch (outcome.Status)
            {
                case FeedStatus.Ok:
                    foodLeft = outcome.FoodLeft;
                    fedNames.Add(species.Name);
                    await gameEvents.RecordAsync(discordId, GameEventKinds.PetFed, detail: species.Name);
                    blocks.Add(FedBlock(outcome));
                    break;
                case FeedStatus.OnCooldown when chosen:
                    blocks.Add($"⏳ **{species.Name}** todavía no tiene hambre: vuelve a comer en **{TimeFormat.Remaining(outcome.Remaining)}**.");
                    break;
                case FeedStatus.MaxLevel when chosen:
                    blocks.Add($"🏆 **{species.Name}** ya está en el nivel máximo: no necesita más comida.");
                    break;
                case FeedStatus.NotOwned or FeedStatus.NoAccount:
                    return new PetResult(NoPets, null);
            }
        }

        if (outOfFood)
        {
            blocks.Add($"🦴 Te quedaste sin **{PetRules.FoodItemName}**: se compra en la **/taberna**.");
        }

        if (blocks.Count == 0)
        {
            // Nadie comió ni hay nada que contar (la base dijo que ninguna estaba lista, ej. otro comando las alimentó un instante antes).
            return new PetResult(NoOneCanEatText(owned, now, cooldown), null);
        }

        var embed = new EmbedBuilder()
            .WithTitle(
                fedNames.Count == 1 ? $"🍖 {targets.First(t => t.Species.Name == fedNames[0]).Species.Emoji} {fedNames[0]} comió"
                : fedNames.Count > 1 ? "🍖 Alimentaste a tus mascotas"
                : "🍖 Tus mascotas") // nadie comió (esperando o sin comida): no decir que se las alimentó
            .WithColor(fedNames.Count > 0 ? Color.Green : Color.Orange)
            .WithDescription(string.Join("\n\n", blocks));

        if (foodLeft >= 0)
        {
            embed.WithFooter($"Te quedan {foodLeft} de comida · cada mascota vuelve a comer en {PetRules.CooldownText(cooldown)}");
        }

        return new PetResult(null, embed.Build());
    }

    // "🐦 Ñandusito — nivel 4 · +2 % de oro" y, si subió, la celebración. outcome.Pet ya es la mascota DESPUÉS de comer.
    public static string FedBlock(FeedOutcome outcome)
    {
        var pet = outcome.Pet!;
        var species = pet.Species;
        int level = PetRules.LevelFor(pet.FeedPoints);
        string text = $"{species.Emoji} **{species.Name}** — nivel {level} · {PetRules.PercentText(PetRules.BonusPercent(pet))} de {PetRules.KindName(species.BonusKind)}";

        if (level > outcome.LevelBefore)
        {
            text += level >= PetRules.MaxLevel
                ? "\n🎉 ¡Llegó al **nivel máximo**!"
                : $"\n🎉 ¡Subió al **nivel {level}**!";
        }
        else if (PetRules.ProgressToNextLevel(pet.FeedPoints) is { } progress)
        {
            text += $"\n🍖 Para el próximo nivel: {progress.Have}/{progress.Need}";
        }

        return text;
    }

    // Nadie puede comer: o todas están al máximo, o hay que esperar (se dice cuánto falta para la primera que pueda).
    public static string NoOneCanEatText(IReadOnlyList<OwnedPet> owned, DateTime nowUtc, TimeSpan? cooldown = null)
    {
        var waiting = owned.Where(p => !PetRules.IsMaxLevel(p)).Select(p => (Pet: p, Remaining: PetRules.RemainingCooldown(p, nowUtc, cooldown))).ToList();
        if (waiting.Count == 0)
        {
            return "🏆 Todas tus mascotas están en el **nivel máximo**: no necesitan más comida.";
        }

        var next = waiting.MinBy(w => w.Remaining);
        return $"Ninguna mascota puede comer todavía: **{next.Pet.Species.Name}** vuelve a comer en **{TimeFormat.Remaining(next.Remaining)}**.";
    }
}

// El botón "Alimentar" de /pet view. Va en su propia clase (sin [Group]) para que el id del botón sea exactamente "pet_feed:{dueño}": en un módulo con [Group] Discord.Net
// le antepone el nombre del grupo al id. El id lleva el dueño, así nadie más usa tu pantalla.
public class PetButtonsModule(
    IUserRepository userRepository, IPetRepository petRepository, IInventoryRepository inventoryRepository, IGameEvents gameEvents, IPlayerBonusService bonusService)
    : InteractionModuleBase<SocketInteractionContext>
{
    [ComponentInteraction("pet_feed:*")]
    public async Task HandleFeedAsync(string ownerRaw)
    {
        if (!ulong.TryParse(ownerRaw, out ulong owner) || owner != Context.User.Id)
        {
            await RespondAsync("Esas no son tus mascotas: mirá las tuyas con **/pet view**.", ephemeral: true);
            return;
        }

        await DeferAsync();

        try
        {
            var feed = await PetModule.ExecuteFeedAsync(userRepository, petRepository, gameEvents, owner, petName: null, bonusService);
            var view = await PetModule.ExecuteViewAsync(userRepository, petRepository, inventoryRepository, owner, bonusService: bonusService);

            // La pantalla se refresca con el estado nuevo (niveles, botón) y lo que pasó va aparte, solo para quien clickeó.
            await ModifyOriginalResponseAsync(props =>
            {
                props.Embed = view.Embed;
                props.Components = view.Components ?? new ComponentBuilder().Build();
            });
            await FollowupAsync(feed.PlainMessage, embed: feed.Embed, ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude alimentar a tus mascotas, intentá de nuevo en un momento.", ephemeral: true);
        }
    }
}

// La lista de /pet feed: las mascotas que tenés, con su nivel y si pueden comer ahora. Pura (sin Discord) para probarla.
public static class PetChoices
{
    public static IReadOnlyList<AutocompleteResult> ForFeed(IEnumerable<OwnedPet> owned, DateTime nowUtc, string typed, TimeSpan? cooldown = null) =>
        owned
            .Where(p => FitsAsValue(p.Species.Name) && Matches(p.Species.Name, typed))
            .OrderBy(p => Relevance(p.Species.Name, typed))
            // La mascota del Fogón Eterno (zona 0) va DESPUÉS de las de la escalera, como en /pet view (el repositorio ordena por min_level): ordenar por el id la pondría primera.
            .ThenBy(p => p.Species.ZoneId == FogonRules.GateZoneId ? int.MaxValue : p.Species.ZoneId)
            .Take(MaxChoices)
            .Select(p =>
            {
                int level = PetRules.LevelFor(p.FeedPoints);
                var remaining = PetRules.RemainingCooldown(p, nowUtc, cooldown);
                string state = PetRules.IsMaxLevel(p) ? "nivel máximo" : remaining == TimeSpan.Zero ? "lista para comer" : $"come en {TimeFormat.Remaining(remaining)}";
                return new AutocompleteResult(Truncate($"{p.Species.Emoji} {p.Species.Name} — nivel {level} · {state}"), p.Species.Name);
            })
            .ToList();
}

public sealed class PetAutocompleteHandler : SafeAutocompleteHandler
{
    protected override async Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services)
    {
        var owned = await services.GetRequiredService<IPetRepository>().GetOwnedAsync(userId);

        // La espera de este jugador (Buen Pienso); sin cuenta o si falla la lectura, la hora de siempre. Nunca crea la cuenta (GetByDiscordIdAsync).
        var player = await services.GetRequiredService<IUserRepository>().GetByDiscordIdAsync(userId);
        TimeSpan? cooldown = player is null ? null : (await services.GetRequiredService<IPlayerBonusService>().GetAsync(userId, player.FuegoNuevo)).PetFeedCooldown;
        return PetChoices.ForFeed(owned, DateTime.UtcNow, typed, cooldown);
    }
}
