namespace BotDsRpg.GameData;

// El resumen de PvP que se ve en /profile, a partir de los contadores del jugador (player_stats: duel_win, duel_loss, arena_join, arena_win).
// Null si nunca peleó ni se anotó: el perfil no suma una línea vacía a quien no juega PvP.
public static class PvpStats
{
    public static string? Describe(IReadOnlyDictionary<string, long> stats)
    {
        long wins = stats.GetValueOrDefault(GameEventKinds.DuelWin);
        long losses = stats.GetValueOrDefault(GameEventKinds.DuelLoss);
        long arenas = stats.GetValueOrDefault(GameEventKinds.ArenaWin);
        long joined = stats.GetValueOrDefault(GameEventKinds.ArenaJoin);

        var lines = new List<string>();

        if (wins + losses > 0)
        {
            lines.Add($"🥊 Duelos: **{wins}** ganado(s) · **{losses}** perdido(s)");
        }

        if (arenas + joined > 0)
        {
            lines.Add($"🏟️ Arena: **{arenas}** campeonato(s) · anotado en **{Math.Max(joined, arenas)}** torneo(s)");
        }

        return lines.Count == 0 ? null : string.Join('\n', lines);
    }
}
