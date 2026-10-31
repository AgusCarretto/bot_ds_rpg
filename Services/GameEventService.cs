using System.Collections.Concurrent;
using System.Globalization;
using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;

namespace BotDsRpg.Services;

// progressNotifier es opcional a propósito: sin él esto es solo el registro (las pruebas del registro lo arman así); con él, cada
// evento guardado también avanza las misiones y los logros y deja sus avisos en la cola.
// referrals también es opcional: con él, cada subida de nivel de un jugador invitado le paga al que lo invitó el premio del nivel 5 (una sola vez; GameData/Referrals.cs).
public sealed class GameEventService(IGameEventRepository eventRepository, IProgressNotifier? progressNotifier = null, IReferralRepository? referrals = null) : IGameEvents
{
    // Avisos pendientes por jugador. En memoria a propósito: si el bot se reinicia entre el evento y el aviso, no pasa nada
    // (el progreso ya está guardado en la base; solo se pierde el cartelito).
    private readonly ConcurrentDictionary<ulong, ConcurrentQueue<GameNotice>> _notices = new();

    public async Task RecordAsync(ulong discordId, string kind, int? zoneId = null, long amount = 1, string? detail = null)
    {
        try
        {
            long newTotal = await eventRepository.RecordAsync(discordId, kind, zoneId, amount, detail);

            // El evento YA está guardado: lo que falle desde acá (calcular un aviso) no lo deshace, solo se pierde el cartelito.
            if (progressNotifier is not null)
            {
                foreach (var notice in await progressNotifier.OnEventAsync(discordId, kind, amount, newTotal, DateTime.UtcNow, detail))
                {
                    Enqueue(discordId, notice);
                }
            }
        }
        catch (Exception ex)
        {
            // Nunca rompe el comando que lo llamó (ver IGameEvents).
            BotLog.Warn(ex);
        }

        if (kind == GameEventKinds.LevelUp && referrals is not null)
        {
            await PayReferralLevelAsync(discordId);
        }
    }

    // El invitado subió de nivel: si llegó al nivel 5 y lo había invitado alguien, ese alguien cobra su premio (la guarda está en la base: se paga una vez). Le avisa a los dos;
    // al que invitó le llega con su próximo comando. Nunca tira: una falla (la tabla no está) solo queda en el log.
    private async Task PayReferralLevelAsync(ulong inviteeId)
    {
        try
        {
            ulong? inviterId = await referrals!.PayLevelBonusAsync(inviteeId);
            if (inviterId is not ulong inviter)
            {
                return;
            }

            string gold = GameHistory.Number(ReferralRules.InviterLevelGold);
            Enqueue(inviter, new GameNotice("", Public: false, new EmbedBuilder()
                .WithColor(new Color(0xE6A23C))
                .WithTitle(Loc.T("🎁 ¡Tu invitado llegó al nivel 5!"))
                .WithDescription(Loc.F($"<@{inviteeId}> llegó al nivel {ReferralRules.RequiredLevel}: cobraste **{gold}** {UiEmojis.Gold} de oro por haberlo invitado."))
                .Build()));
            Enqueue(inviteeId, new GameNotice("", Public: false, new EmbedBuilder()
                .WithColor(new Color(0xE6A23C))
                .WithDescription(Loc.F($"🤝 Llegaste al nivel {ReferralRules.RequiredLevel}: <@{inviter}>, que te invitó, cobró **{gold}** {UiEmojis.Gold} de oro. ¡Gracias por jugar!"))
                .Build()));

            await RecordAsync(inviteeId, GameEventKinds.ReferralLevel, amount: ReferralRules.InviterLevelGold, detail: inviter.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
        }
    }

    public IReadOnlyList<GameNotice> TakeNotices(ulong discordId)
    {
        if (!_notices.TryGetValue(discordId, out var queue))
        {
            return [];
        }

        var taken = new List<GameNotice>();
        var now = DateTime.UtcNow;
        while (queue.TryDequeue(out var notice))
        {
            // Los avisos que vencieron (ver GameNotice.ExpiresUtc) se descartan: no se le entregan a nadie.
            if (notice.ExpiresUtc is { } expires && expires < now)
            {
                continue;
            }

            taken.Add(notice);
        }

        return taken;
    }

    // Para el sistema de misiones y logros: deja un aviso en la cola del jugador.
    internal void Enqueue(ulong discordId, GameNotice notice) =>
        _notices.GetOrAdd(discordId, _ => new ConcurrentQueue<GameNotice>()).Enqueue(notice);
}
