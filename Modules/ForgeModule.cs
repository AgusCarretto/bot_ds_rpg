using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using BotDsRpg.Services;

// El Herrero: separado de la Tienda (/shop) por diseño de juego. La Tienda vende consumibles
// con oro; la Herrería forja equipamiento con oro + materiales/MonsterDrops del inventario.
// Las recetas viven en la base (tablas "recipes"/"recipe_ingredients", ver IRecipeRepository) en
// vez de en código: como sus item_id son Foreign Keys reales, nunca puede haber una receta
// apuntando a un ingrediente o resultado que no exista.
public static class ForgeModule
{
    // Todo lo que sigue es estático (sin dependencia de Context) para que
    // Modules/TextCommandModule.cs comparta exactamente la misma lógica en "aa forge recipes"/"aa forge make".
    //
    // Muestra SOLO las recetas de la zona actual del jugador (o, si esa todavía no tiene ninguna, la anterior
    // más cercana que sí — ver GameData/RecipeCatalog.cs), de esas las que le corresponden: su arma de afinidad,
    // el arma general y los amuletos. Son 4 recetas (2 armas y 2 amuletos), una por bloque, así que entra sobrado en
    // los límites de Discord (un campo de embed admite 1024 caracteres y el embed entero 6000; Build() tira excepción
    // si se pasa) — por eso el molde de 7 recetas por zona y ver solo la propia. Cada bloque dice cuánto suma el
    // resultado ("+15 ATQ" un arma, "+20 DEF" un amuleto; con ⭐ y el valor real si es de la familia de la clase del
    // jugador), el oro y los ingredientes uno por línea.
    // playerLevel: si viene y la zona que se muestra pide más nivel, se avisa (🔒). currentZoneId es la zona A MOSTRAR: la del jugador en
    // "aa forge recipes", o la que elija en el selector de zonas de /forge (ver Modules/BlacksmithModule.cs).
    public static async Task<Embed> BuildRecipesEmbed(
        IRecipeRepository recipeRepository, IZoneRepository zoneRepository, string playerClass, int currentZoneId,
        IReadOnlyDictionary<string, int>? owned = null, int? playerGold = null, int? playerLevel = null, int? highestZoneCleared = null)
    {
        var recipes = await recipeRepository.GetAllAsync();
        var zones = await zoneRepository.GetAllAsync();
        return RenderRecipesEmbed(recipes, zones, playerClass, currentZoneId, owned, playerGold, playerLevel, highestZoneCleared: highestZoneCleared);
    }

    // Lo mismo sin tocar la base (el que ya tiene recetas y zonas cargadas, como la escena de la herrería, lo usa directo).
    // forgeHint: la frase de cómo forjar; la escena de /forge pone la suya (ya estás en la lista), y el resto el comando de siempre.
    public static Embed RenderRecipesEmbed(
        IReadOnlyList<RecipeDetails> recipes, IReadOnlyList<Zone> zones, string playerClass, int currentZoneId,
        IReadOnlyDictionary<string, int>? owned = null, int? playerGold = null, int? playerLevel = null, string? forgeHint = null, int? highestZoneCleared = null)
    {
        var embed = new EmbedBuilder()
            .WithTitle("⚒️ Recetas del Herrero")
            .WithColor(Color.DarkGrey);

        if (recipes.Count == 0)
        {
            embed.WithDescription("Todavía no hay recetas cargadas.");
            return embed.Build();
        }

        // El Fogón Eterno (v0.11.0): si ya venció al jefe de la última zona, la página de ESA zona suma, en su propio bloque, las recetas del equipo del Fogón.
        var orderedZones = ZoneRanking.OrderByDifficulty(zones);
        bool gateOpen = highestZoneCleared is int cleared && FogonRules.IsGateOpen(orderedZones, cleared);
        var view = RecipeCatalog.ViewFor(recipes, zones, playerClass, currentZoneId, gateOpen);
        if (view.Zone is null)
        {
            embed.WithDescription("Todavía no hay recetas para tu zona.");
            return embed.Build();
        }

        var gateRecipes = gateOpen && view.Zone.ZoneId == FogonRules.LastZone(orderedZones)?.ZoneId ? view.GateRecipes ?? [] : [];
        var zoneRecipes = view.Recipes.Where(r => r.ZoneId != FogonRules.GateZoneId).ToList();

        string zoneName = $"{view.Zone.Emoji ?? "🗺️"} **Zona {view.Zone.ZoneId}: {view.Zone.Name}**";
        string fallbackNote = view.IsFallback
            ? "\n_Tu zona actual todavía no tiene recetas propias: te muestro las de la última zona que sí._"
            : string.Empty;
        string lockNote = playerLevel is int level && view.Zone.MinLevel > level
            ? $"\n🔒 _Esta zona pide nivel {view.Zone.MinLevel} (vos sos nivel {level}): mirá qué te espera._"
            : string.Empty;

        if (zoneRecipes.Count == 0 && gateRecipes.Count == 0)
        {
            embed.WithDescription($"{zoneName}{fallbackNote}{lockNote}\nNo hay recetas para vos en esta zona todavía.");
            return embed.Build();
        }

        embed.WithDescription($"{zoneName}{fallbackNote}{lockNote}\n{forgeHint ?? "Forjá con `/forge` (o `aa forge make <nombre>`): la lista te marca ✅ lo que ya podés hacer."}");

        // Sin íconos de espada / amuleto al lado del nombre: el ítem ya trae su propio emoji y dos íconos juntos lo achicaban. Qué tipo es cada uno
        // lo dice una etiqueta bajo el nombre.
        AddRecipes(embed, zoneRecipes, RecipeGroup.ClassWeapon, "arma de tu clase", playerClass, owned, playerGold);
        AddRecipes(embed, zoneRecipes, RecipeGroup.GeneralWeapon, "arma general", playerClass, owned, playerGold);
        AddRecipes(embed, zoneRecipes, RecipeGroup.Amulet, "amuleto", playerClass, owned, playerGold);
        AddRecipes(embed, zoneRecipes, RecipeGroup.Other, "otro", playerClass, owned, playerGold);

        // El equipo de El Fogón Eterno (zona 0): el arma y el amuleto que hay que llevar PUESTOS para entrar. Son caros a propósito (drops de las 5 zonas).
        if (gateRecipes.Count > 0)
        {
            embed.AddField("🔥 El Fogón Eterno", $"Para entrar con **/zona 0** hay que llevar puestos los dos (no hay sinergia de clase). Mirá `/info tema:fogon`.", false);
            AddRecipes(embed, gateRecipes, RecipeGroup.GeneralWeapon, "equipo del Fogón", playerClass, owned, playerGold);
            AddRecipes(embed, gateRecipes, RecipeGroup.Amulet, "equipo del Fogón", playerClass, owned, playerGold);
        }

        return embed.Build();
    }

    // UNA receta por bloque (un field no en línea), en vez de una línea larga por receta: el título lleva el ítem, y
    // abajo van lo que suma, el oro y cada ingrediente en su propia línea. Con las cantidades de la recolección (Hierro
    // x10...) la línea única era una pared de texto.
    private static void AddRecipes(
        EmbedBuilder embed, IEnumerable<RecipeDetails> recipes, RecipeGroup group, string groupLabel, string playerClass,
        IReadOnlyDictionary<string, int>? owned, int? playerGold)
    {
        var inGroup = recipes
            .Where(r => RecipeCatalog.GroupOf(r) == group)
            .OrderBy(r => r.ResultItem.StatValue)
            .ThenBy(r => r.ResultItem.Name, StringComparer.Ordinal);

        foreach (var recipe in inGroup)
        {
            string stat = ItemStatLabel.FormatFor(recipe.ResultItem, playerClass) ?? recipe.ResultItem.Type;
            // Con el inventario a mano cada ingrediente dice cuánto tenés de cuánto hace falta (✅ / ❌) y el oro también; sin él, la
            // lista de siempre. Una línea en blanco separa el resumen de los ingredientes para que no quede apretado.
            string ingredients = string.Join('\n', recipe.Ingredients.Select(i =>
                owned is null
                    ? $"• {i.Quantity}× {ItemDisplay.Format(i.Emoji, i.ItemName)}"
                    : $"{(owned.GetValueOrDefault(i.ItemName) >= i.Quantity ? "✅" : "❌")} {ItemDisplay.Format(i.Emoji, i.ItemName)} — {owned.GetValueOrDefault(i.ItemName)}/{i.Quantity}"));
            string goldText = playerGold is int gold
                ? $"{(gold >= recipe.GoldCost ? "✅" : "❌")} 💰 {recipe.GoldCost} oro (tenés {gold})"
                : $"💰 {recipe.GoldCost} oro";
            string value = $"**{stat}** · _{groupLabel}_\n{goldText}\n\n{ingredients}";

            // Defensa: un campo de más de 1024 caracteres haría reventar todo el mensaje.
            embed.AddField(
                ItemDisplay.Format(recipe.ResultItem.Emoji, recipe.ResultItem.Name),
                value.Length <= 1024 ? value : value[..1023] + "…",
                false);
        }
    }

    // Exactamente uno de los dos campos viene con valor: PlainMessage para los rechazos simples
    // (receta desconocida, clase equivocada), Embed para el resultado real de intentar forjar.
    public sealed record ForgeMakeResult(string? PlainMessage, Embed? Embed);

    public static async Task<ForgeMakeResult> ExecuteMakeAsync(
        IUserRepository userRepository, IRecipeRepository recipeRepository, ICraftingRepository craftingRepository, IGameEvents gameEvents,
        ulong discordId, string itemName)
    {
        var recipe = await recipeRepository.GetByResultItemNameAsync(itemName);
        if (recipe is null)
        {
            // No se listan todas las recetas: con 8 por zona y 5 zonas (40), el nombre con el emoji de cada una pasaría
            // el límite de 2000 caracteres de un mensaje de Discord y el comando fallaría justo cuando el jugador
            // se equivoca de nombre. Se lo manda a las listas, que ya muestran solo lo de su zona.
            return new ForgeMakeResult(
                $"{NpcDialogue.Blacksmith(BlacksmithLine.UnknownRecipe)}\n(Mirá las recetas de tu zona con `/forge`.)", null);
        }

        // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
        var player = await userRepository.GetOrCreateUserAsync(discordId);

        if (recipe.ResultItem.ClassRequirement is not null
            && !string.Equals(recipe.ResultItem.ClassRequirement, player.Class, StringComparison.OrdinalIgnoreCase))
        {
            return new ForgeMakeResult(
                $"{NpcDialogue.Blacksmith(BlacksmithLine.WrongClass)}\n**{ItemDisplay.Format(recipe.ResultItem.Emoji, recipe.ResultItem.Name)}** es exclusivo de la clase **{recipe.ResultItem.ClassRequirement}** — vos sos **{player.Class}**.", null);
        }

        // Un arma o un amuleto se forja directo a equipamiento (no hay /equip ni pasa por el inventario): si ese casillero ya tiene algo puesto,
        // primero hay que vender lo equipado (en /taberna). Se avisa ANTES de tocar nada; CraftAsync lo vuelve a validar dentro de la transacción.
        int? equippedId = recipe.ResultItem.Type switch { "Weapon" => player.WeaponId, "Amulet" => player.AmuletId, _ => null };
        if (equippedId is int currentlyEquipped)
        {
            bool weaponSlot = recipe.ResultItem.Type == "Weapon";
            string article = weaponSlot ? "a" : "o";
            return currentlyEquipped == recipe.ResultItem.ItemId
                ? new ForgeMakeResult(
                    $"{NpcDialogue.Blacksmith(BlacksmithLine.AlreadyEquipped)}\n**{ItemDisplay.Format(recipe.ResultItem.Emoji, recipe.ResultItem.Name)}** ya es tu {(weaponSlot ? "arma" : "amuleto")} equipad{article}.", null)
                : new ForgeMakeResult(
                    $"{NpcDialogue.Blacksmith(BlacksmithLine.SlotTaken)}\nYa tenés {(weaponSlot ? "un arma equipada" : "un amuleto equipado")}: vendel{article} en **/taberna** (o con **/shop sell**) y volvé a pedírmelo.", null);
        }

        // CraftAsync valida oro + cada ingrediente dentro de una única transacción SQL
        // (SELECT ... FOR UPDATE) y hace rollback completo si falta algo.
        var resolvedIngredients = recipe.Ingredients.Select(i => (i.ItemId, i.ItemName, i.Quantity)).ToList();
        var outcome = await craftingRepository.CraftAsync(discordId, recipe.GoldCost, resolvedIngredients, recipe.ResultItem.ItemId);

        if (!outcome.Success)
        {
            if (outcome.SlotOccupied)
            {
                // Se ocupó el casillero entre el chequeo de arriba y la transacción (dos forjas a la vez): mismo aviso.
                return new ForgeMakeResult($"{NpcDialogue.Blacksmith(BlacksmithLine.SlotTaken)}\nVendé lo que tenés equipado y volvé a pedírmelo.", null);
            }

            return new ForgeMakeResult(null, new EmbedBuilder()
                .WithTitle("⚒️ El herrero no pudo forjarlo")
                .WithDescription($"{NpcDialogue.Blacksmith(BlacksmithLine.NotEnough)}\n\nNo pudiste forjar **{ItemDisplay.Format(recipe.ResultItem.Emoji, recipe.ResultItem.Name)}**: {outcome.FailureReason}")
                .WithColor(Color.Red)
                .Build());
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.Craft, player.CurrentZoneId, detail: recipe.ResultItem.Name);

        // Un arma o un amuleto ya queda puesto: se lo dice con lo que suma. Cualquier otra cosa (hoy no hay recetas así) va al inventario.
        string itemText = ItemDisplay.Format(recipe.ResultItem.Emoji, recipe.ResultItem.Name);
        string madeLine = outcome.EquippedSlot is null
            ? $"¡El Herrero ha forjado **{itemText}** con éxito!"
            : $"**{itemText}** ya quedó **equipad{(outcome.EquippedSlot == "weapon" ? "a" : "o")}** como {(outcome.EquippedSlot == "weapon" ? "arma" : "amuleto")} ({ItemStatLabel.FormatFor(recipe.ResultItem, player.Class) ?? recipe.ResultItem.Type}).";

        return new ForgeMakeResult(null, new EmbedBuilder()
            .WithTitle(outcome.EquippedSlot is null ? "⚒️ ¡Forjado con éxito!" : "⚒️ ¡Forjado y equipado!")
            .WithDescription($"{NpcDialogue.Blacksmith(BlacksmithLine.Success)}\n\n{madeLine}\nOro restante: **{outcome.Player!.Gold}**.")
            .WithItemThumbnail(recipe.ResultItem.Emoji)
            .WithColor(Color.Green)
            .Build());
    }
}
