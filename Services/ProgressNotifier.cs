using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;

namespace BotDsRpg.Services;

// Convierte un evento recién guardado en los avisos que le tocan al jugador: "¡Misión completada!", "¡Logro desbloqueado!" y "¡Subiste de nivel!".
public interface IProgressNotifier
{
    // kind/amount: el evento. newTotal: cómo quedó el contador del jugador para ese tipo (lo devuelve el registro de eventos).
    // utcNow se pasa de afuera para poder probarlo en cualquier instante. detail: el detalle del evento tal como se registró (en "level_up",
    // el nivel al que llegó).
    Task<IReadOnlyList<GameNotice>> OnEventAsync(ulong discordId, string kind, long amount, long newTotal, DateTime utcNow, string? detail = null);
}

// Avisa JUSTO cuando se cruza la meta ("antes no llegaba, ahora sí"), no cada vez que se suma algo estando por encima, y sin
// guardar nada: el aviso sale de comparar el valor de antes con el de ahora. Si el bot se reinicia entre el evento y el aviso, solo
// se pierde el cartelito (el progreso ya está en la base y se ve en /missions y /achievements).
//
// La subida de nivel NO es una misión ni un logro, pero sigue el mismo camino: todo lo que da XP ya registra un evento "level_up" (con el
// nivel nuevo como detalle y los niveles subidos como cantidad), así que un solo lugar alcanza para que TODOS los comandos (combate, /daily,
// reclamar misiones y logros, raid) la festejen igual, sin que cada uno arme su propio mensaje. zoneRepository es opcional: sin él, la
// tarjeta simplemente no avisa de zonas nuevas.
public sealed class ProgressNotifier(IMissionRepository missionRepository, IZoneRepository? zoneRepository = null) : IProgressNotifier
{
    // Cuánto vive el aviso de nivel en la cola. En un raid solo recibe el aviso en el acto quien dio el golpe final (el resto lo ve en el
    // mensaje del raid): si no vence, otro jugador lo recibiría horas después, pegado a un comando que no tiene nada que ver.
    private static readonly TimeSpan LevelUpNoticeLifetime = TimeSpan.FromMinutes(2);

    public async Task<IReadOnlyList<GameNotice>> OnEventAsync(
        ulong discordId, string kind, long amount, long newTotal, DateTime utcNow, string? detail = null)
    {
        var notices = new List<GameNotice>();

        if (kind == GameEventKinds.LevelUp && int.TryParse(detail, out int newLevel) && amount > 0)
        {
            notices.Add(await BuildLevelUpNoticeAsync(discordId, newLevel, (int)amount, utcNow));
        }

        notices.AddRange(StoryNotices(kind, amount, newTotal, utcNow, detail));

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

    // Cuánto viven los avisos de la historia que quedan sin entregar (un raid: solo quien dio el golpe final tiene una interacción para contestar). Un capítulo nuevo sigue siendo
    // cierto horas después, a diferencia de un festejo de nivel; igual se descarta para que no le llegue pegado a algo que no tiene nada que ver.
    private static readonly TimeSpan StoryNoticeLifetime = TimeSpan.FromHours(1);

    // La historia (GameData/Lore.cs). Todo sale del evento que ya se guardó, sin consultar la base, y son avisos PRIVADOS y cortos: el texto se lee con /story.
    //   · story_chapter: se abrió un capítulo del Acto I (la primera victoria sobre un jefe; lo registra GameEventExtensions.RecordStoryChapterAsync).
    //   · fuego_nuevo: llegó a un Fuego Nuevo donde se abre una escena (newTotal es el contador de Fuegos Nuevos, uno por reinicio).
    //   · pet_hatched / craft en 1: las frases de la primera mascota y la primera pieza forjada (se avisa justo al cruzar de 0 a 1).
    private static IEnumerable<GameNotice> StoryNotices(string kind, long amount, long newTotal, DateTime utcNow, string? detail)
    {
        if (kind == GameEventKinds.StoryChapter && Lore.FindChapter(detail) is { } chapter)
        {
            yield return new GameNotice(Lore.ChapterNotice(chapter), Public: false, ExpiresUtc: utcNow + StoryNoticeLifetime);
        }
        else if (kind == GameEventKinds.FuegoNuevo && amount > 0 && Lore.SceneNotice((int)newTotal) is { } scenes)
        {
            yield return new GameNotice(scenes, Public: false, ExpiresUtc: utcNow + StoryNoticeLifetime);
        }
        else if (kind == GameEventKinds.PetHatched && amount > 0 && newTotal - amount == 0)
        {
            yield return new GameNotice(Lore.FirstPetLine, Public: false, ExpiresUtc: utcNow + LevelUpNoticeLifetime);
        }
        else if (kind == GameEventKinds.Craft && amount > 0 && newTotal - amount == 0)
        {
            yield return new GameNotice(Lore.FirstForgeLine, Public: false, ExpiresUtc: utcNow + LevelUpNoticeLifetime);
        }
    }

    // La tarjeta de subida de nivel (GameData/LevelUpCard.cs) como un mensaje público y con vencimiento.
    private async Task<GameNotice> BuildLevelUpNoticeAsync(ulong discordId, int newLevel, int levelsGained, DateTime utcNow)
    {
        IReadOnlyList<Zone> zones = [];
        if (zoneRepository is not null)
        {
            try
            {
                zones = await zoneRepository.GetAllAsync();
            }
            catch (Exception ex)
            {
                // Las zonas son un extra de la tarjeta: sin ellas, igual se festeja el nivel.
                BotLog.Warn(ex);
            }
        }

        var card = LevelUpCard.Compose(discordId, newLevel, levelsGained, zones);

        var embed = new EmbedBuilder()
            .WithTitle(card.Title)
            .WithColor(Color.Gold)
            .WithDescription(card.Description)
            .WithFooter(card.Footer);
        foreach (var field in card.Fields)
        {
            embed.AddField(field.Name, field.Value, field.Inline);
        }

        return new GameNotice(string.Empty, Public: true, embed.Build(), utcNow + LevelUpNoticeLifetime);
    }
}
