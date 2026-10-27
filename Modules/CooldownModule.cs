using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using BotDsRpg.Services;

public class CooldownModule(
    ICooldownRepository cooldownRepository, IUserRepository userRepository, IArenaService arenaService, IPlayerBonusService bonusService, ITopGgVoteClient voteClient, IPetRepository petRepository, IDragonRepository dragonRepository)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("cd", "Mirá tus cooldowns: cazar, viajar, talar, minar, jefe, cajas, mascotas, diario y la Arena de hoy.")]
    public async Task HandleCooldownsAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var embed = await BuildStatusEmbedAsync(cooldownRepository, userRepository, Context.User.Id, arenaService, bonusService, voteClient.Enabled, petRepository, dragonRepository);
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
    public static async Task<Embed> BuildStatusEmbedAsync(
        ICooldownRepository cooldownRepository, IUserRepository userRepository, ulong discordId, IArenaService? arenaService = null, IPlayerBonusService? bonusService = null, bool voteEnabled = false,
        IPetRepository? petRepository = null, IDragonRepository? dragonRepository = null)
    {
        var lines = new List<string>();
        var player = await userRepository.GetOrCreateUserAsync(discordId);

        async Task AddLineAsync(CooldownDefinition definition)
        {
            var remaining = await cooldownRepository.GetRemainingAsync(discordId, definition.CommandName, definition.Duration);
            string status = remaining is null
                ? Loc.T("**¡Listo!** ✅")
                : Loc.F($"{TimeFormat.Remaining(remaining.Value)} restantes");

            lines.Add($"{definition.Emoji} **{Loc.Tr(definition.DisplayName)}**: {status}");
        }

        foreach (var definition in CooldownCatalog.All)
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
        }

        if (voteEnabled)
        {
            await AddLineAsync(CooldownCatalog.Vote);
        }

        // La comida de las mascotas: una hora por mascota (menos con la bendición Buen Pienso), y no vive en la tabla cooldowns sino en player_pets.
        if (await TryBuildPetLineAsync(petRepository, discordId, bonuses?.PetFeedCooldown) is { } petLine)
        {
            lines.Add(petLine);
        }

        // El dragón (v0.30.0): cuándo puede volver a criar (el descanso de 12 h vive en player_dragons, no en la tabla cooldowns). Sin dragón, o en el tier máximo (no hay nada que criar), no sale.
        if (await TryBuildDragonLineAsync(dragonRepository, discordId) is { } dragonLine)
        {
            lines.Add(dragonLine);
        }

        // /daily no usa la tabla cooldowns (tiene su propia columna last_daily_claim con
        // ventana de 24h/48h), así que lo evaluamos aparte con la misma lógica pura de /daily.
        var dailyCalculation = DailyRewardCalculator.Evaluate(player.LastDailyClaim, player.DailyStreak, DateTime.UtcNow);
        string dailyStatus = dailyCalculation.Status == DailyClaimStatus.TooSoon
            ? Loc.F($"{TimeFormat.Remaining(dailyCalculation.RemainingCooldown!.Value)} restantes")
            : Loc.T("**¡Listo!** ✅");
        lines.Add($"🎁 **{Loc.T("Diario")}**: {dailyStatus}");

        if (await TryBuildArenaLineAsync(arenaService, discordId) is { } arenaLine)
        {
            lines.Add(arenaLine);
        }

        return new EmbedBuilder()
            .WithTitle(Loc.T("⏱️ Tus cooldowns"))
            .WithColor(Color.Teal)
            .WithDescription(string.Join('\n', lines))
            .Build();
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
            return $"🐾 **{Loc.T("Mascotas")}**: " + Loc.T("**¡Listo!** ✅") + $" — {who} (**/pet feed**)";
        }

        var next = growing.Min(p => PetRules.RemainingCooldown(p, nowUtc, feedCooldown));
        return $"🐾 **{Loc.T("Mascotas")}**: " + Loc.F($"{TimeFormat.Remaining(next)} restantes") + (growing.Count > 1 ? Loc.T(" (la primera)") : string.Empty);
    }

    private static async Task<string?> TryBuildArenaLineAsync(IArenaService? arenaService, ulong discordId)
    {
        if (arenaService is null)
        {
            return null;
        }

        try
        {
            var listing = await arenaService.ListAsync(DateTime.UtcNow);
            return ArenaLine(listing.Entries.Any(e => e.DiscordId == discordId), listing.UntilPlay);
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
            return null;
        }
    }

    // La línea de la Arena (el torneo de hoy se juega a la medianoche de Uruguay): anotado ✅ con lo que falta, o el aviso de que todavía no te anotaste.
    // Pública y pura para probarla sin base.
    public static string ArenaLine(bool joined, TimeSpan untilPlay) =>
        joined
            ? Loc.F($"🏟️ **Arena**: anotado ✅ — se juega en {TimeFormat.Remaining(untilPlay)}")
            : Loc.F($"🏟️ **Arena**: **¡todavía no te anotaste!** ⚠️ Se juega en {TimeFormat.Remaining(untilPlay)}: **/arena join**");
}
