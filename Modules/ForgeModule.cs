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
        for (int i = 0; i < sections.Count; i++)
        {
            string name = SectionTitle(sections[i], playerLevel);
            // Una línea en blanco (un carácter invisible: Discord recorta los espacios del final pero no este) al final de cada parte menos la última, para que una zona se separe bien de la siguiente.
            string value = SectionLines(sections[i].Recipes, reserve: i < sections.Count - 1 ? SectionGap.Length : 0);
            if (i < sections.Count - 1)
            {
                value += SectionGap;
            }

            if (used + name.Length + value.Length > EmbedBudget)
            {
                embed.AddField("…", $"Hay más zonas: miralas de a una con `aa forge recipes <zona>`.", false);
                break;
            }

            embed.AddField(name, value, false);
            used += name.Length + value.Length;
        }

        return embed.Build();
    }

    // El espacio entre dos partes del recetario completo: salto de línea + espacio de ancho cero.
    private const string SectionGap = "\n​";

    // EL RECETARIO CLÁSICO (v0.28.1, el que se ve por defecto; el dueño: «a pedido de los jugadores vamos a volver al menú de forge recipes previo… es el diseño viejo pero con lo actual»): UNA zona por página, un bloque por
    // receta —el título lleva el ítem y abajo van lo que suma, el oro y cada ingrediente en su propia línea— y, cuando se conoce el inventario, con lo que tenés de cada material (✅ / ❌ y «tenés X de Y»). Lo que ve cada jugador
    // es lo de su clase (su arma de clase y el amuleto); la Hoja de Acero Puro, que no es de ninguna zona, está en la página de la Zona 1; y con la puerta abierta la última zona suma el equipo del Fogón. Con 2 o 3 recetas por página entra
    // sobrado en los límites de Discord (1024 por campo y 6000 entre todos). El completo (RenderRecipesEmbed) es la otra opción de /config.
    // currentZoneId es la zona A MOSTRAR: la del jugador en «aa forge recipes», o la que elija en el selector de zonas de /forge (ver Modules/BlacksmithModule.cs).
    // playerLevel: si viene y la zona que se muestra pide más nivel, se avisa (🔒). forgeHint: la frase de cómo forjar; la escena de /forge pone la suya (ya estás en la lista), y el resto el comando de siempre.
    public static Embed RenderZonePageEmbed(
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
        var view = RecipeCatalog.PageFor(recipes, zones, playerClass, currentZoneId, gateOpen);
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

        // Sin íconos de espada / amuleto al lado del nombre: el ítem ya trae su propio emoji y dos íconos juntos lo achicaban. Qué tipo es cada uno lo dice una etiqueta bajo el nombre.
        AddRecipes(embed, zoneRecipes, RecipeGroup.ClassWeapon, "arma de tu clase", playerClass, owned, playerGold);
        AddRecipes(embed, zoneRecipes, RecipeGroup.GeneralWeapon, "arma general", playerClass, owned, playerGold);
        AddRecipes(embed, zoneRecipes, RecipeGroup.Amulet, "amuleto", playerClass, owned, playerGold);
        AddRecipes(embed, zoneRecipes, RecipeGroup.Other, "otro", playerClass, owned, playerGold);

        // El equipo de El Fogón Eterno (zona 0): el arma y el amuleto que hay que llevar PUESTOS para entrar. Son caros a propósito (drops de las 5 zonas).
        if (gateRecipes.Count > 0)
        {
            embed.AddField("🔥 El Fogón Eterno", "Para entrar con **/zona 0** hay que llevar puestos los dos. Mirá `/info tema:fogon`.", false);
            AddRecipes(embed, gateRecipes, RecipeGroup.GeneralWeapon, "equipo del Fogón", playerClass, owned, playerGold);
            AddRecipes(embed, gateRecipes, RecipeGroup.Amulet, "equipo del Fogón", playerClass, owned, playerGold);
        }

        return embed.Build();
    }

    // El recetario que le toca a ESTE jugador según su /config (v0.28.1), para «aa forge recipes [zona]»: el clásico (la página de su zona, o de la que pida, con lo que tiene de cada material) o el completo.
    // Lee las recetas, las zonas y el inventario; la elección ya viene resuelta (fullBook), así que se prueba sin Discord.
    public static async Task<Embed> BuildRecipeBookForPlayerAsync(
        IRecipeRepository recipeRepository, IZoneRepository zoneRepository, IInventoryRepository inventoryRepository, User player, bool fullBook, int? zoneId)
    {
        if (fullBook)
        {
            return await BuildRecipesEmbed(recipeRepository, zoneRepository, player.Class, zoneId, player.Level, player.HighestZoneCleared);
        }

        var recipes = await recipeRepository.GetAllAsync();
        var zones = await zoneRepository.GetAllAsync();
        var owned = (await inventoryRepository.GetByDiscordIdAsync((ulong)player.DiscordId)).ToDictionary(e => e.ItemName, e => e.Quantity);
        return RenderZonePageEmbed(recipes, zones, player.Class, zoneId ?? player.CurrentZoneId, owned, player.Gold, player.Level, highestZoneCleared: player.HighestZoneCleared);
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
    // reserve: lugar que hay que dejar libre al final del campo (el espacio entre zonas).
    internal static string SectionLines(IEnumerable<RecipeDetails> recipes, int reserve = 0)
    {
        int fieldBudget = 1024 - reserve;
        var lines = new List<string>();
        int length = 0;
        foreach (var recipe in recipes)
        {
            string line = RecipeLine(recipe);
            if (length + line.Length + 1 > fieldBudget)
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
