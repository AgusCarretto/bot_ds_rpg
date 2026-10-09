using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Los recordatorios de cooldown (v0.16.0): cuando termina la espera de algo, el bot te avisa en el canal donde lo usaste. Todo lo que se lee y las cuentas de CUÁNDO es puro y vive acá;
// el guardado está en Repositories/ReminderRepository.cs, el reloj en Services/ReminderScheduler.cs y la pantalla de ajustes en Modules/ReminderModule.cs.
//
// Un recordatorio es una fila (jugador, tipo, canal, cuándo): el tipo es la misma clave que usa el cooldown de la base ("hunt", "travel", "chop", "mine", "boss", "buybox", "chop_adv",
// "mine_adv") más dos que no viven en la tabla de cooldowns: "pet" (la comida de las mascotas, una hora por mascota) y "daily" (la recompensa diaria, 24 horas).
// AutoDelete: los avisos de esperas CORTAS (cacería, talar, minar) se borran solos a los pocos minutos para no ensuciar el canal; los de esperas largas se quedan.
public sealed record ReminderKind(string Key, string Emoji, string Name, string Command, string ReadyText, TimeSpan? AutoDelete);

// Una fila para guardar: de qué se trata y cuándo.
public sealed record ReminderDue(string Kind, DateTime DueUtc);

public static class ReminderCatalog
{
    // Lo que se borra solo (esperas cortas) a los 2 minutos.
    public static readonly TimeSpan ShortLived = TimeSpan.FromMinutes(2);

    public static readonly IReadOnlyList<ReminderKind> All =
    [
        new("hunt", "🏹", "Cacería", "/hunt", "¡Cacería lista! Ya podés volver a cazar con `/hunt`.", ShortLived),
        new("travel", "🗺️", "Viaje", "/travel", "¡Viaje listo! Ya podés salir de expedición con `/travel`.", null),
        new("chop", "🪓", "Talar", "/chop", "¡Ya podés talar de nuevo con `/chop`!", ShortLived),
        new("mine", "⛏️", "Minar", "/mine", "¡Ya podés minar de nuevo con `/mine`!", ShortLived),
        new("boss", "👑", "Jefe / Raid", "/boss", "¡Jefe y raid listos! Ya podés enfrentar al jefe con `/boss` o armar un `/raid`.", null),
        new("buybox", "📦", "Comprar caja", "/taberna", "¡Ya podés comprar otra caja! Pasá por la `/taberna`.", null),
        new("chop_adv", "🪓", "Tala avanzada", "/chop", "¡La tala avanzada está lista! `/chop modo:Avanzada`.", null),
        new("mine_adv", "⛏️", "Minería avanzada", "/mine", "¡La minería avanzada está lista! `/mine modo:Avanzada`.", null),
        new("pet", "🐾", "Mascotas", "/pet feed", "¡Tus mascotas ya pueden comer! Dales de comer con `/pet feed`.", null),
        new("daily", "🎁", "Diario", "/daily", "¡Tu recompensa diaria está lista! Reclamala con `/daily`.", null),
    ];

    public static ReminderKind? Find(string? key) => All.FirstOrDefault(k => k.Key == key);

    public static IReadOnlyList<string> Keys => All.Select(k => k.Key).ToList();

    // Las esperas que viven en la tabla de cooldowns, con su duración (la misma de CooldownCatalog, que es la que cobra cada comando).
    private static readonly IReadOnlyList<CooldownDefinition> StoredCooldowns =
        [.. CooldownCatalog.All, CooldownCatalog.ChopAdvanced, CooldownCatalog.MineAdvanced];

    // De las filas de cooldown del jugador (comando, última vez) saca cuáles TODAVÍA corren y cuándo terminan. Lo que ya terminó no genera aviso (el jugador ya puede usarlo).
    // Un cooldown que se guardó con la espera "corta" de un jefe perdido ya viene acomodado en last_executed_at (ver CooldownCatalog.Boss), así que acá es siempre última vez + duración.
    public static IReadOnlyList<ReminderDue> FromCooldowns(IEnumerable<(string Command, DateTime LastExecutedUtc)> rows, DateTime nowUtc)
    {
        var due = new List<ReminderDue>();
        foreach (var (command, last) in rows)
        {
            var definition = StoredCooldowns.FirstOrDefault(d => d.CommandName == command);
            if (definition is null || Find(command) is null)
            {
                continue;
            }

            var ready = DateTime.SpecifyKind(last, DateTimeKind.Utc) + definition.Duration;
            if (ready > nowUtc)
            {
                due.Add(new ReminderDue(command, ready));
            }
        }

        return due;
    }

    // La espera de cada tipo (la misma que cobra el juego), para mostrarla en la pantalla de ajustes. null si la clave no existe.
    public static TimeSpan? WaitOf(string key) => key switch
    {
        "pet" => PetRules.FeedCooldown,
        "daily" => DailyRewardCalculator.MinInterval,
        _ => StoredCooldowns.FirstOrDefault(d => d.CommandName == key)?.Duration,
    };

    // "1 min", "30 min", "5 h", "24 h" (las esperas del juego son enteras en minutos u horas).
    public static string WaitLabel(TimeSpan wait) =>
        wait.TotalHours >= 1 ? $"{(int)wait.TotalHours} h" : $"{Math.Max(1, (int)wait.TotalMinutes)} min";

    // La comida de las mascotas: cuando la PRIMERA mascota que todavía puede subir de nivel termine su espera. Si alguna ya puede comer ahora, o no hay mascotas que puedan subir, no hay aviso.
    // cooldown: la espera de ESE jugador (PlayerBonuses.PetFeedCooldown).
    public static ReminderDue? PetDue(IEnumerable<OwnedPet> pets, TimeSpan cooldown, DateTime nowUtc)
    {
        DateTime? earliest = null;
        foreach (var pet in pets.Where(p => !PetRules.IsMaxLevel(p)))
        {
            if (pet.LastFedAtUtc is not { } fed)
            {
                return null; // una que nunca comió ya puede comer: no hay nada que esperar
            }

            var ready = DateTime.SpecifyKind(fed, DateTimeKind.Utc) + cooldown;
            if (ready <= nowUtc)
            {
                return null; // ya hay una lista
            }

            earliest = earliest is null || ready < earliest ? ready : earliest;
        }

        return earliest is { } at ? new ReminderDue("pet", at) : null;
    }

    // La recompensa diaria: 24 horas después de la última vez que se reclamó.
    public static ReminderDue? DailyDue(DateTime? lastClaimUtc, DateTime nowUtc)
    {
        if (lastClaimUtc is not { } last)
        {
            return null;
        }

        var ready = DateTime.SpecifyKind(last, DateTimeKind.Utc) + DailyRewardCalculator.MinInterval;
        return ready > nowUtc ? new ReminderDue("daily", ready) : null;
    }

    // El mensaje de uno o varios avisos que vencieron juntos, para un jugador. Una sola cosa: una línea. Varias: un encabezado y una línea por cosa.
    public static string Compose(ulong discordId, IReadOnlyList<string> kinds)
    {
        var found = kinds.Select(Find).Where(k => k is not null).Select(k => k!).Distinct().ToList();
        if (found.Count == 0)
        {
            return string.Empty;
        }

        if (found.Count == 1)
        {
            return $"<@{discordId}> {found[0].Emoji} {found[0].ReadyText}";
        }

        return $"<@{discordId}> ⏰ ¡Ya podés de nuevo!\n" + string.Join('\n', found.Select(k => $"{k.Emoji} **{k.Name}** — `{k.Command}`"));
    }

    // Si TODOS los avisos del mensaje son de esperas cortas, el mensaje se borra solo; si hay uno largo, queda (el jugador lo necesita aunque vuelva tarde).
    public static TimeSpan? AutoDeleteFor(IReadOnlyList<string> kinds)
    {
        var found = kinds.Select(Find).Where(k => k is not null).Select(k => k!).ToList();
        return found.Count > 0 && found.All(k => k.AutoDelete is not null) ? found.Max(k => k.AutoDelete) : null;
    }

    // Esperas cortas: el aviso se omite si el jugador estaba jugando justo antes (ver Services/ReminderScheduler.cs): no tiene sentido avisarle a quien ya está pegándole al botón.
    public static bool IsShort(string kind) => Find(kind)?.AutoDelete is not null;

    // Un aviso que llega MUY tarde (el bot estuvo apagado) ya no sirve: una espera corta pasados 5 minutos, una larga pasadas 6 horas. Se descarta en lugar de mandar una ráfaga vieja al volver.
    public static bool IsStale(string kind, DateTime dueUtc, DateTime nowUtc) =>
        nowUtc - dueUtc > (IsShort(kind) ? TimeSpan.FromMinutes(5) : TimeSpan.FromHours(6));

    // Las claves de comando que, al ejecutarse, pueden haber cambiado el aviso de mascotas o el del diario (para no leer eso en cada comando).
    public static bool MayChangePet(string commandHint) => ContainsAny(commandHint, "pet", "mascota");

    public static bool MayChangeDaily(string commandHint) => ContainsAny(commandHint, "daily", "diario", "racha");

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));
}

// La política de a quién se le mandan: hoy a todos (todavía no existe la membresía). Cuando exista, ESTE es el único lugar donde decidir quién recibe los avisos del bot.
public static class ReminderPolicy
{
    public static bool IsEligible(ulong discordId) => true;
}
