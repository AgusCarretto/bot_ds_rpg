using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /forge-special (aa forgespecial): el equipo de El Fogón Eterno (la zona 0) a la vista DESDE EL PRINCIPIO, aparte del herrero normal. Esas dos piezas piden los drops de las 5 zonas, así que
// quien no sabe que existen desmantela (/dismantle) o vende justo los materiales que después le faltan; antes las recetas solo aparecían en /forge cuando ya habías vencido al jefe de la última
// zona. Acá se ven siempre, con lo que tenés de cada ingrediente (✅ / ❌). Es solo para MIRAR: se forjan donde siempre (/forge), cuando la puerta ya está abierta (FogonRules.IsGateOpen).
// Se dibuja con el mismo ForgeModule.AddRecipes que la herrería, así que las dos pantallas muestran lo mismo.
public class ForgeSpecialModule(
    IUserRepository userRepository, IRecipeRepository recipeRepository, IZoneRepository zoneRepository, IInventoryRepository inventoryRepository)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("forge-special", "El equipo del Fogón Eterno (Zona 0): mirá desde ya qué materiales guardar para forjarlo.")]
    public async Task HandleForgeSpecialAsync()
    {
        await DeferAsync();

        try
        {
            await FollowupAsync(embed: await BuildEmbedAsync(userRepository, recipeRepository, zoneRepository, inventoryRepository, Context.User.Id));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude mostrar el forge especial ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Estático y sin Context: lo reusa "aa forgespecial". GetByDiscordIdAsync y no GetOrCreateUserAsync: mirar la lista no tiene que crear una cuenta (sin cuenta se muestran las recetas a secas).
    public static async Task<Embed> BuildEmbedAsync(
        IUserRepository userRepository, IRecipeRepository recipeRepository, IZoneRepository zoneRepository, IInventoryRepository inventoryRepository, ulong discordId)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        var recipes = await recipeRepository.GetAllAsync();
        var zones = await zoneRepository.GetAllAsync();
        var owned = player is null
            ? null
            : (await inventoryRepository.GetByDiscordIdAsync(discordId)).ToDictionary(e => e.ItemName, e => e.Quantity, StringComparer.Ordinal);

        return BuildEmbed(recipes, zones, player, owned);
    }

    // Puro (sin base ni Discord): se prueba directo. player null = sin cuenta (sin "tenés / hace falta" ni estado de la puerta).
    public static Embed BuildEmbed(IReadOnlyList<RecipeDetails> recipes, IReadOnlyList<Zone> zones, User? player, IReadOnlyDictionary<string, int>? owned)
    {
        var embed = new EmbedBuilder()
            .WithTitle("🔥 Forge especial · El Fogón Eterno")
            .WithColor(Color.DarkOrange);

        var gateRecipes = recipes.Where(r => r.ZoneId == FogonRules.GateZoneId).ToList();
        if (gateRecipes.Count == 0)
        {
            return embed.WithDescription("Todavía no hay recetas del Fogón cargadas.").Build();
        }

        var ordered = ZoneRanking.OrderByDifficulty(zones);
        var lastZone = FogonRules.LastZone(ordered);
        bool gateOpen = player is not null && FogonRules.IsGateOpen(ordered, player.HighestZoneCleared);

        string status = player is null
            ? "Empezá con **/start** para ver cuánto te falta de cada cosa."
            : gateOpen
                ? "✅ La puerta ya está abierta: forjalas en **/forge** (o `aa forge make <nombre>`)."
                : $"🔒 Se forjan en **/forge** cuando venzas al jefe de {lastZone?.Emoji ?? "🗺️"} **{lastZone?.Name ?? "la última zona"}**.";

        embed.WithDescription(
            "**El Fogón Eterno** (la «Zona 0») es la puerta al **Fuego Nuevo**: para entrar hay que llevar **puestas** estas dos piezas. Piden drops de cacería de las **5 zonas** y mucho mineral y madera, " +
            "así que conviene ir juntándolos desde ahora.\n\n" +
            "⚠️ **No desmanteles ni vendas** lo que figura acá: **/dismantle** lo gasta y después hay que volver a farmearlo.\n\n" +
            status);

        string playerClass = player?.Class ?? string.Empty;
        ForgeModule.AddRecipes(embed, gateRecipes, RecipeGroup.GeneralWeapon, "equipo del Fogón", playerClass, owned, player?.Gold);
        ForgeModule.AddRecipes(embed, gateRecipes, RecipeGroup.Amulet, "equipo del Fogón", playerClass, owned, player?.Gold);

        return embed.WithFooter("Dónde cae cada material: /drops · qué te falta primero: /tips · cómo funciona el Fogón: /info tema:fogon").Build();
    }
}
