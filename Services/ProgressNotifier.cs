using BotDsRpg.GameData;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

// Convierte un evento recién guardado en los avisos que le tocan al jugador: "¡Misión completada!" y "¡Logro desbloqueado!".
public interface IProgressNotifier
{
    // kind/amount: el evento. newTotal: cómo quedó el contador del jugador para ese tipo (lo devuelve el registro de eventos).
    // utcNow se pasa de afuera para poder probarlo en cualquier instante.
    Task<IReadOnlyList<GameNotice>> OnEventAsync(ulong discordId, string kind, long amount, long newTotal, DateTime utcNow);
}

// Avisa JUSTO cuando se cruza la meta ("antes no llegaba, ahora sí"), no cada vez que se suma algo estando por encima, y sin
// guardar nada: el aviso sale de comparar el valor de antes con el de ahora. Si el bot se reinicia entre el evento y el aviso, solo
// se pierde el cartelito (el progreso ya está en la base y se ve en /missions y /achievements).
public sealed class ProgressNotifier(IMissionRepository missionRepository) : IProgressNotifier
{
    public async Task<IReadOnlyList<GameNotice>> OnEventAsync(ulong discordId, string kind, long amount, long newTotal, DateTime utcNow)
    {
        var notices = new List<GameNotice>();

        // Logros: salen del contador de toda la vida, que ya viene calculado (sin consultar la base).
        foreach (var (achievement, tier) in AchievementCatalog.Crossed(kind, newTotal - amount, newTotal))
        {
            notices.Add(new GameNotice(
                $"🏆 **¡Logro desbloqueado!** {achievement.Emoji} **{AchievementCatalog.TierName(achievement, tier)}** — reclamá tu premio con `/achievements`.",
                Public: true));
        }

        // Misiones: solo las de HOY y de ESTA SEMANA que cuentan este tipo de evento. Si ninguna lo cuenta no se consulta nada.
        foreach (var period in new[] { MissionPeriod.Daily, MissionPeriod.Weekly })
        {
            var start = period == MissionPeriod.Daily ? UruguayCalendar.DayStartUtc(utcNow) : UruguayCalendar.WeekStartUtc(utcNow);
            var matching = MissionCatalog.ForPeriod(period, start).Where(m => m.Kind == kind).ToList();
            if (matching.Count == 0)
            {
                continue;
            }

            var progress = await missionRepository.GetProgressAsync(discordId, start, [kind]);
            long total = progress.GetValueOrDefault(kind);

            foreach (var mission in matching.Where(m => total - amount < m.Target && m.Target <= total))
            {
                notices.Add(new GameNotice($"✅ **¡Misión completada!** {mission.Title} — reclamá el premio con `/missions`.", Public: false));
            }
        }

        return notices;
    }
}
