using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using BotDsRpg.Services;

public class CooldownModule(ICooldownRepository cooldownRepository, IUserRepository userRepository, IArenaService arenaService, IPlayerBonusService bonusService)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("cd", "Mirá tus cooldowns: cazar, viajar, talar, minar, jefe, raid, cajas, diario y la Arena de hoy.")]
    public async Task HandleCooldownsAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var embed = await BuildStatusEmbedAsync(cooldownRepository, userRepository, Context.User.Id, arenaService, bonusService);
            await FollowupAsync(embed: embed, ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("No pude consultar tus cooldowns ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Estático (sin dependencia de Context) para que Modules/TextCommandModule.cs use exactamente
    // la misma lógica en "aa cd" — acá vive tanto el cálculo como el embed, no hay nada más que compartir.
    // arenaService: con él, la última línea dice si ya estás anotado en la Arena de hoy y cuánto falta para que se juegue (para que nadie se olvide de
    // anotarse). Sin él (o si falla) la línea simplemente no sale: /cd nunca se rompe por la Arena.
    // bonusService (v0.13.0): con él, la tala y la minería avanzadas (oficios al nivel 100) aparecen en la lista solo para quien ya las desbloqueó.
    public static async Task<Embed> BuildStatusEmbedAsync(
        ICooldownRepository cooldownRepository, IUserRepository userRepository, ulong discordId, IArenaService? arenaService = null, IPlayerBonusService? bonusService = null)
    {
        var lines = new List<string>();
        var player = await userRepository.GetOrCreateUserAsync(discordId);

        async Task AddLineAsync(CooldownDefinition definition)
        {
            var remaining = await cooldownRepository.GetRemainingAsync(discordId, definition.CommandName, definition.Duration);
            string status = remaining is null
                ? "**¡Listo!** ✅"
                : $"{TimeFormat.Remaining(remaining.Value)} restantes";

            lines.Add($"{definition.Emoji} **{definition.DisplayName}**: {status}");
        }

        foreach (var definition in CooldownCatalog.All)
        {
            await AddLineAsync(definition);
        }

        // Las versiones avanzadas de /chop y /mine: solo con el oficio al nivel máximo (GameData/ProfessionRules.cs).
        if (bonusService is not null)
        {
            var bonuses = await bonusService.GetAsync(discordId, player.FuegoNuevo);
            if (ProfessionRules.AdvancedUnlocked(bonuses.ProfessionLevel(ProfessionCatalog.WoodcutterKey)))
            {
                await AddLineAsync(CooldownCatalog.ChopAdvanced);
            }

            if (ProfessionRules.AdvancedUnlocked(bonuses.ProfessionLevel(ProfessionCatalog.MinerKey)))
            {
                await AddLineAsync(CooldownCatalog.MineAdvanced);
            }
        }

        // /daily no usa la tabla cooldowns (tiene su propia columna last_daily_claim con
        // ventana de 24h/48h), así que lo evaluamos aparte con la misma lógica pura de /daily.
        var dailyCalculation = DailyRewardCalculator.Evaluate(player.LastDailyClaim, player.DailyStreak, DateTime.UtcNow);
        string dailyStatus = dailyCalculation.Status == DailyClaimStatus.TooSoon
            ? $"{TimeFormat.Remaining(dailyCalculation.RemainingCooldown!.Value)} restantes"
            : "**¡Listo!** ✅";
        lines.Add($"🎁 **Diario**: {dailyStatus}");

        if (await TryBuildArenaLineAsync(arenaService, discordId) is { } arenaLine)
        {
            lines.Add(arenaLine);
        }

        return new EmbedBuilder()
            .WithTitle("⏱️ Tus cooldowns")
            .WithColor(Color.Teal)
            .WithDescription(string.Join('\n', lines))
            .Build();
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
            ? $"🏟️ **Arena**: anotado ✅ — se juega en {TimeFormat.Remaining(untilPlay)}"
            : $"🏟️ **Arena**: **¡todavía no te anotaste!** ⚠️ Se juega en {TimeFormat.Remaining(untilPlay)}: **/arena join**";
}
