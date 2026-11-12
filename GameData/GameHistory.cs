using System.Globalization;
using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Lo que le pasó a un jugador en UN juego: cuántas veces ganó y cuántas perdió (y, en el casino, cuánto oro ganó y perdió). Played = Won + Lost.
public sealed record GameRecord(string Emoji, string Name, long Won, long Lost, long GoldWon = 0, long GoldLost = 0, bool IsCasino = false)
{
    public long Played => Won + Lost;
}

// El historial completo: los juegos que jugó (en un orden fijo) y lo de la Arena, que es distinto (se anota y a veces gana el torneo).
public sealed record GameHistoryReport(IReadOnlyList<GameRecord> Games, long ArenaJoined, long ArenaTitles);

// El armado PURO del historial por juego (/history) a partir de los eventos que ya registra el bot (game_events). No guarda nada: es un resumen de
// lo registrado. Cuenta desde que cada juego empezó a registrarse (las cacerías y demás, desde la v0.6; el casino y los duelos, desde que existen
// sus eventos), no desde siempre — el pie del mensaje lo aclara.
public static class GameHistory
{
    // Los tipos de evento que lee el historial.
    public static readonly IReadOnlyCollection<string> Kinds =
    [
        GameEventKinds.HuntWin, GameEventKinds.TravelWin, GameEventKinds.BossWin, GameEventKinds.RaidWin, GameEventKinds.FightLost,
        GameEventKinds.DuelWin, GameEventKinds.DuelLoss, GameEventKinds.ArenaJoin, GameEventKinds.ArenaWin,
        GameEventKinds.CasinoWin, GameEventKinds.CasinoLoss,
        GameEventKinds.RpsWin, GameEventKinds.RpsLoss, GameEventKinds.RpsGoldWon, GameEventKinds.RpsGoldLost,
    ];

    public static GameHistoryReport Build(IReadOnlyList<EventTotal> totals)
    {
        long Count(string kind, string? detail = null) =>
            totals.Where(t => t.Kind == kind && (detail is null || string.Equals(t.Detail, detail, StringComparison.OrdinalIgnoreCase))).Sum(t => t.Count);

        long Gold(string kind, string detail) =>
            totals.Where(t => t.Kind == kind && string.Equals(t.Detail, detail, StringComparison.OrdinalIgnoreCase)).Sum(t => t.Amount);

        long AllGold(string kind) => totals.Where(t => t.Kind == kind).Sum(t => t.Amount);

        var games = new List<GameRecord>
        {
            new("🏹", Loc.T("Cacería"), Count(GameEventKinds.HuntWin), Count(GameEventKinds.FightLost, "hunt")),
            new("🗺️", Loc.T("Viaje"), Count(GameEventKinds.TravelWin), Count(GameEventKinds.FightLost, "travel")),
            new("👑", Loc.T("Jefe"), Count(GameEventKinds.BossWin), Count(GameEventKinds.FightLost, "boss")),
            new("🛡️", Loc.T("Jefe en equipo"), Count(GameEventKinds.RaidWin), Count(GameEventKinds.FightLost, "raid")),
            new("🥊", Loc.T("Duelos"), Count(GameEventKinds.DuelWin), Count(GameEventKinds.DuelLoss)),
            new("🎰", Loc.T("Tragamonedas"), Count(GameEventKinds.CasinoWin, "slots"), Count(GameEventKinds.CasinoLoss, "slots"),
                Gold(GameEventKinds.CasinoWin, "slots"), Gold(GameEventKinds.CasinoLoss, "slots"), IsCasino: true),
            new("🪙", Loc.T("Cara o cruz"), Count(GameEventKinds.CasinoWin, "coinflip"), Count(GameEventKinds.CasinoLoss, "coinflip"),
                Gold(GameEventKinds.CasinoWin, "coinflip"), Gold(GameEventKinds.CasinoLoss, "coinflip"), IsCasino: true),
            new("🃏", "Blackjack", Count(GameEventKinds.CasinoWin, "blackjack"), Count(GameEventKinds.CasinoLoss, "blackjack"),
                Gold(GameEventKinds.CasinoWin, "blackjack"), Gold(GameEventKinds.CasinoLoss, "blackjack"), IsCasino: true),
            // v0.54.0: el detail es el rival, así que el oro se suma de todos los rivales.
            new("✊", Loc.T("Piedra, papel o tijera"), Count(GameEventKinds.RpsWin), Count(GameEventKinds.RpsLoss),
                AllGold(GameEventKinds.RpsGoldWon), AllGold(GameEventKinds.RpsGoldLost), IsCasino: true),
        };

        return new GameHistoryReport(
            games.Where(g => g.Played > 0).ToList(), Count(GameEventKinds.ArenaJoin), Count(GameEventKinds.ArenaWin));
    }

    // Título y texto del campo de UN juego: "🏹 Cacería · 120 veces" y debajo ganadas / perdidas / % (y el oro, si es de casino).
    public static (string Title, string Value) Describe(GameRecord game)
    {
        string title = $"{game.Emoji} {game.Name} · {Plural(game.Played, Loc.T("vez"), Loc.T("veces"))}";
        string rate = game.Played == 0 ? "—" : $"{(int)Math.Round(game.Won * 100.0 / game.Played)}%";

        var lines = new List<string>
        {
            Loc.F($"✅ **{Number(game.Won)}** ganada(s) · ❌ **{Number(game.Lost)}** perdida(s) · 📊 {rate}"),
        };

        if (game.IsCasino)
        {
            long net = game.GoldWon - game.GoldLost;
            lines.Add(Loc.F($"💰 Ganaste **+{Number(game.GoldWon)}** · perdiste **−{Number(game.GoldLost)}**"));
            lines.Add(Loc.F($"{(net >= 0 ? "📈" : "📉")} Balance: **{(net >= 0 ? "+" : "−")}{Number(Math.Abs(net))}** de oro"));
        }

        return (title, string.Join('\n', lines));
    }

    // La Arena: torneos en los que se anotó y cuántos ganó. Null si nunca se anotó.
    public static (string Title, string Value)? DescribeArena(GameHistoryReport report) =>
        report.ArenaJoined + report.ArenaTitles == 0
            ? null
            : (Loc.T("🏟️ Arena"), Loc.F($"🏁 Te anotaste en **{Number(Math.Max(report.ArenaJoined, report.ArenaTitles))}** torneo(s) · 🏆 **{Number(report.ArenaTitles)}** campeonato(s)"));

    // 1234567 -> "1.234.567" en español (el punto como separador de miles, como se escribe en Uruguay) y "1,234,567" en inglés (Loc.Current), sin depender de la cultura del servidor.
    public static string Number(long value)
    {
        string text = value.ToString("N0", CultureInfo.InvariantCulture);
        return Loc.Current == Loc.English ? text : text.Replace(',', '.');
    }

    private static string Plural(long n, string one, string many) => $"{Number(n)} {(n == 1 ? one : many)}";
}

// Los duelos de un jugador: su récord y los últimos rivales. Pura: recibe los totales y los últimos eventos que ya leyó el módulo.
public static class DuelHistory
{
    public static readonly IReadOnlyCollection<string> Kinds = [GameEventKinds.DuelWin, GameEventKinds.DuelLoss];

    public const int RecentCount = 10;

    // (Título, descripción) del embed de /duels. now se pasa de afuera para probarlo.
    public static (string Title, string Description) Describe(string playerName, IReadOnlyList<EventTotal> totals, IReadOnlyList<RecentEvent> recent, DateTime utcNow)
    {
        long wins = totals.Where(t => t.Kind == GameEventKinds.DuelWin).Sum(t => t.Count);
        long losses = totals.Where(t => t.Kind == GameEventKinds.DuelLoss).Sum(t => t.Count);
        string title = Loc.F($"🥊 Duelos de {playerName}");

        if (wins + losses == 0)
        {
            return (title, Loc.T("Todavía no hiciste ningún duelo. Desafiá a alguien con **/fight @jugador**: es amistoso, no se pierde nada."));
        }

        long played = wins + losses;
        var lines = new List<string>
        {
            Loc.F($"✅ **{GameHistory.Number(wins)}** ganado(s) · ❌ **{GameHistory.Number(losses)}** perdido(s) · 📊 {(int)Math.Round(wins * 100.0 / played)}% de victorias"),
        };

        // La racha: cuántos seguidos, del más nuevo para atrás, con el mismo resultado.
        if (recent.Count > 0)
        {
            string newest = recent[0].Kind;
            int streak = recent.TakeWhile(e => e.Kind == newest).Count();
            if (streak >= 2)
            {
                lines.Add(newest == GameEventKinds.DuelWin ? Loc.F($"🔥 Racha: **{streak}** victorias seguidas") : Loc.F($"🧊 Racha: **{streak}** derrotas seguidas"));
            }
        }

        lines.Add(string.Empty);
        lines.Add(Loc.F($"**Últimos {Math.Min(recent.Count, RecentCount)}:**"));
        foreach (var duel in recent.Take(RecentCount))
        {
            string rival = string.IsNullOrWhiteSpace(duel.Detail) ? Loc.T("un rival") : duel.Detail;
            string when = $"<t:{new DateTimeOffset(DateTime.SpecifyKind(duel.OccurredAtUtc, DateTimeKind.Utc)).ToUnixTimeSeconds()}:R>";
            lines.Add(duel.Kind == GameEventKinds.DuelWin
                ? Loc.F($"✅ Le ganaste a **{rival}** · {when}")
                : Loc.F($"❌ Perdiste contra **{rival}** · {when}"));
        }

        return (title, string.Join('\n', lines));
    }
}
