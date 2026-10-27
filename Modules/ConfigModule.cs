using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /config (aa config, alias conf / set / settings / ajustes): cómo querés VER el bot (v0.26.1). Dos opciones: mostrar u ocultar la tablita del resumen del combate (turnos, daño hecho y recibido) que sale al
// terminar una pelea de /hunt, /travel, /boss y /autohunt, y (v0.28.1) qué recetario ves en la herrería: el clásico (una zona por página, con lo que tenés de cada material) o el completo de la v0.27.0 (todas las zonas
// juntas). Nada de esto toca el juego, solo lo que se muestra. Se arma como una pantalla con un botón por opción (un click la cambia y la pantalla se actualiza en su lugar) y también se puede escribir:
// «aa config resumen off», «aa config recetas completo». Las opciones viven en GameData/PlayerSettings.cs y se guardan en player_settings (Services/PlayerSettingsService.cs); el idioma tiene su propio comando
// (/language). La lógica es estática y sin Context: el slash, el botón y el comando de texto llaman a lo mismo.
public sealed record ConfigScreen(Embed Embed, MessageComponent Components);

public class ConfigModule(IPlayerSettingsService settings) : InteractionModuleBase<SocketInteractionContext>
{
    // El id del botón lleva a quién pertenece la pantalla y el valor al que PASA al tocarlo (on / off; full / classic): nadie puede cambiar la configuración de otro con una pantalla vieja o ajena.
    public const string SummaryButton = "config_summary";
    public const string RecipesButton = "config_recipes";

    [SlashCommand("config", "Configurá cómo ves el bot: el resumen del combate y el recetario de la herrería.")]
    public async Task HandleConfigAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var screen = await BuildScreenAsync(settings, Context.User.Id);
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

    // Lo que tienen en común los botones: solo el dueño de la pantalla, guardar, y actualizar la pantalla en su lugar.
    private async Task ApplyButtonAsync(string ownerRaw, Func<ulong, Task<bool>> save)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Esa configuración es de otra persona: abrí la tuya con **/config**."), ephemeral: true);
                return;
            }

            bool saved = await save(ownerId);
            var screen = await BuildScreenAsync(settings, ownerId);

            // La pantalla se actualiza en su lugar; si no se pudo guardar, además se avisa aparte (la pantalla sigue mostrando lo que de verdad hay).
            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = screen.Embed;
                p.Components = screen.Components;
            });

            if (!saved)
            {
                await FollowupAsync(Loc.T("No pude guardar el cambio, intentá de nuevo en un momento."), ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude cambiar la configuración, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // La pantalla: qué tenés ahora y el botón que lo cambia, una opción debajo de la otra. Pública y sin Context para que el comando de texto muestre lo mismo y se pruebe sin Discord.
    public static async Task<ConfigScreen> BuildScreenAsync(IPlayerSettingsService service, ulong discordId)
    {
        var current = await service.GetAsync(discordId);
        bool on = current.ShowFightSummary;
        bool full = current.FullRecipeBook;

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
                new Emoji(full ? "📖" : "📜"))
            .Build();

        return new ConfigScreen(embed, buttons);
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
    public static async Task<(string? Note, ConfigScreen Screen)> ExecuteTypedAsync(IPlayerSettingsService service, ulong discordId, string? option, string? value)
    {
        if (string.IsNullOrWhiteSpace(option))
        {
            return (null, await BuildScreenAsync(service, discordId));
        }

        return SettingWords.KeyOf(option) switch
        {
            SettingKey.FightSummary => await ExecuteSummaryTypedAsync(service, discordId, value),
            SettingKey.RecipeBook => await ExecuteRecipeBookTypedAsync(service, discordId, value),
            _ => (Loc.T("No conozco esa opción. Probá `aa config resumen off` o `aa config recetas completo`."), await BuildScreenAsync(service, discordId)),
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
