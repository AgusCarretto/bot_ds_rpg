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
    // EL RECETARIO (v0.27.0, el dueño: «quiero que sea así de sencillo, como el `rpg recipes`»): TODAS las zonas en un solo mensaje, una línea por receta —
    // «<emoji> Nombre `+14 ATQ` → 12 <emoji> + 4 <emoji> … · <oro>»—, agrupadas por zona de la más fácil a la más difícil, con lo que VE ese jugador (el arma de su clase y el amuleto), después
    // «Sin zona específica» (la Hoja de Acero Puro, la única arma general, hecha solo con materiales) y, con la puerta abierta, el equipo del Fogón. Sin las listas de ✅/❌ ni «tenés X de Y»: la herrería (/forge) ya te marca
    // qué podés forjar y qué te falta. Cada parte es un campo (1024 caracteres: dos recetas con cinco emojis cada una, ~600) y el embed entero tiene que entrar en 6000: si algún día no entra (más zonas, herramientas) se
    // corta con un aviso en vez de reventar (Build() tira excepción si se pasa).
    // zoneId: solo esa zona (aa forge recipes 3; 0 es el Fogón); null = todas. playerLevel: si viene, las zonas que piden más nivel llevan un 🔒.
    public static async Task<Embed> BuildRecipesEmbed(
        IRecipeRepository recipeRepository, IZoneRepository zoneRepository, string playerClass, int? zoneId = null,
        int? playerLevel = null, int? highestZoneCleared = null)
    {
        var recipes = await recipeRepository.GetAllAsync();
        var zones = await zoneRepository.GetAllAsync();
        return RenderRecipesEmbed(recipes, zones, playerClass, zoneId, playerLevel, highestZoneCleared: highestZoneCleared);
    }

    // Lo mismo sin tocar la base (el que ya tiene recetas y zonas cargadas, como la escena de la herrería, lo usa directo).
    // forgeHint: la frase de cómo forjar; la escena de /forge pone la suya (ya estás en la lista), y el resto el comando de siempre.
    public static Embed RenderRecipesEmbed(
        IReadOnlyList<RecipeDetails> recipes, IReadOnlyList<Zone> zones, string playerClass, int? zoneId = null,
        int? playerLevel = null, string? forgeHint = null, int? highestZoneCleared = null)
    {
        const int EmbedBudget = 5800; // el límite de Discord es 6000 entre título, descripción y campos

        var embed = new EmbedBuilder()
            .WithTitle("⚒️ Recetas del Herrero")
            .WithColor(Color.DarkGrey);

        if (recipes.Count == 0)
        {
            embed.WithDescription("Todavía no hay recetas cargadas.");
            return embed.Build();
        }

        // El Fogón Eterno (v0.11.0): si ya venció al jefe de la última zona, el recetario suma, en su propio bloque, las recetas del equipo del Fogón.
        var orderedZones = ZoneRanking.OrderByDifficulty(zones);
        bool gateOpen = highestZoneCleared is int cleared && FogonRules.IsGateOpen(orderedZones, cleared);
        var sections = RecipeCatalog.BookFor(recipes, zones, playerClass, gateOpen, zoneId);

        if (sections.Count == 0)
        {
            embed.WithDescription(zoneId is null ? "No hay recetas para vos todavía." : $"No hay recetas para vos en la zona {zoneId}.");
            return embed.Build();
        }

        string hint = forgeHint ?? "Forjá con `/forge` (o `aa forge make <nombre>`).";
        embed.WithDescription($"Lo que ves es lo de tu clase (**{playerClass}**). {hint}");

        int used = (embed.Title?.Length ?? 0) + (embed.Description?.Length ?? 0);
        int shown = 0;
        foreach (var section in sections)
        {
            string name = SectionTitle(section, playerLevel);
            string value = SectionLines(section.Recipes);
            if (used + name.Length + value.Length > EmbedBudget)
            {
                embed.AddField("…", $"Hay más zonas: miralas de a una con `aa forge recipes <zona>`.", false);
                break;
            }

            embed.AddField(name, value, false);
            used += name.Length + value.Length;
            shown++;
        }

        return embed.Build();
    }

    // El título de cada parte del recetario: «🌾 Zona 1 · Praderas del Mate · nivel 1» (con 🔒 si todavía no llegás), «🧰 Sin zona específica» o «🔥 El Fogón Eterno».
    private static string SectionTitle(RecipeCatalog.RecipeSection section, int? playerLevel)
    {
        if (section.IsGate)
        {
            return "🔥 El Fogón Eterno";
        }

        if (section.Zone is null)
        {
            return "🧰 Sin zona específica";
        }

        string locked = playerLevel is int level && section.Zone.MinLevel > level ? " 🔒" : string.Empty;
        return $"{section.Zone.Emoji ?? "🗺️"} Zona {section.Zone.ZoneId} · {section.Zone.Name} · nivel {section.Zone.MinLevel}{locked}";
    }

    // Una línea por receta (con el emoji de CADA ítem: el resultado y cada ingrediente; un ingrediente sin emoji cargado se escribe con su nombre). Si no entran todas en el campo
    // (1024), se cortan las últimas en vez de partir un emoji a la mitad.
    internal static string SectionLines(IEnumerable<RecipeDetails> recipes)
    {
        const int FieldBudget = 1024;
        var lines = new List<string>();
        int length = 0;
        foreach (var recipe in recipes)
        {
            string line = RecipeLine(recipe);
            if (length + line.Length + 1 > FieldBudget)
            {
                break;
            }

            lines.Add(line);
            length += line.Length + 1;
        }

        return string.Join('\n', lines);
    }

    // «🗡️ **Espada de Madera** `+14 ATQ` → 12 <pino> + 4 <hierro> + 2 <colmillo> + 1 <cuero> · 💰 100».
    internal static string RecipeLine(RecipeDetails recipe)
    {
        string stat = ItemStatLabel.Format(recipe.ResultItem) ?? recipe.ResultItem.Type;
        string ingredients = string.Join(" + ", recipe.Ingredients.Select(i =>
            $"{i.Quantity} {(string.IsNullOrWhiteSpace(i.Emoji) ? i.ItemName : i.Emoji)}"));
        string result = string.IsNullOrWhiteSpace(recipe.ResultItem.Emoji)
            ? $"**{recipe.ResultItem.Name}**"
            : $"{recipe.ResultItem.Emoji} **{recipe.ResultItem.Name}**";
        return $"{result} `{stat}` → {ingredients} · {UiEmojis.Gold} {GameHistory.Number(recipe.GoldCost)}";
    }

    // UNA receta por bloque (un field no en línea), en vez de una línea larga por receta: el título lleva el ítem, y
    // abajo van lo que suma, el oro y cada ingrediente en su propia línea. Con las cantidades de la recolección (Hierro
    // x10...) la línea única era una pared de texto.
    internal static void AddRecipes(
        EmbedBuilder embed, IEnumerable<RecipeDetails> recipes, RecipeGroup group, string groupLabel, string playerClass,
        IReadOnlyDictionary<string, int>? owned, int? playerGold)
    {
        var inGroup = recipes
            .Where(r => RecipeCatalog.GroupOf(r) == group)
            .OrderBy(r => r.ResultItem.StatValue)
            .ThenBy(r => r.ResultItem.Name, StringComparer.Ordinal);

        foreach (var recipe in inGroup)
        {
            string stat = ItemStatLabel.Format(recipe.ResultItem) ?? recipe.ResultItem.Type;
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
            : $"**{itemText}** ya quedó **equipad{(outcome.EquippedSlot == "weapon" ? "a" : "o")}** como {(outcome.EquippedSlot == "weapon" ? "arma" : "amuleto")} ({ItemStatLabel.Format(recipe.ResultItem) ?? recipe.ResultItem.Type}).";

        return new ForgeMakeResult(null, new EmbedBuilder()
            .WithTitle(outcome.EquippedSlot is null ? "⚒️ ¡Forjado con éxito!" : "⚒️ ¡Forjado y equipado!")
            .WithDescription($"{NpcDialogue.Blacksmith(BlacksmithLine.Success)}\n\n{madeLine}\nOro restante: **{outcome.Player!.Gold}**.")
            .WithItemThumbnail(recipe.ResultItem.Emoji)
            .WithColor(Color.Green)
            .Build());
    }
}
