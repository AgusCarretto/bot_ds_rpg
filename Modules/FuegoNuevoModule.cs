using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /fuegonuevo y /blessings (v0.12.0, GameData/FuegoNuevoRules.cs y GameData/BlessingCatalog.cs). El Fuego Nuevo es el reinicio voluntario que se habilita al vencer al Asador Eterno:
// volvés al nivel 1 (con la clase que quieras), perdés el equipo y los materiales, y ganás porcentajes permanentes y una bendición. Todo el reinicio es UNA transacción
// (IFuegoNuevoRepository.RenewAsync); acá solo se muestra el resumen, se pide la confirmación en dos pasos (botón → clase) y se cuenta el resultado.
// Las reglas viven en los métodos estáticos de abajo (sin Context) para que "aa fuegonuevo" y "aa bendiciones" hagan exactamente lo mismo. Los botones llevan el id del dueño: nadie usa tu pantalla.
// Este módulo NO tiene [Group] a propósito: con un grupo Discord.Net le antepone su nombre al id de los componentes y los botones dejarían de coincidir.
public class FuegoNuevoModule(
    IFuegoNuevoRepository fuegoNuevoRepository,
    IBlessingRepository blessingRepository,
    ICombatSessionService combatSessions,
    IRaidSessionService raidSessions,
    IGameEvents gameEvents) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("fuegonuevo", "Fuego Nuevo: volvé a empezar con bonus permanentes y una bendición nueva.")]
    public Task HandleFuegoNuevoAsync() => RunAsync(() => ExecuteStatusAsync(fuegoNuevoRepository, blessingRepository, Context.User.Id));

    [SlashCommand("blessings", "Tus bendiciones (una por cada Fuego Nuevo) y la que tengas para elegir.")]
    public Task HandleBlessingsAsync() => RunAsync(() => ExecuteBlessingsAsync(blessingRepository, Context.User.Id));

    private async Task RunAsync(Func<Task<FnResult>> action)
    {
        // Ephemeral: es tu pantalla, con tus materiales y tu equipo; no hace falta mostrársela al canal.
        await DeferAsync(ephemeral: true);

        try
        {
            var result = await action();
            await FollowupAsync(result.PlainMessage, embed: result.Embed, components: result.Components, ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude abrir la pantalla del Fuego Nuevo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // ---- Botones ----

    // Paso 1 → 2: "Hacer Fuego Nuevo" muestra la pantalla para elegir la clase de la vuelta nueva. El número es el contador que se veía al abrir la pantalla.
    [ComponentInteraction("fn_start:*:*")]
    public async Task HandleStartAsync(string ownerRaw, string countRaw)
    {
        if (!await IsOwnerAsync(ownerRaw))
        {
            return;
        }

        await DeferAsync();

        try
        {
            var result = await ExecuteStartAsync(fuegoNuevoRepository, combatSessions, raidSessions, Context.User.Id, ParseCount(countRaw));
            await ShowAsync(result);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude seguir con el Fuego Nuevo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Paso 2 → hecho: elegir la clase ES la confirmación. Ya no se puede deshacer.
    [ComponentInteraction("fn_go:*:*:*")]
    public async Task HandleGoAsync(string ownerRaw, string countRaw, string className)
    {
        if (!await IsOwnerAsync(ownerRaw))
        {
            return;
        }

        await DeferAsync();

        try
        {
            var result = await ExecuteRenewAsync(
                fuegoNuevoRepository, combatSessions, raidSessions, gameEvents, Context.User.Id, ParseCount(countRaw), className);
            await ShowAsync(result);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude completar el Fuego Nuevo. Mirá **/fuegonuevo** para ver si se hizo antes de intentar otra vez.", ephemeral: true);
        }
    }

    [ComponentInteraction("fn_cancel:*")]
    public async Task HandleCancelAsync(string ownerRaw)
    {
        if (!await IsOwnerAsync(ownerRaw))
        {
            return;
        }

        await DeferAsync();
        await ModifyOriginalResponseAsync(props =>
        {
            props.Content = "Lo pensaste mejor: no pasó nada. Cuando quieras, **/fuegonuevo**.";
            props.Embed = null;
            props.Components = new ComponentBuilder().Build();
        });
    }

    // Elegir una de las 3 bendiciones de la oferta.
    [ComponentInteraction("fn_bless:*:*:*")]
    public async Task HandleBlessAsync(string ownerRaw, string noRaw, string key)
    {
        if (!await IsOwnerAsync(ownerRaw))
        {
            return;
        }

        await DeferAsync();

        try
        {
            var result = await ExecuteChooseAsync(blessingRepository, gameEvents, Context.User.Id, ParseCount(noRaw), key);
            await ShowAsync(result);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude guardar tu bendición. Mirá **/blessings** para ver si quedó antes de intentar otra vez.", ephemeral: true);
        }
    }

    private async Task<bool> IsOwnerAsync(string ownerRaw)
    {
        if (ulong.TryParse(ownerRaw, out ulong owner) && owner == Context.User.Id)
        {
            return true;
        }

        await RespondAsync("Esa pantalla es de otro jugador: abrí la tuya con **/fuegonuevo**.", ephemeral: true);
        return false;
    }

    private static int ParseCount(string raw) => int.TryParse(raw, out int value) ? value : -1;

    // Un resultado con pantalla (embed) reemplaza al mensaje del botón; un rechazo (solo texto) NO lo pisa: se dice aparte y solo para quien clickeó. Si no, un segundo click que llega
    // tarde ("esa pantalla ya no vale") taparía la pantalla del éxito con sus bendiciones por elegir.
    private async Task ShowAsync(FnResult result)
    {
        if (result.Embed is null)
        {
            await FollowupAsync(result.PlainMessage, ephemeral: true);
            return;
        }

        await ModifyOriginalResponseAsync(props =>
        {
            props.Content = result.PlainMessage;
            props.Embed = result.Embed;
            props.Components = result.Components ?? new ComponentBuilder().Build();
        });
    }

    // ---- Lógica compartida con "aa fuegonuevo" / "aa bendiciones" ----

    // PlainMessage solo (rechazo o aviso) o Embed (pantalla) con sus botones.
    public sealed record FnResult(string? PlainMessage, Embed? Embed, MessageComponent? Components = null);

    public const string NoAccount = "Todavía no tenés cuenta: empezá con **/start**.";
    public const string BusyMessage = "No podés hacer esto en medio de un combate o un raid. Terminalo (atacando o huyendo) primero.";

    // /fuegonuevo: el estado. Con el Asador vencido, el resumen de lo que se va y lo que se queda con el botón; sin él, qué falta y lo que ya tenés. Si hay una bendición por elegir, sus botones van abajo.
    public static async Task<FnResult> ExecuteStatusAsync(IFuegoNuevoRepository fuegoNuevoRepository, IBlessingRepository blessingRepository, ulong discordId)
    {
        var preview = await fuegoNuevoRepository.GetPreviewAsync(discordId);
        if (preview is null)
        {
            return new FnResult(NoAccount, null);
        }

        var offer = await blessingRepository.GetPendingOfferAsync(discordId);
        var levels = await blessingRepository.GetLevelsAsync(discordId);
        return new FnResult(null, BuildStatusEmbed(preview, offer, levels), BuildStatusButtons(discordId, preview, offer, levels));
    }

    // /blessings: las que tenés, las que existen y la que tengas para elegir.
    public static async Task<FnResult> ExecuteBlessingsAsync(IBlessingRepository blessingRepository, ulong discordId)
    {
        var levels = await blessingRepository.GetLevelsAsync(discordId);
        var offer = await blessingRepository.GetPendingOfferAsync(discordId);
        return new FnResult(null, BuildBlessingsEmbed(levels, offer), BuildOfferButtons(discordId, offer, levels));
    }

    // Paso 1 → 2: vuelve a comprobar (la pantalla pudo quedar vieja) y, si sigue en pie, pide la clase.
    public static async Task<FnResult> ExecuteStartAsync(
        IFuegoNuevoRepository fuegoNuevoRepository, ICombatSessionService combatSessions, IRaidSessionService raidSessions, ulong discordId, int expectedCount)
    {
        if (combatSessions.Peek(discordId) is not null || raidSessions.IsInAnyRaid(discordId))
        {
            return new FnResult(BusyMessage, null);
        }

        var preview = await fuegoNuevoRepository.GetPreviewAsync(discordId);
        if (preview is null)
        {
            return new FnResult(NoAccount, null);
        }

        if (!preview.GateCleared || preview.FuegoNuevo != expectedCount)
        {
            return new FnResult(StaleMessage, null);
        }

        return new FnResult(null, BuildClassPickEmbed(preview), BuildClassPickButtons(discordId, preview));
    }

    public const string StaleMessage = "Esa pantalla ya no vale (¿ya hiciste el Fuego Nuevo, o todavía no vencés al Asador?). Mirá **/fuegonuevo**.";

    // Paso 2 → hecho. Todo el reinicio es la transacción del repositorio; acá solo se traduce el resultado.
    public static async Task<FnResult> ExecuteRenewAsync(
        IFuegoNuevoRepository fuegoNuevoRepository, ICombatSessionService combatSessions, IRaidSessionService raidSessions, IGameEvents gameEvents,
        ulong discordId, int expectedCount, string newClass)
    {
        // Misma regla que la tienda y las cajas: en plena pelea no. (Es una comprobación en memoria; la transacción no depende de ella.)
        if (combatSessions.Peek(discordId) is not null || raidSessions.IsInAnyRaid(discordId))
        {
            return new FnResult(BusyMessage, null);
        }

        var outcome = await fuegoNuevoRepository.RenewAsync(discordId, expectedCount, newClass);
        switch (outcome.Status)
        {
            case RenewStatus.Ok:
                break;
            case RenewStatus.NotCleared or RenewStatus.StaleCount:
                return new FnResult(StaleMessage, null);
            case RenewStatus.InvalidClass:
                return new FnResult("Esa clase no existe: elegí una de las cuatro de **/fuegonuevo**.", null);
            default:
                return new FnResult(NoAccount, null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.FuegoNuevo, amount: 1, detail: newClass);

        var embed = BuildRenewedEmbed(outcome, newClass);
        return new FnResult(null, embed, BuildOfferButtons(discordId, outcome.Offer, new Dictionary<string, int>()));
    }

    // Elegir una bendición de la oferta. La transacción del repositorio decide (oferta vigente, que no haya elegido ya, nivel máximo).
    public static async Task<FnResult> ExecuteChooseAsync(IBlessingRepository blessingRepository, IGameEvents gameEvents, ulong discordId, int fuegoNuevoNo, string key)
    {
        var outcome = await blessingRepository.ChooseAsync(discordId, fuegoNuevoNo, key);
        switch (outcome.Status)
        {
            case ChooseStatus.Ok:
                break;
            case ChooseStatus.AlreadyChosen:
                return new FnResult("Esa bendición ya está elegida: cada Fuego Nuevo da una sola. Mirá las tuyas con **/blessings**.", null);
            case ChooseStatus.MaxLevel:
                return new FnResult("Esa bendición ya está al nivel máximo.", null);
            case ChooseStatus.NoAccount:
                return new FnResult(NoAccount, null);
            default:
                return new FnResult("Esa bendición no estaba en tu oferta: mirá la tuya con **/blessings**.", null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.BlessingChosen, amount: 1, detail: key);

        var definition = BlessingCatalog.Find(key)!;
        var embed = new EmbedBuilder()
            .WithTitle("🙏 ¡Bendición elegida!")
            .WithColor(Color.Gold)
            .WithDescription(
                $"**{BlessingCatalog.Label(definition, outcome.NewLevel)}**\n{BlessingCatalog.Describe(definition, outcome.NewLevel)}" +
                (outcome.PetFoodGranted > 0 || outcome.BoxesGranted > 0
                    ? $"\n\n🎒 La Alforja te dejó **{outcome.PetFoodGranted}× {PetRules.FoodItemName}** y **{outcome.BoxesGranted}× {BlessingCatalog.SatchelBoxName}** en la mochila."
                    : string.Empty) +
                "\n\nMirá todas las tuyas con **/blessings**.")
            .Build();

        return new FnResult(null, embed);
    }

    // ---- Armado de pantallas (puro: se prueba sin Discord) ----

    public static Embed BuildStatusEmbed(RenewPreview preview, BlessingOffer? offer, IReadOnlyDictionary<string, int> levels)
    {
        int next = preview.FuegoNuevo + 1;
        var embed = new EmbedBuilder()
            .WithTitle(preview.FuegoNuevo > 0 ? $"🔥 Fuego Nuevo — llevás {preview.FuegoNuevo}" : "🔥 Fuego Nuevo")
            .WithColor(Color.Orange);

        if (preview.GateCleared)
        {
            embed
                .WithDescription(
                    "Venciste al **Asador Eterno**: podés **volver a empezar**. Arrancás de nivel 1 (con la clase que quieras) y sin equipo ni materiales, pero ganás porcentajes " +
                    "**permanentes** y una **bendición** nueva. Es voluntario: si preferís, quedate en la última zona con tu mejor equipo.")
                .AddField($"🔥 Lo que ganás (Fuego Nuevo ×{next})", FuegoNuevoRules.BonusLines(next) + "\n🙏 Una bendición a elegir entre 3", false)
                .AddField("💨 Lo que se va", Cut(LostText(preview)), false)
                .AddField("🔒 Lo que se queda", Cut(KeptText(preview)), false)
                .WithFooter("No se puede deshacer, y lo que se va no se reembolsa.");

            // Lo que ya te da hoy (desde la v0.14.3 el perfil no lo muestra: se mira acá).
            if (preview.FuegoNuevo > 0)
            {
                embed.AddField($"🔥 Lo que te da hoy (Fuego Nuevo ×{preview.FuegoNuevo})", FuegoNuevoRules.BonusLines(preview.FuegoNuevo), false);
            }
        }
        else
        {
            embed.WithDescription(preview.FuegoNuevo == 0
                ? "El **Fuego Nuevo** es volver a empezar con porcentajes **permanentes** y una **bendición** por cada vuelta. Se habilita al vencer al **Asador Eterno** en **El Fogón Eterno** " +
                  "(la Zona 0, se abre al vencer al jefe de la última zona): mirá **/zonas** y **/info tema:fuego**."
                : "Para el próximo Fuego Nuevo hay que volver a vencer al **Asador Eterno** en **El Fogón Eterno** (el equipo del Fogón se forja de nuevo en cada vuelta).");

            if (preview.FuegoNuevo > 0)
            {
                embed.AddField($"🔥 Lo que te da hoy (Fuego Nuevo ×{preview.FuegoNuevo})", FuegoNuevoRules.BonusLines(preview.FuegoNuevo), false);
            }
        }

        // La historia (GameData/Lore.cs): cuánto pesar le queda al Asador. Una línea; el relato completo está en /story.
        if (preview.FuegoNuevo > 0)
        {
            embed.AddField("📖 El Asador", $"{StoryModule.PesarLine(preview.FuegoNuevo)}\n{ProgressBar.Render(Lore.Pesar(preview.FuegoNuevo), 100, 20)}\nLeé lo que el fuego recuerda con `/story`.", false);
        }

        // Las bendiciones que ya juntó (desde la v0.14.3 el perfil no las lista: los bufs del Fuego Nuevo se miran acá; el detalle de cada una y las que existen están en /blessings).
        var ownedBlessings = BlessingCatalog.OwnedLines(levels);
        if (ownedBlessings.Count > 0)
        {
            embed.AddField("🙏 Tus bendiciones", Cut(string.Join('\n', ownedBlessings)), false);
        }

        if (offer is not null)
        {
            embed.AddField("🙏 Elegí tu bendición", Cut(OfferText(offer, levels)), false);
        }

        return embed.Build();
    }

    public static MessageComponent BuildStatusButtons(ulong owner, RenewPreview preview, BlessingOffer? offer, IReadOnlyDictionary<string, int> levels)
    {
        var components = new ComponentBuilder();
        if (preview.GateCleared)
        {
            components.WithButton("Hacer Fuego Nuevo", $"fn_start:{owner}:{preview.FuegoNuevo}", ButtonStyle.Danger, new Emoji("🔥"), row: 0);
        }

        AddOfferButtons(components, owner, offer, levels, row: 1);
        return components.Build();
    }

    public static Embed BuildClassPickEmbed(RenewPreview preview)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"🔥 ¿Con qué clase arrancás la vuelta {preview.FuegoNuevo + 2}?")
            .WithColor(Color.Red)
            .WithDescription(
                "Tocar una clase es **confirmar**: se borra el nivel, el equipo y los materiales, y empezás de nuevo en la Zona 1. **No se puede deshacer.**\n" +
                $"Hoy sos **{preview.Class}**: podés repetir o probar otra.");

        foreach (var classDef in ClassCatalog.All)
        {
            embed.AddField($"{classDef.Emoji} {classDef.Name} — {classDef.WeaponType}", classDef.Description, false);
        }

        return embed.Build();
    }

    public static MessageComponent BuildClassPickButtons(ulong owner, RenewPreview preview)
    {
        var components = new ComponentBuilder();
        foreach (var classDef in ClassCatalog.All)
        {
            components.WithButton(
                classDef.Name == preview.Class ? $"{classDef.Name} (la tuya)" : classDef.Name,
                $"fn_go:{owner}:{preview.FuegoNuevo}:{classDef.Name}", ButtonStyle.Primary, new Emoji(classDef.Emoji), row: 0);
        }

        components.WithButton("Cancelar", $"fn_cancel:{owner}", ButtonStyle.Secondary, new Emoji("✖️"), row: 0);
        return components.Build();
    }

    public static Embed BuildRenewedEmbed(RenewOutcome outcome, string newClass)
    {
        var classDef = ClassCatalog.All.First(c => c.Name == newClass);
        var embed = new EmbedBuilder()
            .WithTitle($"🔥 ¡FUEGO NUEVO ×{outcome.NewNumber}! 🔥")
            .WithColor(Color.Gold)
            .WithDescription(
                $"Volviste a empezar como **{classDef.Emoji} {classDef.Name}** en la Zona 1. Esto es lo que ganás ahora, para siempre:\n\n{FuegoNuevoRules.BonusLines(outcome.NewNumber)}");

        if (outcome.SatchelPetFood > 0 || outcome.SatchelBoxes > 0)
        {
            embed.AddField("🎒 Alforja del Fogonero", $"Te dejó **{outcome.SatchelPetFood}× {PetRules.FoodItemName}** y **{outcome.SatchelBoxes}× {BlessingCatalog.SatchelBoxName}** en la mochila.", false);
        }

        if (outcome.Offer is not null)
        {
            embed.AddField("🙏 Elegí tu bendición", Cut(OfferText(outcome.Offer, new Dictionary<string, int>())), false);
        }
        else
        {
            embed.AddField("🙏 Bendiciones", "Ya tenés todas las bendiciones al nivel máximo: no queda ninguna por elegir.", false);
        }

        // La historia: cada vuelta le saca un poco de pesar al Asador (las escenas nuevas llegan como aviso aparte, al terminar el comando).
        embed.WithFooter($"El Asador carga ahora con el {Lore.Pesar(outcome.NewNumber)} % de su pesar · /story");

        return embed.Build();
    }

    public static Embed BuildBlessingsEmbed(IReadOnlyDictionary<string, int> levels, BlessingOffer? offer)
    {
        var owned = BlessingCatalog.OwnedLines(levels);
        var embed = new EmbedBuilder()
            .WithTitle("🙏 Tus bendiciones")
            .WithColor(Color.Gold)
            .WithDescription(owned.Count == 0
                ? "Todavía no tenés ninguna. Llega **una por cada Fuego Nuevo** (**/fuegonuevo**), elegida entre 3. Si repetís una, sube de nivel (hasta el " + BlessingCatalog.RomanLevel(BlessingCatalog.MaxLevel) + ")."
                : string.Join('\n', owned));

        if (offer is not null)
        {
            embed.AddField("🎁 Elegí una", Cut(OfferText(offer, levels)), false);
        }

        // Todas las que existen, con lo que da cada NIVEL (para decidir qué conviene). Las del poder son chicas a propósito y no cuentan en duelos ni en la Arena.
        string pool = string.Join('\n', BlessingCatalog.All.Select(b => $"{b.Emoji} **{b.Name}** — {b.PerLevel}"));
        embed.AddField("📖 Las que existen (por nivel)", Cut(pool), false);
        return embed.WithFooter("Las bendiciones son permanentes: se quedan con cada Fuego Nuevo. Lo de ataque, defensa y vida solo vale contra monstruos.").Build();
    }

    // Los botones de la oferta (uno por bendición), en la fila 0 de su mensaje. Sin oferta pendiente no hay botones.
    public static MessageComponent BuildOfferButtons(ulong owner, BlessingOffer? offer, IReadOnlyDictionary<string, int> levels)
    {
        var components = new ComponentBuilder();
        AddOfferButtons(components, owner, offer, levels, row: 0);
        return components.Build();
    }

    private static void AddOfferButtons(ComponentBuilder components, ulong owner, BlessingOffer? offer, IReadOnlyDictionary<string, int> levels, int row)
    {
        if (offer is null)
        {
            return;
        }

        foreach (string key in offer.Keys)
        {
            var definition = BlessingCatalog.Find(key);
            if (definition is null)
            {
                continue;
            }

            levels.TryGetValue(key, out int have);
            // Con emoji unicode (nunca un emoji personalizado: si el bot no lo puede usar, Discord rechaza el mensaje entero).
            components.WithButton(
                $"{definition.Name} {BlessingCatalog.RomanLevel(Math.Min(BlessingCatalog.MaxLevel, have + 1))}",
                $"fn_bless:{owner}:{offer.FuegoNuevoNo}:{key}", ButtonStyle.Success, new Emoji(definition.Emoji), row: row);
        }
    }

    // Las 3 opciones con lo que daría cada una (al nivel al que quedaría).
    private static string OfferText(BlessingOffer offer, IReadOnlyDictionary<string, int> levels)
    {
        var lines = new List<string>();
        foreach (string key in offer.Keys)
        {
            var definition = BlessingCatalog.Find(key);
            if (definition is null)
            {
                continue;
            }

            levels.TryGetValue(key, out int have);
            int level = Math.Min(BlessingCatalog.MaxLevel, have + 1);
            lines.Add($"{BlessingCatalog.Label(definition, level)}{(have > 0 ? " _(sube de nivel)_" : string.Empty)}\n{BlessingCatalog.Describe(definition, level)}");
        }

        return string.Join("\n\n", lines);
    }

    private static string LostText(RenewPreview preview)
    {
        var lines = new List<string>
        {
            $"📈 Tu nivel {preview.Level} y toda tu EXP (volvés al nivel 1)",
            "🗺️ Tu zona y los jefes que ya venciste (volvés a la Zona 1)",
            $"⚔️ Tu arma: {GearText(preview.WeaponName, "weapon", preview.WeaponEnchant)}",
            $"🛡️ Tu amuleto: {GearText(preview.AmuletName, "amulet", preview.AmuletEnchant)}",
        };

        if (preview.Dust > 0)
        {
            lines.Add($"✨ {GameHistory.Number(preview.Dust)} de Polvo");
        }

        foreach (var (type, units) in preview.LostByType.OrderBy(pair => TypeOrder(pair.Key)).ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            lines.Add($"{TypeLabel(type)}: {GameHistory.Number(units)}");
        }

        lines.Add("🍖 Los banquetes activos y los enfriamientos de la vuelta");
        return string.Join('\n', lines);
    }

    private static string KeptText(RenewPreview preview)
    {
        var lines = new List<string>
        {
            "💰 Tu oro" + (preview.HasBank ? $" y el del banco ({GameHistory.Number(preview.Gold)} + {GameHistory.Number(preview.BankGold)})" : $" ({GameHistory.Number(preview.Gold)})"),
            "🐾 Tus mascotas" + (preview.Pets > 0 ? $" ({preview.Pets})" : string.Empty)
                + (preview.KeptEggs > 0 ? $", tus huevos ({GameHistory.Number(preview.KeptEggs)})" : string.Empty)
                + (preview.KeptPetFood > 0 ? $" y su comida ({GameHistory.Number(preview.KeptPetFood)})" : string.Empty),
            "🏆 Tus logros y las misiones ya cobradas",
            "🎁 Tu racha diaria",
        };

        if (preview.Blessings > 0)
        {
            lines.Add($"🙏 Tus bendiciones ({preview.Blessings}) y tus Fuegos Nuevos");
        }

        return string.Join('\n', lines);
    }

    private static string GearText(string? name, string slot, int enchant) =>
        name is null ? "_ninguno_" : $"{name}" + (Enchantments.IsValidTier(enchant) ? $" (✨ {Enchantments.Label(slot, enchant)})" : string.Empty);

    private static string TypeLabel(string type) => type switch
    {
        "Madera" => "🪵 Madera",
        "Mineral" => "⛏️ Minerales",
        "Material" => "🩸 Drops de monstruo",
        "Consumable" => "🍖 Consumibles",
        "Caja" => "📦 Cajas",
        _ => $"📦 {type}",
    };

    private static int TypeOrder(string type) => type switch { "Madera" => 0, "Mineral" => 1, "Material" => 2, "Consumable" => 3, "Caja" => 4, _ => 5 };

    // Un campo de embed admite 1024 caracteres: lo que se pase se corta con "..." en vez de hacer que Discord rechace el mensaje entero.
    private static string Cut(string text) => text.Length <= 1024 ? text : text[..1021] + "...";
}
