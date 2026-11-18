using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /cook (aa cook, aa cook recipes) — la cocina (v0.49.0, diseño: docs/superpowers/specs/2026-11-05-cocina-design.md). Un plato se cocina y se come en el momento; los permanentes suman su stat y se apilan hasta el
// próximo Fuego Nuevo y los de 60 minutos dan un bufo que corre por reloj (GameData/CookingCatalog.cs). Las reglas y el descuento están en CookingRepository (una transacción); esto es la cara del comando: pedir,
// contestar y registrar el evento. El comando de texto llama exactamente a ExecuteCookAsync, BuildSceneAsync y BuildRecipesEmbedAsync.
public sealed record CookReply(string? PlainMessage, Embed? Embed);

// AttachmentPath: el archivo de la foto del Cocinero que hay que adjuntar al mensaje (null si no hay foto o es una URL).
public sealed record CookScene(Embed Embed, MessageComponent Components, string? AttachmentPath = null);

// LA COCINA COMO ESCENA (2026-11-18, el dueño: «que el /cook quede como la taberna… que no aparezca una lista de todo sino de las cosas y lo que te dan, un check si se puede cocinar pero no TODO… la lista completa
// que sea cook recipes, como en forge»). /cook sin plato y «aa cook» muestran al Cocinero con cada plato en UNA línea (✅/❌, el plato y lo que da) y debajo una lista desplegable: elegís un plato y se cocina UNO (para
// varios de una, /cook plato:… veces:… o «aa cook <plato> <veces>»). La escena se refresca y el resultado sale en un mensaje aparte, justo debajo. Los ingredientes de cada plato están en «aa cook recipes» y en el
// botón 📖 Recetas (te las muestra solo a vos). Por debajo es exactamente ExecuteCookAsync.
public class CookModule(
    ICookingRepository cooking, IInventoryRepository inventory, IGameEvents gameEvents,
    ICombatSessionService combatSessions, IRaidSessionService raidSessions, IDuelService duels, IDungeonService dungeons, IAdventureService adventures)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string Pick = "cook_pick";
    private const string Recipes = "cook_recipes";

    [SlashCommand("cook", "Cociná un plato con tus pescados y materiales (sin plato, entrás a la cocina).")]
    public async Task HandleAsync(
        [Summary("plato", "Qué cocinar (si lo dejás vacío entrás a la cocina).")] [Autocomplete(typeof(CookDishAutocompleteHandler))] string? plato = null,
        [Summary("veces", "Cuántos platos (de 1 a 10).")] [MinValue(1)] [MaxValue(CookingCatalog.MaxTimes)] int veces = 1)
    {
        await DeferAsync();

        try
        {
            if (string.IsNullOrWhiteSpace(plato))
            {
                var scene = await BuildSceneAsync(inventory, cooking, Context.User.Id);
                if (scene.AttachmentPath is { } file)
                {
                    await FollowupWithFileAsync(file, embed: scene.Embed, components: scene.Components);
                }
                else
                {
                    await FollowupAsync(embed: scene.Embed, components: scene.Components);
                }

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

    // Elegiste un plato de la lista: se cocina UNO. El id lleva a quién pertenece la cocina (nadie cocina con los ingredientes de otro).
    [ComponentInteraction($"{Pick}:*")]
    public async Task HandlePickAsync(string ownerRaw, string[] selected)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Esa cocina es de otra persona: abrí la tuya con **/cook**."), ephemeral: true);
                return;
            }

            // Los menús no pasan por el bloqueo de pelea de Program.cs (solo frena comandos): acá se aplica la misma regla que a /cook.
            if (adventures.IsInAdventure(Context.User.Id))
            {
                await FollowupAsync(FightLock.AdventureMessage("/cook"), ephemeral: true);
                return;
            }

            if (FightGuard.IsInFight(combatSessions, raidSessions, duels, Context.User.Id, dungeons))
            {
                await FollowupAsync(FightLock.Message("/cook"), ephemeral: true);
                return;
            }

            var reply = await ExecuteCookAsync(cooking, gameEvents, Context.User.Id, selected.FirstOrDefault() ?? string.Empty, 1);

            // La escena se refresca (los ✅/❌, lo cocinado, los relojes) y el resultado va en un mensaje aparte.
            var scene = await BuildSceneAsync(inventory, cooking, Context.User.Id);
            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = scene.Embed;
                p.Components = scene.Components;
            });

            await FollowupAsync(reply.PlainMessage, embed: reply.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude cocinar ahora mismo, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // 📖 Recetas: los ingredientes de cada plato (lo mismo que «aa cook recipes»), solo para quien abrió la cocina y en privado (no ensucia el canal).
    [ComponentInteraction($"{Recipes}:*")]
    public async Task HandleRecipesAsync(string ownerRaw)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Esa cocina es de otra persona: abrí la tuya con **/cook**."), ephemeral: true);
                return;
            }

            await FollowupAsync(embed: await BuildRecipesEmbedAsync(inventory, cooking, Context.User.Id), ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude mostrarte las recetas, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // Para cuántos platos te alcanza: lo que limita es el ingrediente (o el carbón) que menos rinde.
    private static int CanCook(Dish dish, IReadOnlyDictionary<string, int> have) =>
        dish.Ingredients.Append(new DishIngredient(CookingCatalog.FuelItemName, dish.Fuel))
            .Where(i => i.Quantity > 0)
            .Min(i => have.GetValueOrDefault(i.ItemName) / i.Quantity);

    // La escena de la cocina: el Cocinero (su foto si hay, NpcImages.Cook), cada plato en UNA línea (✅ si te alcanza para uno, ❌ si no; el plato y lo que da; en los de tiempo, 🟢 y cuánto le queda si está activo),
    // partidos en permanentes y de tiempo, y debajo la lista para cocinar y el botón de las recetas. SIN ingredientes: eso es «aa cook recipes». Pública y sin Context: la usa también «aa cook».
    public static async Task<CookScene> BuildSceneAsync(IInventoryRepository inventory, ICookingRepository cooking, ulong discordId, Random? rng = null)
    {
        var have = (await inventory.GetByDiscordIdAsync(discordId)).GroupBy(e => e.ItemName).ToDictionary(g => g.Key, g => g.Sum(e => e.Quantity), StringComparer.Ordinal);
        var cooked = await cooking.GetCookedAsync(discordId);
        var active = await cooking.GetActiveBuffsAsync(discordId);

        string Line(Dish dish)
        {
            string line = $"{(CanCook(dish, have) > 0 ? "✅" : "❌")} {dish.Icon} **{dish.Name}**: {CookingCatalog.EffectLine(dish)}";
            return active.TryGetValue(dish.Key, out var left) ? $"{line} · 🟢 {TimeFormat.Remaining(left)}" : line;
        }

        int hp = CookingCatalog.Sum(cooked, d => d.MaxHp), def = CookingCatalog.Sum(cooked, d => d.Defense), atk = CookingCatalog.Sum(cooked, d => d.Attack);
        string total = string.Join(" · ", new[] { hp > 0 ? Loc.F($"+{hp} de vida máxima") : null, def > 0 ? Loc.F($"+{def} de DEF") : null, atk > 0 ? Loc.F($"+{atk} de ATQ") : null }.Where(t => t is not null));

        var embed = new EmbedBuilder()
            .WithTitle(Loc.T("🍳 La cocina"))
            .WithColor(Color.Orange)
            .WithDescription(
                NpcDialogue.Cook(CookLine.Greeting, rng) + "\n\n" +
                Loc.T("Elegí un plato en la lista y se cocina **uno**; para varios, **/cook** plato:… veces:…. Los ingredientes: **📖 Recetas** o **aa cook recipes**.") +
                (total.Length == 0 ? string.Empty : "\n\n" + Loc.F($"Llevás cocinado en esta vuelta: **{total}**.")))
            .AddField(Loc.T("Permanentes (hasta el próximo Fuego Nuevo)"), string.Join("\n", CookingCatalog.All.Where(d => d.Kind == DishKind.Permanent).Select(Line)), false)
            .AddField(Loc.F($"De {CookingCatalog.TimedMinutes} minutos"), string.Join("\n", CookingCatalog.All.Where(d => d.Kind == DishKind.Timed).Select(Line)), false)
            .WithFooter(Loc.T("Solo cuentan contra monstruos; el Fuego Nuevo borra lo cocinado"));

        var built = NpcImages.Decorate(embed.Build(), NpcImages.Cook);
        return new CookScene(built, BuildComponents(discordId, have), NpcImages.AttachmentPathFor(built));
    }

    // La lista desplegable (un plato por opción, en el orden del catálogo, con lo que da y para cuántos te alcanza) y el botón de las recetas. El valor de cada opción es la clave del plato (CookingCatalog.Find la
    // acepta) y el emoji, el arte del plato si la aplicación lo tiene (un emoji de la propia aplicación nunca hace rechazar el mensaje) o el 🍳.
    private static MessageComponent BuildComponents(ulong discordId, IReadOnlyDictionary<string, int> have)
    {
        var menu = new SelectMenuBuilder()
            .WithCustomId($"{Pick}:{discordId}")
            .WithPlaceholder(Loc.T("Cocinar un plato (de a uno)"))
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var dish in CookingCatalog.All)
        {
            int canCook = CanCook(dish, have);
            string effect = CookingCatalog.EffectLine(dish);
            string description = canCook > 0 ? Loc.F($"✅ Te alcanza para {canCook} · {effect}") : Loc.F($"❌ Te faltan ingredientes · {effect}");
            string label = dish.Kind == DishKind.Timed ? $"{dish.Name} · {CookingCatalog.TimedMinutes} min" : dish.Name;
            IEmote emote = Emote.TryParse(dish.Icon, out var custom) ? custom : new Emoji("🍳");
            menu.AddOption(Cut(label), dish.Key, Cut(description), emote);
        }

        return new ComponentBuilder()
            .WithSelectMenu(menu, row: 0)
            .WithButton(Loc.T("Recetas"), $"{Recipes}:{discordId}", ButtonStyle.Secondary, new Emoji("📖"), row: 1)
            .Build();
    }

    private static string Cut(string text) => text.Length <= 100 ? text : text[..99] + "…";

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

        // Permanente: lo que llevás apilado. De tiempo: cuánto te queda (cada plato suma su reloj, el efecto no se multiplica).
        string detail = dish.Kind == DishKind.Timed
            ? Loc.F($"Cocinaste y te comiste **{times}×** {dish.Name}: {CookingCatalog.EffectLine(dish)}.") + "\n\n" +
              Loc.F($"Te quedan **{TimeFormat.Remaining(outcome.Remaining ?? TimeSpan.Zero)}** de este efecto (cada plato suma {CookingCatalog.TimedMinutes} minutos y se pueden tener varios platos a la vez).")
            : Loc.F($"Cocinaste y te comiste **{times}×** {dish.Name}: {CookingCatalog.EffectLine(dish, times)}.") + "\n\n" +
              Loc.F($"Llevás **{outcome.NewCount}** de este plato en esta vuelta: **{CookingCatalog.EffectLine(dish, outcome.NewCount)}** en total, hasta el próximo Fuego Nuevo.");

        // El título va en texto plano (un emoji propio no se garantiza en un título) y el arte del plato, que es el protagonista del mensaje, va grande de miniatura.
        var embed = new EmbedBuilder()
            .WithTitle($"🍳 {dish.Name}")
            .WithColor(Color.Orange)
            .WithDescription(detail)
            .WithFooter(Loc.T("Solo cuenta contra monstruos"));
        if (ItemDisplay.ImageUrl(dish.Icon, 256) is { } dishImage)
        {
            embed.WithThumbnailUrl(dishImage);
        }

        return new CookReply(null, embed.Build());
    }

    // Las recetas: cada plato con su efecto y los ingredientes con lo que tenés y lo que pide (✅/❌), el combustible aparte, lo que ya cocinaste esta vuelta y, en los de tiempo, cuánto les queda. Si con los emojis de los
    // ítems el mensaje se pasara de los 6000 caracteres de Discord, se rehace con solo los nombres (nunca se rompe).
    public static async Task<Embed> BuildRecipesEmbedAsync(IInventoryRepository inventory, ICookingRepository cooking, ulong discordId)
    {
        var have = (await inventory.GetByDiscordIdAsync(discordId)).GroupBy(e => e.ItemName).ToDictionary(g => g.Key, g => g.Sum(e => e.Quantity), StringComparer.Ordinal);
        var cooked = await cooking.GetCookedAsync(discordId);
        var active = await cooking.GetActiveBuffsAsync(discordId);

        Embed Build(bool withEmojis)
        {
            string Name(string item) => withEmojis ? ItemEmojiRegistry.Label(item, string.Empty) : item;
            string Line(DishIngredient ing)
            {
                int owned = have.GetValueOrDefault(ing.ItemName);
                return $"{(owned >= ing.Quantity ? "✅" : "❌")} {Name(ing.ItemName)} {owned}/{ing.Quantity}";
            }

            var embed = new EmbedBuilder()
                .WithTitle(Loc.T("🍳 La cocina"))
                .WithColor(Color.Orange)
                .WithFooter(Loc.T("Solo cuentan contra monstruos; el Fuego Nuevo borra lo cocinado"));

            int hp = CookingCatalog.Sum(cooked, d => d.MaxHp), def = CookingCatalog.Sum(cooked, d => d.Defense), atk = CookingCatalog.Sum(cooked, d => d.Attack);
            string total = string.Join(" · ", new[] { hp > 0 ? Loc.F($"+{hp} de vida máxima") : null, def > 0 ? Loc.F($"+{def} de DEF") : null, atk > 0 ? Loc.F($"+{atk} de ATQ") : null }.Where(t => t is not null));
            embed.WithDescription(
                Loc.T("Cada plato se **cocina y se come en el momento**: elegilo en la lista de **/cook** (de a uno) o pedí varios con **/cook** plato:… veces:… (o **aa cook <plato> [veces]**). Los permanentes se apilan hasta el próximo Fuego Nuevo.") + "\n" +
                Loc.F($"Los de tiempo duran {CookingCatalog.TimedMinutes} minutos, corren a la vez y comer el mismo otra vez suma otros {CookingCatalog.TimedMinutes}.") + "\n" +
                Loc.T("Cada ingrediente muestra **lo que tenés / lo que pide** (✅ alcanza, ❌ falta); el combustible es el carbón que se gasta al cocinar.") +
                (total.Length == 0 ? string.Empty : "\n\n" + Loc.F($"Llevás cocinado en esta vuelta: **{total}**.")));

            foreach (var dish in CookingCatalog.All)
            {
                // El nombre del campo es texto plano; el ícono del plato (un emoji propio) va en el VALOR, en la primera línea, junto a lo que suma.
                string lines = $"{dish.Icon} **{CookingCatalog.EffectLine(dish)}**\n" + string.Join("\n", dish.Ingredients.Select(Line)) + "\n" + Loc.F($"🔥 Combustible: {Line(new DishIngredient(CookingCatalog.FuelItemName, dish.Fuel))}");
                string title = dish.Name;
                if (dish.Kind == DishKind.Timed)
                {
                    title += $" · {CookingCatalog.TimedMinutes} min";
                    if (active.TryGetValue(dish.Key, out var left))
                    {
                        lines += "\n" + Loc.F($"🟢 Activo: te quedan {TimeFormat.Remaining(left)}");
                    }
                }
                else if (cooked.GetValueOrDefault(dish.Key) is int count and > 0)
                {
                    lines += "\n" + Loc.F($"🍽️ Llevás {count} cocinados");
                }

                embed.AddField(title, lines, false);
            }

            return embed.Build();
        }

        var full = Build(withEmojis: true);
        return full.Length <= 5800 ? full : Build(withEmojis: false);
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
