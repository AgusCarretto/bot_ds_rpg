using System.Globalization;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Microsoft.Extensions.Configuration;

namespace BotDsRpg.Services;

// Navidad en el Fogón (v0.57.0, docs/navidad.md): la etapa del evento según la fecha y el cobro de Regalos por victorias y por el /daily.
public interface IChristmasService
{
    ChristmasSettings Settings { get; }

    ChristmasPhase PhaseNow();

    // Suma los Regalos de una victoria (GameEventKinds.HuntWin, TravelWin, BossWin, RaidWin) o del /daily (DailyClaim, con la racha).
    // Devuelve cuántos dio: 0 fuera del farmeo o si no tocó. NUNCA tira (como IGameEvents): un fallo acá no le cuesta la victoria a nadie.
    Task<int> GrantAsync(ulong discordId, string kind, int streakDay = 0);
}

public sealed class ChristmasService(IChristmasRepository repository, ChristmasSettings settings, Func<DateTime>? clock = null) : IChristmasService
{
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);

    public ChristmasSettings Settings => settings;

    public ChristmasPhase PhaseNow() => ChristmasRules.PhaseAt(settings, _clock());

    public async Task<int> GrantAsync(ulong discordId, string kind, int streakDay = 0)
    {
        if (PhaseNow() != ChristmasPhase.Farm)
        {
            return 0;
        }

        int amount = kind == GameEventKinds.DailyClaim
            ? ChristmasRules.DailyClaimGifts(streakDay)
            : ChristmasRules.VictoryGifts(kind, Random.Shared.Next(100));
        if (amount <= 0)
        {
            return 0;
        }

        try
        {
            // Con la Vela prendida el repositorio suma +50 %: lo que se muestra es lo que se sumó de verdad.
            return await repository.GrantAsync(discordId, settings.Season, amount);
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
            return 0;
        }
    }

    // Las fechas de la configuración (Christmas__Start, Christmas__FarmEnd, Christmas__End, yyyy-MM-dd de Uruguay); sin ellas, las de 2026.
    public static ChristmasSettings LoadSettings(IConfiguration configuration)
    {
        var defaults = ChristmasSettings.Default;
        DateOnly Read(string key, DateTime fallbackUtc)
        {
            string? raw = configuration[$"Christmas:{key}"];
            return raw is not null && DateOnly.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day
                : DateOnly.FromDateTime(fallbackUtc + UruguayCalendar.Offset);
        }

        return ChristmasSettings.FromLocalDates(
            Read("Start", defaults.StartUtc), Read("FarmEnd", defaults.FarmEndUtc), Read("End", defaults.EndUtc));
    }
}

// La línea de Regalos que se suma al final de una pantalla de victoria (o del /daily). Sin Regalos devuelve el embed tal cual.
public static class ChristmasDisplay
{
    public static Embed WithGifts(Embed embed, int gifts)
    {
        if (gifts <= 0 || embed.Fields.Length >= EmbedBuilder.MaxFieldCount)
        {
            return embed;
        }

        return embed.ToEmbedBuilder()
            .AddField(Loc.T("❄️ Navidad en el Fogón"), Loc.F($"🎁 **+{gifts}** Regalos"), false)
            .Build();
    }

    // Lo mismo para la pantalla del jefe en equipo: una línea por jugador que se llevó Regalos.
    public static Embed WithGifts(Embed embed, IReadOnlyList<(string Name, int Gifts)> gifts)
    {
        var lines = gifts.Where(g => g.Gifts > 0).Select(g => Loc.F($"🎁 **{g.Name}**: +{g.Gifts} Regalos")).ToList();
        if (lines.Count == 0 || embed.Fields.Length >= EmbedBuilder.MaxFieldCount)
        {
            return embed;
        }

        return embed.ToEmbedBuilder()
            .AddField(Loc.T("❄️ Navidad en el Fogón"), string.Join("\n", lines), false)
            .Build();
    }
}
