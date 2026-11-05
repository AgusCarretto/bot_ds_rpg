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
// Banner del perfil (v0.50.0): hay UN banner en todo el juego, el de la bendición Brasa de Color, que mejora con su nivel (GameData/ProfileBanners.cs). Quien la tiene elige en un menú: sin banner, el mejor (el de su nivel,
// mejora solo) o uno de los tiers que ya desbloqueó; también «aa banner on», «aa banner 3» y «aa banner off».
public sealed record ConfigScreen(Embed Embed, MessageComponent Components);

public enum BannerSetStatus { Saved, NotOwned, TierTooHigh, Failed }

public class ConfigModule(IPlayerSettingsService settings, IBlessingRepository blessings) : InteractionModuleBase<SocketInteractionContext>
{
    // El id del botón lleva a quién pertenece la pantalla y el valor al que PASA al tocarlo (on / off; full / classic): nadie puede cambiar la configuración de otro con una pantalla vieja o ajena.
    public const string SummaryButton = "config_summary";
    public const string RecipesButton = "config_recipes";
    public const string BannerSelect = "config_banner";
    public const string CdShowSelect = "config_cdshow";
    public const string CdFirstSelect = "config_cdfirst";

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

    // El menú del banner (v0.50.0): el valor es «none» (sin banner), «auto» (el mejor: el de tu nivel, mejora solo) o el número de tier (1 a 5) que elegiste.
    [ComponentInteraction($"{BannerSelect}:*")]
    public async Task HandleBannerSelectAsync(string ownerRaw, string[] selected)
    {
        string? stored = ProfileBanners.StoredFor(selected.FirstOrDefault());
        await ApplyAsync(ownerRaw, async owner => await TrySetBannerAsync(settings, blessings, owner, stored) switch
        {
            BannerSetStatus.Saved => null,
            BannerSetStatus.NotOwned => Loc.T("Todavía no tenés la bendición Brasa de Color: es la que desbloquea el banner."),
            BannerSetStatus.TierTooHigh => Loc.T("Todavía no llegaste a ese tier: se desbloquea al subir de nivel la Brasa de Color."),
            _ => Loc.T("No pude guardar el cambio, intentá de nuevo en un momento."),
        });
    }

    // El menú de qué líneas de /cd ver (v0.51.0): lo tildado se ve, lo demás se oculta. No cambia el orden.
    [ComponentInteraction($"{CdShowSelect}:*")]
    public async Task HandleCdShowAsync(string ownerRaw, string[] selected) =>
        await ApplyButtonAsync(ownerRaw, owner => TrySetCdLayoutAsync(settings, owner, current => CooldownLayout.SetVisible(current, selected)));

    // El menú de «poner primera» (v0.51.0): la línea elegida pasa al puesto 1; «reset» deja /cd como siempre (todas las líneas, en el orden de siempre).
    [ComponentInteraction($"{CdFirstSelect}:*")]
    public async Task HandleCdFirstAsync(string ownerRaw, string[] selected)
    {
        string picked = selected.FirstOrDefault() ?? string.Empty;
        await ApplyButtonAsync(ownerRaw, owner => TrySetCdLayoutAsync(settings, owner, current => picked == CooldownLayout.ResetChoice ? null : CooldownLayout.MoveTo(current, picked, 1)));
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
        int? bannerLevel = unlocked.Count == 0 ? null : unlocked[0].Level;
        var (bannerOn, bannerTier) = ProfileBanners.Parse(current.ProfileBanner);
        if (levels is not null)
        {
            embed = AddBannerField(embed, bannerLevel, bannerOn, bannerTier);
        }

        embed = AddCdField(embed, current.CdLayout);

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

        // El menú para elegir el banner (v0.50.0), en su propia fila y solo para quien tiene la Brasa de Color: «Sin banner», «El mejor» (el de su nivel, mejora solo) y un tier por cada nivel que ya tiene la bendición.
        if (bannerLevel is int unlockedTiers)
        {
            var menu = new SelectMenuBuilder()
                .WithCustomId($"{BannerSelect}:{discordId}")
                .WithPlaceholder(Loc.T("Elegí el banner de tu perfil"))
                .WithMinValues(1)
                .WithMaxValues(1)
                .AddOption(Loc.T("Sin banner"), ProfileBanners.NoneChoice, Loc.T("El perfil sale como siempre."), new Emoji("🚫"), isDefault: !bannerOn)
                .AddOption(Loc.T("El mejor (automático)"), ProfileBanners.AutoChoice, Loc.T("Siempre el de tu nivel: mejora solo al subir la bendición."), new Emoji("✨"), isDefault: bannerOn && bannerTier is null);
            for (int tier = 1; tier <= unlockedTiers; tier++)
            {
                menu.AddOption(Loc.F($"Tier {BlessingCatalog.RomanLevel(tier)}"), $"{tier}", Loc.F($"El banner de la Brasa de Color en su tier {BlessingCatalog.RomanLevel(tier)}."), new Emoji("🌈"), isDefault: bannerOn && bannerTier == tier);
            }

            buttons.WithSelectMenu(menu, row: 1);
        }

        // Los cooldowns de /cd (v0.51.0): un menú con TODAS las líneas (lo tildado se ve) y otro para poner una primera. Para un puesto exacto, `aa cd orden cazar 3`.
        var cdMaterial = CooldownLayout.Materialize(current.CdLayout);
        var showMenu = new SelectMenuBuilder()
            .WithCustomId($"{CdShowSelect}:{discordId}")
            .WithPlaceholder(Loc.T("Qué líneas ver en /cd"))
            .WithMinValues(0)
            .WithMaxValues(CooldownLayout.Known.Count);
        foreach (var (key, hidden) in cdMaterial)
        {
            var line = CooldownLayout.LineOf(key)!;
            showMenu.AddOption(Loc.Tr(line.Name), key, null, new Emoji(line.Emoji), isDefault: !hidden);
        }

        buttons.WithSelectMenu(showMenu, row: buttons.ActionRows.Count);

        var firstMenu = new SelectMenuBuilder()
            .WithCustomId($"{CdFirstSelect}:{discordId}")
            .WithPlaceholder(Loc.T("Poner una línea primera en /cd"))
            .WithMinValues(1)
            .WithMaxValues(1);
        int place = 0;
        foreach (var (key, hidden) in cdMaterial)
        {
            if (hidden)
            {
                continue;
            }

            place++;
            var line = CooldownLayout.LineOf(key)!;
            firstMenu.AddOption(Loc.Tr(line.Name), key, Loc.F($"Ahora en el puesto {place}"), new Emoji(line.Emoji));
        }

        firstMenu.AddOption(Loc.T("Volver al orden de siempre"), CooldownLayout.ResetChoice, Loc.T("Se ven todas las líneas, como antes."), new Emoji("↩"));
        buttons.WithSelectMenu(firstMenu, row: buttons.ActionRows.Count);

        return new ConfigScreen(embed, buttons.Build());
    }

    // El campo del banner: cómo se desbloquea, o cuál tenés puesto (level = el nivel de la bendición Brasa de Color; null = no la tiene).
    private static Embed AddBannerField(Embed embed, int? level, bool on, int? tier)
    {
        string text;
        if (level is null)
        {
            text = Loc.T("Se desbloquea con la bendición **Brasa de Color** (se consigue con el Fuego Nuevo). Es el banner de tu `/profile`: mejora con cada nivel de la bendición y vos elegís cuál tier mostrar.");
        }
        else if (!on)
        {
            text = Loc.F($"❌ **Sin banner.** Tu Brasa de Color está en el tier {BlessingCatalog.RomanLevel(level.Value)}: elegí uno en el menú de abajo.");
        }
        else if (tier is null)
        {
            text = Loc.F($"✅ **El mejor (automático)**: tier {BlessingCatalog.RomanLevel(level.Value)} ahora, y mejora solo cada vez que sube la bendición.");
        }
        else
        {
            text = Loc.F($"✅ **Tier {BlessingCatalog.RomanLevel(Math.Min(tier.Value, level.Value))}**, al pie de tu perfil (tenés desbloqueados hasta el {BlessingCatalog.RomanLevel(level.Value)}).");
        }

        return Discord.EmbedBuilderExtensions.ToEmbedBuilder(embed).AddField(Loc.T("🖼️ Banner del perfil"), text, false).Build();
    }

    // Guarda el banner (null = sin banner; «brasa_de_color» = el mejor; «brasa_de_color:3» = ese tier). Solo lo deja poner a quien tiene la bendición Brasa de Color y solo hasta el tier que ya desbloqueó.
    // Nunca tira: si la base falla se registra y devuelve Failed.
    public static async Task<BannerSetStatus> TrySetBannerAsync(IPlayerSettingsService service, IBlessingRepository blessings, ulong discordId, string? stored)
    {
        try
        {
            if (stored is not null)
            {
                var (valid, tier) = ProfileBanners.Parse(stored);
                var levels = await blessings.GetLevelsAsync(discordId);
                if (!valid || !levels.TryGetValue(ProfileBanners.BannerKey, out int level) || level <= 0)
                {
                    return BannerSetStatus.NotOwned;
                }

                if (tier is int wanted && wanted > Math.Min(level, BlessingCatalog.MaxLevel))
                {
                    return BannerSetStatus.TierTooHigh;
                }
            }

            await service.SetProfileBannerAsync(discordId, stored);
            return BannerSetStatus.Saved;
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            return BannerSetStatus.Failed;
        }
    }

    // El campo de los cooldowns: cuántas líneas ve y si es el orden de siempre.
    private static Embed AddCdField(Embed embed, string? layout)
    {
        int total = CooldownLayout.Known.Count;
        int visible = CooldownLayout.VisibleKeys(layout).Count;
        string text = CooldownLayout.IsDefault(layout)
            ? Loc.T("Ves **todas** las líneas, en el orden de siempre. Destildá las que no quieras ver en el primer menú y elegí cuál va primera en el segundo. Para otro puesto: `aa cd orden cazar 3`.")
            : Loc.F($"Ves **{visible} de {total}** líneas, en **tu orden**. Podés cambiarlo en los menús de abajo, con `aa cd orden cazar 3` o volver a lo de siempre con la última opción del segundo menú.");
        return Discord.EmbedBuilderExtensions.ToEmbedBuilder(embed).AddField(Loc.T("⏱️ Cooldowns (/cd)"), text, false).Build();
    }

    // Guarda las líneas de /cd (v0.51.0): `change` recibe lo guardado hoy y devuelve lo nuevo. Nunca tira: si la base falla se registra y devuelve false (la pantalla de después muestra lo que de verdad quedó).
    public static async Task<bool> TrySetCdLayoutAsync(IPlayerSettingsService service, ulong discordId, Func<string?, string?> change)
    {
        try
        {
            string? current = (await service.GetAsync(discordId)).CdLayout;
            await service.SetCdLayoutAsync(discordId, change(current));
            return true;
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            return false;
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
            _ => (Loc.T("No conozco esa opción. Probá `aa config resumen off`, `aa config recetas completo` o `aa banner on`."), await BuildScreenAsync(service, discordId, blessings)),
        };
    }

    // «aa banner» (o «aa config banner») sin nada abre el panel; «on» o «auto» pone el mejor (el de tu nivel, mejora solo), «3» (también «tier 3», «iii») fija ese tier, «off» (también «ninguno», «sacar», «no») lo
    // saca y «toggle» lo invierte.
    private static async Task<(string? Note, ConfigScreen Screen)> ExecuteBannerTypedAsync(IPlayerSettingsService service, IBlessingRepository? blessings, ulong discordId, string? value)
    {
        if (blessings is null || string.IsNullOrWhiteSpace(value))
        {
            return (null, await BuildScreenAsync(service, discordId, blessings));
        }

        int? tier = ProfileBanners.IsNoneWord(value) ? null : ProfileBanners.TierFrom(value);
        string? stored;
        if (ProfileBanners.IsNoneWord(value))
        {
            stored = null;
        }
        else if (tier is int fixedTier)
        {
            stored = ProfileBanners.StoredFor(fixedTier);
        }
        else if (ProfileBanners.IsOnWord(value))
        {
            stored = ProfileBanners.BannerKey;
        }
        else if (SettingWords.ParseSwitch(value).Toggle)
        {
            stored = ProfileBanners.Parse((await service.GetAsync(discordId)).ProfileBanner).On ? null : ProfileBanners.BannerKey;
        }
        else
        {
            return (Loc.T("Decime qué querés: `aa banner on` (el mejor), `aa banner 3` (ese tier) o `aa banner off`."), await BuildScreenAsync(service, discordId, blessings));
        }

        var status = await TrySetBannerAsync(service, blessings, discordId, stored);
        var screen = await BuildScreenAsync(service, discordId, blessings);
        return status switch
        {
            BannerSetStatus.NotOwned => (Loc.T("Todavía no tenés la bendición Brasa de Color: es la que desbloquea el banner."), screen),
            BannerSetStatus.TierTooHigh => (Loc.T("Todavía no llegaste a ese tier: se desbloquea al subir de nivel la Brasa de Color."), screen),
            BannerSetStatus.Failed => (Loc.T("No pude guardar el cambio, intentá de nuevo en un momento."), screen),
            _ when stored is null => (Loc.T("✅ Listo: tu perfil va **sin banner**."), screen),
            _ when tier is int shown => (Loc.F($"✅ Listo: tu perfil lleva el banner de la Brasa de Color, **tier {BlessingCatalog.RomanLevel(shown)}**."), screen),
            _ => (Loc.T("✅ Listo: tu perfil lleva el **mejor banner de la Brasa de Color** (el de tu nivel, mejora solo)."), screen),
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
