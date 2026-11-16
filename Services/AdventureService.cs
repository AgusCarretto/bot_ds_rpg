using System.Collections.Concurrent;
using BotDsRpg.GameData;

namespace BotDsRpg.Services;

public enum AdventurePhase
{
    Choosing,   // en una escena, eligiendo camino (reloj de 3 minutos)
    Fighting,   // en una pelea (la maneja ICombatSessionService con su reloj por turno)
    Ended,
}

// Una aventura en curso (v0.57.0, docs/aventura.md). Se toca SIEMPRE con Lock tomado y sin await adentro.
public sealed class AdventureRun
{
    public required ulong OwnerId { get; init; }
    public required IReadOnlyList<AdventureScene> Scenes { get; init; }
    public Guid RunId { get; } = Guid.NewGuid();
    public object Lock { get; } = new();

    // Corto para los ids de los botones: un botón de una corrida vieja no vale en la nueva.
    public string Token => RunId.ToString("N")[..8];

    public int Scene { get; set; } = 1;                 // 1..5 (la 5 es el Guardián)
    public int LootPercent { get; set; }                // el botín acumulado (+20 por cada arriesgado, +30 por un cofre)
    public int DamageBonusPercent { get; set; }         // el santuario
    public string? LastNote { get; set; }               // lo que pasó en la escena anterior (un evento, una pelea ganada)
    public AdventurePhase Phase { get; set; } = AdventurePhase.Choosing;
    public bool PendingRiskyBonus { get; set; }         // la pelea en curso es la del camino arriesgado: si se gana, suma el +20
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

    public AdventureScene CurrentScene => Scene >= AdventureRules.Scenes ? AdventureRules.Final : Scenes[Scene - 1];
    public bool AtGuardian => Scene >= AdventureRules.Scenes;
}

public interface IAdventureService
{
    // Una corrida nueva (null si ya tiene una activa).
    AdventureRun? Start(ulong ownerId, Random random);

    // La corrida activa (null si no hay, si terminó o si se le venció el reloj).
    AdventureRun? Peek(ulong ownerId);

    bool IsInAdventure(ulong ownerId);

    // La corrida aunque su pelea se acabe de cerrar (para seguir después de ganar): null solo si no hay o ya terminó.
    AdventureRun? Find(ulong ownerId);

    void End(ulong ownerId);
}

// En memoria (como las mazmorras): si el bot se reinicia a mitad, la corrida se pierde (el cooldown ya se cobró; no se paga nada).
// Una corrida eligiendo camino vence a los 3 minutos sin tocar nada; peleando, vence si su pelea ya no existe (el reloj de la pelea la cerró).
public sealed class AdventureService(ICombatSessionService combatSessions) : IAdventureService
{
    private static readonly TimeSpan FightGrace = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<ulong, AdventureRun> _runs = new();

    public AdventureRun? Start(ulong ownerId, Random random)
    {
        if (Peek(ownerId) is not null)
        {
            return null;
        }

        var run = new AdventureRun { OwnerId = ownerId, Scenes = AdventureRules.PickScenes(random) };
        _runs[ownerId] = run;
        return run;
    }

    public AdventureRun? Peek(ulong ownerId)
    {
        if (!_runs.TryGetValue(ownerId, out var run))
        {
            return null;
        }

        bool alive;
        lock (run.Lock)
        {
            alive = run.Phase switch
            {
                AdventurePhase.Choosing => DateTime.UtcNow - run.LastActivityUtc < AdventureRules.ChoiceTimeout,
                // Peleando: viva mientras exista su pelea, y unos segundos de gracia (al ganar, la pelea se cierra un instante antes de que la aventura siga).
                AdventurePhase.Fighting => combatSessions.Peek(ownerId)?.State.CommandName == AdventureRules.CommandName
                    || DateTime.UtcNow - run.LastActivityUtc < FightGrace,
                _ => false,
            };
            if (!alive)
            {
                run.Phase = AdventurePhase.Ended;
            }
        }

        if (!alive)
        {
            ((ICollection<KeyValuePair<ulong, AdventureRun>>)_runs).Remove(new KeyValuePair<ulong, AdventureRun>(ownerId, run));
            return null;
        }

        return run;
    }

    public bool IsInAdventure(ulong ownerId) => Peek(ownerId) is not null;

    public AdventureRun? Find(ulong ownerId)
    {
        if (!_runs.TryGetValue(ownerId, out var run))
        {
            return null;
        }

        lock (run.Lock)
        {
            return run.Phase == AdventurePhase.Ended ? null : run;
        }
    }

    public void End(ulong ownerId)
    {
        if (_runs.TryRemove(ownerId, out var run))
        {
            lock (run.Lock)
            {
                run.Phase = AdventurePhase.Ended;
            }
        }
    }
}
