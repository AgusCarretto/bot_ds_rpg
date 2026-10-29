using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using static AutocompleteText;

// La evolución de las mascotas (v0.40.0, reglas en GameData/PetRules.cs): al nivel máximo de su tier (10, 20) una mascota puede evolucionar y subir el tope de nivel a 20 y a 30. Pide las horas de
// expedición acumuladas de ESA mascota (100 para el tier II, 250 para el III) y huevos sobrantes de su especie (5 y 10), que se gastan. Sin condición de Fuego Nuevo: los huevos ya son la cuenta
// (cada vuelta da uno por jefe aunque ya tengas la mascota), así que 5 huevos son el FN 6 y 15 el FN 16. El tier II bonifica el doble y el III el cuádruple (bono de la mascota y premio de las expediciones).
public partial class PetModule
{
    [SlashCommand("evolve", "Evolucioná una mascota en el nivel máximo de su tier (pide horas de expedición y huevos).")]
    public Task HandleEvolveAsync(
        [Summary("mascota", "Cuál evolucionás. Si hay una sola lista, esa.")] [Autocomplete(typeof(EvolvePetAutocompleteHandler))] string? pet = null) =>
        RunAsync(() => ExecuteEvolveAsync(userRepository, petRepository, gameEvents, Context.User.Id, pet), ephemeral: false);

    // /pet evolve [mascota] y «aa pet evolve [mascota]». Sin nombre: si hay UNA mascota lista para intentarlo la usa; si hay varias, pide que elijas.
    public static async Task<PetResult> ExecuteEvolveAsync(
        IUserRepository userRepository, IPetRepository petRepository, IGameEvents gameEvents, ulong discordId, string? petName)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new PetResult(NoAccount, null);
        }

        var owned = await petRepository.GetOwnedAsync(discordId);
        if (owned.Count == 0)
        {
            return new PetResult(NoPets, null);
        }

        OwnedPet target;
        if (!string.IsNullOrWhiteSpace(petName))
        {
            var match = owned.FirstOrDefault(p => SameName(p.Species.Name, petName!));
            if (match is null)
            {
                return new PetResult($"No tenés una mascota que se llame **{petName!.Trim()}**. Mirá las tuyas con **/pet view**.", null);
            }

            target = match;
        }
        else
        {
            var atCap = owned.Where(p => PetRules.IsMaxLevel(p) && p.Tier < PetRules.MaxTier).ToList();
            if (atCap.Count == 0)
            {
                return new PetResult("Ninguna de tus mascotas está en el **nivel máximo de su tier** todavía: solo ahí pueden evolucionar. Mirá cómo van con **/pet view**.", null);
            }

            if (atCap.Count > 1)
            {
                return new PetResult($"Hay más de una lista para intentarlo ({string.Join(", ", atCap.Select(p => $"**{PetRules.DisplayName(p)}**"))}): elegí cuál con **/pet evolve mascota:<nombre>**.", null);
            }

            target = atCap[0];
        }

        var outcome = await petRepository.EvolveAsync(discordId, target.Species.SpeciesId);
        string name = PetRules.DisplayName(outcome.Pet ?? target);

        switch (outcome.Status)
        {
            case EvolveStatus.Ok:
                break;
            case EvolveStatus.MaxTier:
                return new PetResult($"🏆 **{name}** ya es tier {PetRules.TierName(PetRules.MaxTier)}: no puede evolucionar más.", null);
            case EvolveStatus.NotAtCap:
                return new PetResult($"**{name}** va por el nivel {outcome.Have} de {outcome.Need}: solo evolucionan las mascotas en el **nivel máximo de su tier**. Seguí con **/pet feed**.", null);
            case EvolveStatus.NotEnoughHours:
                return new PetResult($"⏱️ A **{name}** le faltan horas de expedición para evolucionar: lleva **{outcome.Have}** de **{outcome.Need}**. Mandala con **/pet expedition**.", null);
            case EvolveStatus.NotEnoughEggs:
                return new PetResult($"🥚 A **{name}** le faltan huevos sobrantes de su especie para evolucionar: tenés **{outcome.Have}** de **{outcome.Need}**. Cada vuelta, al vencer por primera vez al jefe de su zona, te llega uno.", null);
            case EvolveStatus.NotOwned:
                return new PetResult(NoPets, null);
            default:
                return new PetResult(NoAccount, null);
        }

        var evolved = outcome.Pet!;
        var species = evolved.Species;
        int cap = PetRules.LevelCap(evolved.Tier);
        await gameEvents.RecordAsync(discordId, GameEventKinds.PetEvolved, species.ZoneId, evolved.Tier, species.Name);

        var embed = new EmbedBuilder()
            .WithTitle($"🧬 ¡{species.Emoji} {species.Name} evolucionó!")
            .WithColor(Color.Gold)
            .WithDescription(
                $"**{species.Name}** pasó al **tier {PetRules.TierName(evolved.Tier)}**. Gastó **{PetRules.EvolveEggs(evolved.Tier)}** huevos sobrantes.\n\n" +
                $"Ahora puede crecer hasta el **nivel {cap}** y su bonus llega a **{PetRules.PercentText(PetRules.BonusPercent(species, cap))} de {PetRules.KindName(species.BonusKind)}**. " +
                $"Se nota también en las expediciones.\n" +
                $"Cada alimentada del nivel {PetRules.LevelCap(evolved.Tier - 1)} en adelante gasta **{PetRules.FoodPerFeed(PetRules.LevelCap(evolved.Tier - 1))}** {PetRules.FoodItemName}: seguí con **/pet feed**.")
            .Build();
        return new PetResult(null, embed);
    }
}

public static class EvolveChoices
{
    // La lista de /pet evolve: las mascotas que están en el nivel máximo de su tier y todavía pueden evolucionar.
    public static IReadOnlyList<AutocompleteResult> ForEvolve(IEnumerable<OwnedPet> owned, string typed) =>
        owned
            .Where(p => PetRules.IsMaxLevel(p) && p.Tier < PetRules.MaxTier && FitsAsValue(p.Species.Name) && Matches(p.Species.Name, typed))
            .OrderBy(p => Relevance(p.Species.Name, typed))
            .ThenBy(p => p.Species.ZoneId == FogonRules.GateZoneId ? int.MaxValue : p.Species.ZoneId)
            .Take(MaxChoices)
            .Select(p => new AutocompleteResult(Truncate($"{p.Species.Emoji} {PetRules.DisplayName(p)} — nivel {PetRules.LevelFor(p.FeedPoints)}"), p.Species.Name))
            .ToList();
}

public sealed class EvolvePetAutocompleteHandler : SafeAutocompleteHandler
{
    protected override async Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services) =>
        EvolveChoices.ForEvolve(await services.GetRequiredService<IPetRepository>().GetOwnedAsync(userId), typed);
}
