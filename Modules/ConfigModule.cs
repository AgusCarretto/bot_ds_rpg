using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /config (aa config, alias conf / set / settings / ajustes): cómo querés VER el bot (v0.26.1). Dos opciones: mostrar u ocultar la tablita del resumen del combate (turnos, daño hecho y recibido) que sale al
// terminar una pelea de /hunt, /travel, /boss y /autohunt, y (v0.28.1) qué recetario ves en la herrería: el clásico (una zona por página, con lo que tenés de cada material) o el completo de la v0.27.0 (todas las zonas
// juntas). Nada de esto toca el juego, solo lo que se muestra. Se arma como una pantalla con un botón por opción (un click la cambia y la pantalla se actualiza en su lugar) y también se puede escribir:
// «aa config resumen off», «aa config recetas completo». Las opciones viven en GameData/PlayerSettings.cs y se guardan en player_settings (Services/PlayerSettingsService.cs); el idioma tiene su propio comando
// (/language). La lógica es estática y sin Context: el slash, el botón y el comando de texto llaman a lo mismo.
// Banner del perfil (v0.50.0): un menú con los banners que desbloqueaste (uno por bendición que tenés, GameData/ProfileBanners.cs) y «Sin banner»; también «aa config banner mano de leñador» y «aa banner ninguno».
public sealed record ConfigScreen(Embed Embed, MessageComponent Components);

public enum BannerSetStatus { Saved, NotOwned, Failed }

public class ConfigModule(IPlayerSettingsService settings, IBlessingRepository blessings) : InteractionModuleBase<SocketInteractionContext>
{
    // El id del botón lleva a quién pertenece la pantalla y el valor al que PASA al tocarlo (on / off; full / classic): nadie puede cambiar la configuración de otro con una pantalla vieja o ajena.
    public const string SummaryButton = "config_summary";
    public const string RecipesButton = "config_recipes";
    public const string BannerSelect = "config_banner";

    [SlashCommand("config", "Configurá cómo ves el bot: resumen del combate, recetario de la herrería y banner del perfil.")]
    public async Task HandleConfigAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var screen = await BuildScreenAsync(settings, Context.User.Id, blessings);
            await FollowupAsync(embed: screen.Embed, components: screen.Components, ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude abrir la configuración, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    [ComponentInteraction($"{SummaryButton}:*:*")]
    public async Task HandleSummaryButtonAsync(string ownerRaw, string valueRaw) =>
        await ApplyButtonAsync(ownerRaw, owner => TrySetSummaryAsync(settings, owner, valueRaw == "on"));

    [ComponentInteraction($"{RecipesButton}:*:*")]
    public async Task HandleRecipesButtonAsync(string ownerRaw, string valueRaw) =>
        await ApplyButtonAsync(ownerRaw, owner => TrySetRecipeBookAsync(settings, owner, valueRaw == "full"));

    // El menú del banner (v0.50.0): el valor es la clave de la bendición, o «none» para no usar ninguno.
    [ComponentInteraction($"{BannerSelect}:*")]
    public async Task HandleBannerSelectAsync(string ownerRaw, string[] selected)
    {
        string picked = selected.FirstOrDefault() ?? ProfileBanners.NoneValue;
        string? key = picked == ProfileBanners.NoneValue ? null : picked;
        await ApplyAsync(ownerRaw, async owner => await TrySetBannerAsync(settings, blessings, owner, key) switch
        {
            BannerSetStatus.Saved => null,
            BannerSetStatus.NotOwned => Loc.T("Todavía no tenés esa bendición: cada una desbloquea su banner."),
            _ => Loc.T("No pude guardar el cambio, intentá de nuevo en un momento."),
        });
    }

    // Lo que tienen en común los botones: solo el dueño de la pantalla, guardar, y actualizar la pantalla en su lugar.
    private async Task ApplyButtonAsync(string ownerRaw, Func<ulong, Task<bool>> save) =>
        await ApplyAsync(ownerRaw, async owner => await save(owner) ? null : Loc.T("No pude guardar el cambio, intentá de nuevo en un momento."));

    // Igual, pero quien guarda devuelve el aviso a mostrar si no se pudo (null = se guardó).
    private async Task ApplyAsync(string ownerRaw, Func<ulong, Task<string?>> save)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Esa configuración es de otra persona: abrí la tuya con **/config**."), ephemeral: true);
                return;
            }

            string? problem = await save(ownerId);
            var screen = await BuildScreenAsync(settings, ownerId, blessings);

            // La pantalla se actualiza en su lugar; si no se pudo guardar, además se avisa aparte (la pantalla sigue mostrando lo que de verdad hay).
            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = screen.Embed;
                p.Components = screen.Components;
            });

            if (problem is not null)
            {
                await FollowupAsync(problem, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude cambiar la configuración, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // La pantalla: qué tenés ahora y el botón que lo cambia, una opción debajo de la otra. Pública y sin Context para que el comando de texto muestre lo mismo y se pruebe sin Discord.
    public static async Task<ConfigScreen> BuildScreenAsync(IPlayerSettingsService service, ulong discordId, IBlessingRepository? blessings = null)
    {
        var current = await service.GetAsync(discordId);
        bool on = current.ShowFightSummary;
        bool full = current.FullRecipeBook;

        // Los banners que desbloqueó (una bendición = un banner): sin el repositorio, o si la lectura falla, la pantalla sale sin esa opción.
        IReadOnlyDictionary<string, int>? levels = null;
        if (blessings is not null)
        {
            try
            {
                levels = await blessings.GetLevelsAsync(discordId);
            }
            catch (Exception ex)
            {
                BotLog.Warn(ex);
            }
        }

        var embed = new EmbedBuilder()
            .WithTitle(Loc.T("⚙️ Configuración"))
            .WithColor(OnboardingModule.BrandColor)
            .WithDescription(Loc.T("Elegí cómo querés ver el bot. Se guarda en tu cuenta y vale para todos tus comandos. Para cambiar el idioma usá `/language`."))
            .AddField(
                Loc.T("📋 Resumen del combate"),
                on
                    ? Loc.T("✅ **Activado**: al terminar una pelea ves una tablita con los turnos, el daño hecho y el recibido.")
                    : Loc.T("❌ **Desactivado**: las peleas terminan sin la tablita."),
                false)
            .AddField(
                Loc.T("📜 Recetario de la herrería"),
                full
                    ? Loc.T("📜 **Completo**: todas las zonas juntas en un solo mensaje, una línea por receta.")
                    : Loc.T("📖 **Clásico**: las recetas de una zona por vez, con lo que tenés de cada material (✅/❌)."),
                false)
            .Build();

        IReadOnlyList<(BlessingDefinition Blessing, int Level)> unlocked = levels is null ? [] : ProfileBanners.Unlocked(levels);
        var chosen = unlocked.FirstOrDefault(u => u.Blessing.Key == current.ProfileBanner);
        if (levels is not null)
        {
            embed = AddBannerField(embed, unlocked, chosen);
        }

        var buttons = new ComponentBuilder()
            .WithButton(
                on ? Loc.T("Ocultar el resumen") : Loc.T("Mostrar el resumen"),
                $"{SummaryButton}:{discordId}:{(on ? "off" : "on")}",
                on ? ButtonStyle.Secondary : ButtonStyle.Success,
                new Emoji(on ? "🙈" : "📋"))
            .WithButton(
                full ? Loc.T("Recetario clásico") : Loc.T("Recetario completo"),
                $"{RecipesButton}:{discordId}:{(full ? "classic" : "full")}",
                ButtonStyle.Secondary,
                new Emoji(full ? "📖" : "📜"));

        // El menú para elegir el banner (v0.50.0), en su propia fila: «Sin banner» y uno por bendición desbloqueada.
        if (unlocked.Count > 0)
        {
            var menu = new SelectMenuBuilder()
                .WithCustomId($"{BannerSelect}:{discordId}")
                .WithPlaceholder(Loc.T("Elegí el banner de tu perfil"))
                .WithMinValues(1)
                .WithMaxValues(1)
                .AddOption(Loc.T("Sin banner"), ProfileBanners.NoneValue, Loc.T("El perfil sale como siempre."), new Emoji("🚫"), isDefault: chosen.Blessing is null);
            foreach (var (blessing, level) in unlocked)
            {
                menu.AddOption($"{blessing.Name} {BlessingCatalog.RomanLevel(level)}", blessing.Key, Loc.F($"Tier {BlessingCatalog.RomanLevel(level)}: cuanto más sube la bendición, mejor el banner"), new Emoji(blessing.Emoji), isDefault: chosen.Blessing?.Key == blessing.Key);
            }

            buttons.WithSelectMenu(menu, row: 1);
        }

        return new ConfigScreen(embed, buttons.Build());
    }

    // El campo del banner: cuál usa, o cómo se desbloquean.
    private static Embed AddBannerField(Embed embed, IReadOnlyList<(BlessingDefinition Blessing, int Level)> unlocked, (BlessingDefinition Blessing, int Level) chosen)
    {
        string text;
        if (unlocked.Count == 0)
        {
            text = Loc.T("Todavía no tenés ninguna bendición. Cada una desbloquea un banner para tu `/profile`, y cuanto más sube de nivel, mejor se ve (se consiguen con el Fuego Nuevo).");
        }
        else if (chosen.Blessing is null)
        {
            text = Loc.F($"Sin banner. Tenés {unlocked.Count} desbloqueados: elegí uno en el menú de abajo.");
        }
        else
        {
            text = Loc.F($"{chosen.Blessing.Emoji} **{chosen.Blessing.Name}** (tier {BlessingCatalog.RomanLevel(chosen.Level)}): se ve al pie de tu perfil. Cuanto más sube la bendición, mejor el banner.");
        }

        return Discord.EmbedBuilderExtensions.ToEmbedBuilder(embed).AddField(Loc.T("🖼️ Banner del perfil"), text, false).Build();
    }

    // Guarda el banner (la clave de una bendición, o null para ninguno). Solo deja elegir uno que esté desbloqueado. Nunca tira: si la base falla se registra y devuelve Failed.
    public static async Task<BannerSetStatus> TrySetBannerAsync(IPlayerSettingsService service, IBlessingRepository blessings, ulong discordId, string? key)
    {
        try
        {
            if (key is not null)
            {
                var levels = await blessings.GetLevelsAsync(discordId);
                if (BlessingCatalog.Find(key) is null || !levels.TryGetValue(key, out int level) || level <= 0)
                {
                    return BannerSetStatus.NotOwned;
                }
            }

            await service.SetProfileBannerAsync(discordId, key);
            return BannerSetStatus.Saved;
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            return BannerSetStatus.Failed;
        }
    }

    // Guarda la opción. Nunca tira: si la base falla se registra y devuelve false (la pantalla de después muestra lo que de verdad quedó).
    public static async Task<bool> TrySetSummaryAsync(IPlayerSettingsService service, ulong discordId, bool value)
    {
        try
        {
            await service.SetShowFightSummaryAsync(discordId, value);
            return true;
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            return false;
        }
    }

    // Lo mismo para el recetario (v0.28.1): true = el completo, false = el clásico.
    public static async Task<bool> TrySetRecipeBookAsync(IPlayerSettingsService service, ulong discordId, bool full)
    {
        try
        {
            await service.SetFullRecipeBookAsync(discordId, full);
            return true;
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            return false;
        }
    }

    // "aa config" (sin nada) abre la pantalla; "aa config resumen off" (también «summary», «fight summary»; on/off, si/no, mostrar/ocultar, toggle) cambia el resumen y "aa config recetas completo" (también «recipes full»,
    // «clásico»/«classic», toggle) el recetario; en los dos casos muestra la pantalla ya al día.
    // note: una línea de aviso arriba de la pantalla (lo que se hizo, o qué no se entendió); null si solo se abrió.
    public static async Task<(string? Note, ConfigScreen Screen)> ExecuteTypedAsync(IPlayerSettingsService service, ulong discordId, string? option, string? value, IBlessingRepository? blessings = null)
    {
        if (string.IsNullOrWhiteSpace(option))
        {
            return (null, await BuildScreenAsync(service, discordId, blessings));
        }

        return SettingWords.KeyOf(option) switch
        {
            SettingKey.FightSummary => await ExecuteSummaryTypedAsync(service, discordId, value),
            SettingKey.RecipeBook => await ExecuteRecipeBookTypedAsync(service, discordId, value),
            SettingKey.Banner => await ExecuteBannerTypedAsync(service, blessings, discordId, value),
            _ => (Loc.T("No conozco esa opción. Probá `aa config resumen off`, `aa config recetas completo` o `aa banner`."), await BuildScreenAsync(service, discordId, blessings)),
        };
    }

    // «aa banner» (o «aa config banner») sin nombre abre el panel; con el nombre de una bendición suya la pone de banner; «ninguno» lo saca.
    private static async Task<(string? Note, ConfigScreen Screen)> ExecuteBannerTypedAsync(IPlayerSettingsService service, IBlessingRepository? blessings, ulong discordId, string? value)
    {
        if (blessings is null || string.IsNullOrWhiteSpace(value))
        {
            return (null, await BuildScreenAsync(service, discordId, blessings));
        }

        string? key = null;
        BlessingDefinition? wanted = null;
        if (!ProfileBanners.IsNoneWord(value))
        {
            wanted = ProfileBanners.Find(value);
            if (wanted is null)
            {
                return (Loc.T("No conozco ese banner. Escribí el nombre de una de tus bendiciones (`aa banner mano de leñador`) o `aa banner ninguno`."), await BuildScreenAsync(service, discordId, blessings));
            }

            key = wanted.Key;
        }

        var status = await TrySetBannerAsync(service, blessings, discordId, key);
        var screen = await BuildScreenAsync(service, discordId, blessings);
        return status switch
        {
            BannerSetStatus.NotOwned => (Loc.F($"Todavía no tenés la bendición **{wanted?.Name}**: cada una desbloquea su banner."), screen),
            BannerSetStatus.Failed => (Loc.T("No pude guardar el cambio, intentá de nuevo en un momento."), screen),
            _ when wanted is null => (Loc.T("✅ Listo: tu perfil va **sin banner**."), screen),
            _ => (Loc.F($"✅ Listo: tu banner es **{wanted.Name}**."), screen),
        };
    }

    private static async Task<(string? Note, ConfigScreen Screen)> ExecuteSummaryTypedAsync(IPlayerSettingsService service, ulong discordId, string? value)
    {
        var (wanted, toggle) = SettingWords.ParseSwitch(value);
        if (wanted is null && !toggle)
        {
            return (Loc.T("Decime si lo querés **on** u **off**: `aa config resumen off`."), await BuildScreenAsync(service, discordId));
        }

        bool target = wanted ?? !(await service.GetAsync(discordId)).ShowFightSummary;
        bool saved = await TrySetSummaryAsync(service, discordId, target);
        var screen = await BuildScreenAsync(service, discordId);

        if (!saved)
        {
            return (Loc.T("No pude guardar el cambio, intentá de nuevo en un momento."), screen);
        }

        return (target ? Loc.T("✅ Listo: el resumen del combate está **activado**.") : Loc.T("✅ Listo: el resumen del combate está **desactivado**."), screen);
    }

    private static async Task<(string? Note, ConfigScreen Screen)> ExecuteRecipeBookTypedAsync(IPlayerSettingsService service, ulong discordId, string? value)
    {
        var (wanted, toggle) = SettingWords.ParseRecipeBook(value);
        if (wanted is null && !toggle)
        {
            return (Loc.T("Decime cuál querés, **completo** o **clásico**: `aa config recetas completo`."), await BuildScreenAsync(service, discordId));
        }

        bool target = wanted ?? !(await service.GetAsync(discordId)).FullRecipeBook;
        bool saved = await TrySetRecipeBookAsync(service, discordId, target);
        var screen = await BuildScreenAsync(service, discordId);

        if (!saved)
        {
            return (Loc.T("No pude guardar el cambio, intentá de nuevo en un momento."), screen);
        }

        return (target ? Loc.T("✅ Listo: el recetario de la herrería es el **completo**.") : Loc.T("✅ Listo: el recetario de la herrería es el **clásico**."), screen);
    }
}
