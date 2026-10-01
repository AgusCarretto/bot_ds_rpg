using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using static AutocompleteText;

// Lista desplegable de /zona: todas las zonas en orden de dificultad, cada una marcada con si podés
// entrar ahora o por qué no (📍 estás acá / 🔒 te falta nivel / 🔒 hay que derrotar a un jefe). El valor
// de cada opción es el ID de la zona, así que ZoneModule.ExecuteTravelAsync no cambia — y como las
// zonas bloqueadas también se listan, elegir una devuelve el mismo aviso de siempre en vez de ocultarlas
// (es útil ver cuál es la próxima y qué falta para llegar).
public static class ZoneChoices
{
    // bossNameByZoneId: nombre del jefe de cada zona que actúa de "guardián" (solo hace falta el de las
    // zonas que frenan el paso a otra; ver ZoneRanking.PendingGatekeeperZone).
    public static IReadOnlyList<AutocompleteResult> For(
        IReadOnlyList<Zone> zones, User player, IReadOnlyDictionary<int, string> bossNameByZoneId, string typed)
    {
        var ordered = ZoneRanking.OrderByDifficulty(zones);

        return ordered
            .Where(zone => MatchesTyped(zone, typed))
            .Take(MaxChoices)
            .Select(zone =>
            {
                string status = string.Empty;

                if (zone.ZoneId == player.CurrentZoneId)
                {
                    status = " · 📍 estás acá";
                }
                else if (player.Level < zone.MinLevel)
                {
                    status = " · 🔒 te falta nivel";
                }
                else if (ZoneRanking.PendingGatekeeperZone(ordered, zone.ZoneId, player.HighestZoneCleared) is { } gatekeeper
                    && bossNameByZoneId.TryGetValue(gatekeeper.ZoneId, out string? bossName))
                {
                    // Si esa zona no tiene jefe cargado no hay a quién derrotar: no se marca como bloqueada
                    // (mismo criterio que ExecuteTravelAsync).
                    status = $" · 🔒 derrotá a {bossName}";
                }

                string emoji = string.IsNullOrEmpty(zone.Emoji) ? "🗺️" : zone.Emoji;
                return new AutocompleteResult(
                    Truncate($"{emoji} {zone.ZoneId}. {zone.Name} (nivel {zone.MinLevel}){status}"), zone.ZoneId);
            })
            .ToList();
    }

    // Se puede buscar por nombre ("bosque") o por ID ("4"): el parámetro de /zona es el ID numérico.
    private static bool MatchesTyped(Zone zone, string typed) =>
        Matches(zone.Name, typed)
        || (!string.IsNullOrWhiteSpace(typed) && zone.ZoneId.ToString().StartsWith(typed.Trim(), StringComparison.Ordinal));
}

public sealed class ZoneAutocompleteHandler : SafeAutocompleteHandler
{
    protected override async Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services)
    {
        // GetByDiscordIdAsync (no GetOrCreate): abrir una lista no tiene que crearle cuenta a nadie.
        var player = await services.GetRequiredService<IUserRepository>().GetByDiscordIdAsync(userId);
        if (player is null)
        {
            return [];
        }

        var zones = await services.GetRequiredService<IZoneRepository>().GetAllAsync();
        var ordered = ZoneRanking.OrderByDifficulty(zones);

        // Solo se consulta el jefe de las zonas que de verdad frenan a este jugador en alguna zona.
        var monsters = services.GetRequiredService<IMonsterRepository>();
        var bossNames = new Dictionary<int, string>();
        foreach (var zone in ordered)
        {
            if (ZoneRanking.PendingGatekeeperZone(ordered, zone.ZoneId, player.HighestZoneCleared) is { } gatekeeper
                && !bossNames.ContainsKey(gatekeeper.ZoneId)
                && await monsters.GetBossByZoneAsync(gatekeeper.ZoneId) is { } boss)
            {
                bossNames[gatekeeper.ZoneId] = boss.Name;
            }
        }

        return ZoneChoices.For(zones, player, bossNames, typed);
    }
}
