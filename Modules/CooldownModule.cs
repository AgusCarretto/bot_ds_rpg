using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using BotDsRpg.Services;

public class CooldownModule(
    ICooldownRepository cooldownRepository, IUserRepository userRepository, IArenaService arenaService, IPlayerBonusService bonusService, ITopGgVoteClient voteClient, IPetRepository petRepository, IDragonRepository dragonRepository, IPetExpeditionRepository expeditionRepository,
    IWorldBossService worldBossService, IPlayerSettingsService settings)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("cd", "Mirá tus cooldowns: cazar, viajar, talar, minar, jefe, cajas, mascotas, diario y la Arena de hoy.")]
    public async Task HandleCooldownsAsync(
        [Summary("jugador", "Opcional: de qué jugador del server querés ver los cooldowns.")] Discord.WebSocket.SocketGuildUser? targetUser = null)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            string? layout = (await settings.GetAsync(Context.User.Id)).CdLayout;

            // Los de OTRO jugador (v0.51.3): con las líneas que cada uno eligió ver en /config, las del que mira.
            if (targetUser is not null && targetUser.Id != Context.User.Id)
            {
                var other = await BuildOtherStatusAsync(cooldownRepository, userRepository, targetUser, arenaService, bonusService, voteClient.Enabled, petRepository, dragonRepository, expeditionRepository, worldBossService, layout);
                await FollowupAsync(other.Message, embed: other.Embed, ephemeral: true);
                return;
            }

            var embed = await BuildStatusEmbedAsync(cooldownRepository, userRepository, Context.User.Id, arenaService, bonusService, voteClient.Enabled, petRepository, dragonRepository, expeditionRepository, worldBossService, layout);
            await FollowupAsync(embed: embed, ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync(Loc.T("No pude consultar tus cooldowns ahora mismo, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // Estático (sin dependencia de Context) para que Modules/TextCommandModule.cs use exactamente
    // la misma lógica en "aa cd" — acá vive tanto el cálculo como el embed, no hay nada más que compartir.
    // arenaService: con él, la última línea dice si ya estás anotado en la Arena de hoy y cuánto falta para que se juegue (para que nadie se olvide de
    // anotarse). Sin él (o si falla) la línea simplemente no sale: /cd nunca se rompe por la Arena.
    // bonusService (v0.13.0): con él, la tala y la minería avanzadas (oficios al nivel 100) aparecen en la lista solo para quien ya las desbloqueó.
    // voteEnabled (v0.18.0): con el voto habilitado (hay token de top.gg), la lista suma la línea de "Votar"; mientras no se pueda votar no se muestra.
    // petRepository (v0.18.4, sugerencia de Adiofh en #sugerencias): con él, la línea de "Mascotas" dice si ya se puede alimentar o cuánto falta (con la espera propia del jugador, la del Buen Pienso);
    // sin mascotas que puedan subir de nivel la línea no sale, y si falla leerlas tampoco: /cd nunca se rompe por esto.
    // targetName (v0.51.3): el nombre de OTRO jugador cuyos cooldowns se miran («aa cd @alguien», «/cd jugador:»). Con él el título lo nombra, el jugador NO se crea si no existe (quien llama ya lo comprobó) y las líneas
    // que le hablan al dueño («todavía no te anotaste») pasan a hablar de él. Sin él es /cd de siempre.
    public static async Task<Embed> BuildStatusEmbedAsync(
        ICooldownRepository cooldownRepository, IUserRepository userRepository, ulong discordId, IArenaService? arenaService = null, IPlayerBonusService? bonusService = null, bool voteEnabled = false,
        IPetRepository? petRepository = null, IDragonRepository? dragonRepository = null, IPetExpeditionRepository? expeditionRepository = null, IWorldBossService? worldBossService = null, string? layout = null,
        string? targetName = null)
    {
        // Cada línea lleva su clave (CooldownLayout.Known) para que el jugador pueda ocultarla o cambiarla de lugar (v0.51.0); sin nada guardado salen todas, en el orden de siempre.
        var lines = new List<(string Key, string Text)>();
        bool other = targetName is not null;
        var player = other
            ? await userRepository.GetByDiscordIdAsync(discordId) ?? throw new InvalidOperationException("El jugador no tiene personaje.")
            : await userRepository.GetOrCreateUserAsync(discordId);

        async Task AddLineAsync(CooldownDefinition definition)
        {
            var remaining = await cooldownRepository.GetRemainingAsync(discordId, definition.CommandName, definition.Duration);
            string status = remaining is null
                ? Loc.T("**¡Listo!** ✅")
                : Loc.F($"{TimeFormat.Remaining(remaining.Value)} restantes");

            lines.Add((definition.CommandName, $"{definition.Emoji} **{Loc.Tr(definition.DisplayName)}**: {status}"));
        }

        // El jefe va abajo de todo, con el jefe mundial y la Arena (el dueño, v0.46.0): los tres son las peleas «grandes» del día.
        foreach (var definition in CooldownCatalog.All.Where(d => d != CooldownCatalog.Boss))
        {
            await AddLineAsync(definition);
        }

        // Las versiones avanzadas de /chop y /mine: solo con el oficio al nivel máximo (GameData/ProfessionRules.cs).
        PlayerBonuses? bonuses = null;
        if (bonusService is not null)
        {
            bonuses = await bonusService.GetAsync(discordId, player.FuegoNuevo);
            if (ProfessionRules.AdvancedUnlocked(bonuses.ProfessionLevel(ProfessionCatalog.WoodcutterKey)))
            {
                await AddLineAsync(CooldownCatalog.ChopAdvanced);
            }

            if (ProfessionRules.AdvancedUnlocked(bonuses.ProfessionLevel(ProfessionCatalog.MinerKey)))
            {
                await AddLineAsync(CooldownCatalog.MineAdvanced);
            }

            if (ProfessionRules.AdvancedUnlocked(bonuses.ProfessionLevel(ProfessionCatalog.FisherKey)))
            {
                await AddLineAsync(CooldownCatalog.FishAdvanced);
            }
        }

        if (voteEnabled)
        {
            await AddLineAsync(CooldownCatalog.Vote);
        }

        // La comida de las mascotas: una hora por mascota (menos con la bendición Buen Pienso), y no vive en la tabla cooldowns sino en player_pets.
        if (await TryBuildPetLineAsync(petRepository, discordId, bonuses?.PetFeedCooldown) is { } petLine)
        {
            lines.Add((CooldownLayout.PetsKey, petLine));
        }

        // El dragón (v0.30.0): cuándo puede volver a criar (el descanso de 12 h vive en player_dragons, no en la tabla cooldowns). Sin dragón, o en el tier máximo (no hay nada que criar), no sale.
        if (await TryBuildDragonLineAsync(dragonRepository, discordId) is { } dragonLine)
        {
            lines.Add((CooldownLayout.DragonKey, dragonLine));
        }

        // La expedición de la mascota (v0.39.0): vive en pet_expeditions. Sin expedición en curso la línea no sale; si falla leerla, tampoco.
        if (await TryBuildExpeditionLineAsync(expeditionRepository, discordId) is { } expeditionLine)
        {
            lines.Add((CooldownLayout.ExpeditionKey, expeditionLine));
        }

        // /daily no usa la tabla cooldowns (tiene su propia columna last_daily_claim con
        // ventana de 24h/48h), así que lo evaluamos aparte con la misma lógica pura de /daily.
        var dailyCalculation = DailyRewardCalculator.Evaluate(player.LastDailyClaim, player.DailyStreak, DateTime.UtcNow);
        string dailyStatus = dailyCalculation.Status == DailyClaimStatus.TooSoon
            ? Loc.F($"{TimeFormat.Remaining(dailyCalculation.RemainingCooldown!.Value)} restantes")
            : Loc.T("**¡Listo!** ✅");
        lines.Add((CooldownLayout.DailyKey, $"🎁 **{Loc.T("Diario")}**: {dailyStatus}"));

        // Abajo de todo: el jefe, el jefe mundial (v0.46.0: su propio límite, uno por día, no gasta el cooldown del jefe) y la Arena.
        await AddLineAsync(CooldownCatalog.Boss);
        if (await TryBuildWorldBossLineAsync(worldBossService, discordId, other) is { } worldBossLine)
        {
            lines.Add((CooldownLayout.WorldBossKey, worldBossLine));
        }

        if (await TryBuildArenaLineAsync(arenaService, discordId, other) is { } arenaLine)
        {
            lines.Add((CooldownLayout.ArenaKey, arenaLine));
        }

        // Lo que el jugador eligió ver y en qué orden (v0.51.0, /config y «aa cd orden»). Si ocultó todo, la pantalla lo dice en vez de quedar vacía.
        var shown = CooldownLayout.Apply(lines, layout);
        return new EmbedBuilder()
            .WithTitle(other ? Loc.F($"⏱️ Cooldowns de {targetName}") : Loc.T("⏱️ Tus cooldowns"))
            .WithColor(Color.Orange) // naranja como perfil, stats e inventario: las pantallas del personaje van del mismo color (auditoría de UI, M15)
            .WithDescription(shown.Count == 0 ? Loc.T("Tenés todas las líneas ocultas. Mostralas en **/config** o con `aa cd mostrar todo`.") : string.Join('\n', shown))
            .WithFooter(other ? Loc.T("Con las líneas que elegiste ver en /config.") : Loc.T("Elegí qué líneas ver y en qué orden en /config (o con `aa cd orden`)."))
            .Build();
    }

    // Los cooldowns de OTRO jugador (v0.51.3, el dueño: «poder ver el cd de otra persona»): nunca le crea una cuenta (si no tiene personaje lo dice) y los bots no tienen. Con el mensaje o con la pantalla, uno de los dos.
    public static async Task<(string? Message, Embed? Embed)> BuildOtherStatusAsync(
        ICooldownRepository cooldownRepository, IUserRepository userRepository, IUser target, IArenaService? arenaService = null, IPlayerBonusService? bonusService = null, bool voteEnabled = false,
        IPetRepository? petRepository = null, IDragonRepository? dragonRepository = null, IPetExpeditionRepository? expeditionRepository = null, IWorldBossService? worldBossService = null, string? layout = null)
    {
        string name = GameModule.GetDisplayName(target);
        if (target.IsBot)
        {
            return (Loc.F($"**{name}** es un bot: no tiene cooldowns."), null);
        }

        if (await userRepository.GetByDiscordIdAsync(target.Id) is null)
        {
            return (Loc.F($"**{name}** todavía no tiene personaje."), null);
        }

        var embed = await BuildStatusEmbedAsync(cooldownRepository, userRepository, target.Id, arenaService, bonusService, voteEnabled, petRepository, dragonRepository, expeditionRepository, worldBossService, layout, name);
        return (null, embed);
    }

    // Las palabras que «aa cd» entiende para elegir qué líneas ver (ExecuteLayoutAsync): si lo que se escribió después de «aa cd» no es una de estas, se lee como un jugador («aa cd @alguien»).
    public static bool IsLayoutWord(string? word) =>
        (word ?? string.Empty).Trim().ToLowerInvariant() is
            "orden" or "order" or "mover" or "move" or "posicion" or "posición" or "position"
            or "ocultar" or "hide" or "sacar" or "esconder"
            or "mostrar" or "show" or "ver" or "poner"
            or "reset" or "restablecer" or "reiniciar" or "default" or "normal";

    // Lo que se escribe después de «aa cd» (v0.51.0): «orden <línea> <puesto>» (1 = primera; cuenta solo las que ves), «ocultar <línea>», «mostrar <línea|todo>» y «reset». Devuelve el aviso de lo que se hizo (o qué no
    // se entendió) y null si no se escribió nada (entonces solo se muestra /cd). Nunca tira: si la base falla, lo dice.
    public static async Task<string?> ExecuteLayoutAsync(IPlayerSettingsService settings, ulong discordId, string? action, string? rest)
    {
        string word = (action ?? string.Empty).Trim().ToLowerInvariant();
        if (word.Length == 0)
        {
            return null;
        }

        string argument = (rest ?? string.Empty).Trim();
        string? current = (await settings.GetAsync(discordId)).CdLayout;
        string? next;
        string note;

        switch (word)
        {
            case "orden" or "order" or "mover" or "move" or "posicion" or "posición" or "position":
            {
                // La última palabra es el puesto y lo que va antes, la línea («jefe mundial 2»).
                int cut = argument.LastIndexOf(' ');
                string lineText = cut < 0 ? argument : argument[..cut];
                string numberText = cut < 0 ? string.Empty : argument[(cut + 1)..];
                var line = CooldownLayout.Find(lineText);
                if (line is null || !int.TryParse(numberText, out int position) || position < 1)
                {
                    return Loc.T("Decime qué línea y en qué puesto: `aa cd orden cazar 1` (1 = primera).");
                }

                next = CooldownLayout.MoveTo(current, line.Key, position);
                var visible = CooldownLayout.VisibleKeys(next).ToList();
                int placed = visible.IndexOf(line.Key) + 1;
                note = placed > 0
                    ? Loc.F($"✅ Listo: **{Loc.Tr(line.Name)}** pasó al puesto **{placed}**.")
                    : Loc.F($"✅ Listo: **{Loc.Tr(line.Name)}** quedó en ese lugar, pero está oculta: mostrala con `aa cd mostrar {line.Key}`.");
                break;
            }

            case "ocultar" or "hide" or "sacar" or "esconder":
            {
                var line = CooldownLayout.Find(argument);
                if (line is null)
                {
                    return UnknownLineNote();
                }

                next = CooldownLayout.SetLineVisible(current, line.Key, false);
                note = Loc.F($"✅ Listo: **{Loc.Tr(line.Name)}** ya no se ve en /cd.");
                break;
            }

            case "mostrar" or "show" or "ver" or "poner":
            {
                if (argument.ToLowerInvariant() is "todo" or "todas" or "todos" or "all")
                {
                    next = CooldownLayout.ShowAll(current);
                    note = Loc.T("✅ Listo: ahora se ven todas las líneas.");
                    break;
                }

                var line = CooldownLayout.Find(argument);
                if (line is null)
                {
                    return UnknownLineNote();
                }

                next = CooldownLayout.SetLineVisible(current, line.Key, true);
                note = Loc.F($"✅ Listo: **{Loc.Tr(line.Name)}** vuelve a verse en /cd.");
                break;
            }

            case "reset" or "restablecer" or "reiniciar" or "default" or "normal":
                next = null;
                note = Loc.T("✅ Listo: /cd vuelve a mostrar todas las líneas, en el orden de siempre.");
                break;

            default:
                return Loc.T("No entendí. Probá `aa cd orden cazar 1`, `aa cd ocultar viajar`, `aa cd mostrar todo` o `aa cd reset`.");
        }

        try
        {
            await settings.SetCdLayoutAsync(discordId, next);
            return note;
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            return Loc.T("No pude guardar el cambio, intentá de nuevo en un momento.");
        }
    }

    private static string UnknownLineNote() =>
        Loc.T("No conozco esa línea. Probá con: cazar, viajar, talar, minar, pescar, cajas, mascotas, dragón, expedición, diario, jefe, jefe mundial o arena.");

    private static async Task<string?> TryBuildExpeditionLineAsync(IPetExpeditionRepository? expeditionRepository, ulong discordId)
    {
        if (expeditionRepository is null)
        {
            return null;
        }

        try
        {
            return ExpeditionLine(await expeditionRepository.GetAsync(discordId));
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
            return null;
        }
    }

    // La línea de la expedición (pura, para probarla sin base): «¡Listo!» si ya volvió o lo que falta. Null si no hay ninguna en curso.
    public static string? ExpeditionLine(ActiveExpedition? expedition)
    {
        if (expedition is null)
        {
            return null;
        }

        string status = expedition.IsReady ? Loc.T("**¡Listo!** ✅") + " — **/pet expedition**" : Loc.F($"{TimeFormat.Remaining(expedition.Remaining)} restantes");
        return $"🧭 **{Loc.T("Expedición")}** ({expedition.Species.Name}): {status}";
    }

    private static async Task<string?> TryBuildDragonLineAsync(IDragonRepository? dragonRepository, ulong discordId)
    {
        if (dragonRepository is null)
        {
            return null;
        }

        try
        {
            return DragonLine(await dragonRepository.GetAsync(discordId));
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
            return null;
        }
    }

    // La línea del dragón (pura, para probarla sin base): «¡Listo!» si ya puede criar o lo que le falta. Null sin dragón o en el tier máximo.
    public static string? DragonLine(PlayerDragon? dragon)
    {
        if (dragon is null || dragon.Tier >= DragonRules.MaxTier)
        {
            return null;
        }

        string status = dragon.CanBreed ? Loc.T("**¡Listo!** ✅") + " — **/dragon-breed**" : Loc.F($"{TimeFormat.Remaining(dragon.Wait)} restantes");
        return $"{DragonRules.KindEmoji(dragon.Kind)} **{Loc.T("Dragón (criar)")}**: {status}";
    }

    private static async Task<string?> TryBuildPetLineAsync(IPetRepository? petRepository, ulong discordId, TimeSpan? feedCooldown)
    {
        if (petRepository is null)
        {
            return null;
        }

        try
        {
            return PetLine(await petRepository.GetOwnedAsync(discordId), feedCooldown, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
            return null;
        }
    }

    // La línea de las mascotas (pura, para probarla sin base): «¡Listo!» si alguna de las que todavía pueden subir de nivel puede comer ahora (y cuántas), o lo que falta para la PRIMERA que pueda.
    // Null si no hay mascotas o todas están al máximo: no hay nada que alimentar y no se muestra una línea inútil.
    public static string? PetLine(IReadOnlyList<OwnedPet> pets, TimeSpan? feedCooldown, DateTime nowUtc)
    {
        var growing = pets.Where(p => !PetRules.IsMaxLevel(p)).ToList();
        if (growing.Count == 0)
        {
            return null;
        }

        int ready = growing.Count(p => PetRules.CanEat(p, nowUtc, feedCooldown));
        if (ready > 0)
        {
            string who = ready == 1 ? Loc.T("1 puede comer") : Loc.F($"{ready} pueden comer");
            return $"🐾 **{Loc.T("Mascotas")}**: " + Loc.T("**¡Listo!** ✅") + Loc.F($" — {who} (**/pet feed**)");
        }

        var next = growing.Min(p => PetRules.RemainingCooldown(p, nowUtc, feedCooldown));
        return $"🐾 **{Loc.T("Mascotas")}**: " + Loc.F($"{TimeFormat.Remaining(next)} restantes") + (growing.Count > 1 ? Loc.T(" (la primera)") : string.Empty);
    }

    private static async Task<string?> TryBuildWorldBossLineAsync(IWorldBossService? worldBossService, ulong discordId, bool other = false)
    {
        if (worldBossService is null)
        {
            return null;
        }

        try
        {
            var listing = await worldBossService.ListAsync(DateTime.UtcNow);
            return WorldBossLine(listing.Entries.Any(e => e.DiscordId == discordId), listing.UntilPlay, other);
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
            return null;
        }
    }

    // La línea del jefe mundial: anotado ✅ con lo que falta para la pelea de las 00:00, o el aviso ⚠️ de que todavía no te anotaste (como la Arena). Pública y pura para probarla sin base.
    // other (v0.51.3): al mirar a OTRO jugador la línea habla de él («todavía no se anotó») y no le da la orden de anotarse.
    public static string WorldBossLine(bool joined, TimeSpan untilPlay, bool other = false) =>
        joined
            ? Loc.F($"🌍 **Jefe mundial**: anotado ✅ — se pelea en {TimeFormat.Remaining(untilPlay)}")
            : other
                ? Loc.F($"🌍 **Jefe mundial**: **todavía no se anotó** ⚠️ Se pelea en {TimeFormat.Remaining(untilPlay)}")
                : Loc.F($"🌍 **Jefe mundial**: **¡todavía no te anotaste!** ⚠️ Se pelea en {TimeFormat.Remaining(untilPlay)}: **/miniboss**");

    private static async Task<string?> TryBuildArenaLineAsync(IArenaService? arenaService, ulong discordId, bool other = false)
    {
        if (arenaService is null)
        {
            return null;
        }

        try
        {
            var listing = await arenaService.ListAsync(DateTime.UtcNow);
            return ArenaLine(listing.Entries.Any(e => e.DiscordId == discordId), listing.UntilPlay, other);
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
            return null;
        }
    }

    // La línea de la Arena (el torneo de hoy se juega a la medianoche de Uruguay): anotado ✅ con lo que falta, o el aviso de que todavía no te anotaste.
    // Pública y pura para probarla sin base.
    public static string ArenaLine(bool joined, TimeSpan untilPlay, bool other = false) =>
        joined
            ? Loc.F($"🏟️ **Arena**: anotado ✅ — se juega en {TimeFormat.Remaining(untilPlay)}")
            : other
                ? Loc.F($"🏟️ **Arena**: **todavía no se anotó** ⚠️ Se juega en {TimeFormat.Remaining(untilPlay)}")
                : Loc.F($"🏟️ **Arena**: **¡todavía no te anotaste!** ⚠️ Se juega en {TimeFormat.Remaining(untilPlay)}: **/arena join**");
}
