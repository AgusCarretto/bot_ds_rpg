namespace BotDsRpg.GameData;

// Un jugador listo para pelear contra otro: sus números ya calculados (PlayerCombatProfileCalculator) y su vida COMPLETA de combate.
// En PvP nadie arranca herido: el duelo no toca la base, así que la vida real del jugador no importa (ni se ve afectada).
public sealed record DuelFighter(ulong DiscordId, string Name, int Level, string PlayerClass, CombatantProfile Profile, int MaxHp);

// Un lado del duelo en un momento dado, con los acumulados para el resumen final.
public sealed record DuelSide(DuelFighter Fighter, int Hp, AbilityState Status, int DamageDealt = 0, int Crits = 0, int Dodges = 0)
{
    public bool Defeated => Hp <= 0;
    public double HpFraction => Fighter.MaxHp <= 0 ? 0 : (double)Math.Max(0, Hp) / Fighter.MaxHp;
}

// El duelo entero: A (quien desafió, o el que quedó primero en la llave) y B, de quién es el turno y cuántas acciones van.
public sealed record DuelState(DuelSide A, DuelSide B, bool ATurn, int Actions)
{
    public DuelSide Actor => ATurn ? A : B;
    public DuelSide Target => ATurn ? B : A;
    public bool Finished => A.Defeated || B.Defeated;
    public DuelSide? Winner => !Finished ? null : A.Defeated ? B : A;
}

// Lo que pasó en UNA acción, para contarlo. Damage es lo que efectivamente le sacó al otro (ya con defensa, esquive y mitigación).
public sealed record DuelEvent(
    bool ActorIsA, string ActorName, string TargetName, ClassAbility? AbilityUsed, bool Ambush,
    bool Attacked, bool Dodged, int Damage, int Crits, int Heal);

public sealed record DuelStep(DuelState State, DuelEvent Event);

// Cómo terminó un duelo simulado de principio a fin (la arena). Capped: se acabó el tiempo de combate (MaxActions) y se decidió por vida.
public sealed record DuelResult(DuelSide Winner, DuelSide Loser, int Actions, bool Capped);

// El PvP, sin Discord ni base. Reusa EXACTAMENTE las mismas piezas del combate contra monstruos (CombatTurnResolver.ResolveStrike para
// "yo pego" y ResolveIncoming para "me pegan"), así que las habilidades, las pasivas de clase y el crítico se comportan igual que en /hunt.
//
// Reglas propias del PvP (todas acá):
//  - Los turnos se alternan: una acción por vez, la del que tiene el turno.
//  - La DEFENSA del que recibe se resta del ataque ANTES de las habilidades y el crítico (ataque efectivo = ataque − defensa, mínimo 1).
//    Restarla al final dejaba a las habilidades de ráfaga (Bola de Fuego x2.2) pegando 4 veces más que un golpe común contra un rival con
//    defensa, cuando contra monstruos rinden ~1.3 veces; así cada habilidad mantiene el efecto con el que se calibró.
//  - El esquive y la mitigación (Aguante, pasiva del Guerrero) son los de siempre y se aplican al golpe ya resuelto.
//  - El Sifón de Almas cura sobre el daño que de verdad entró (un golpe esquivado no cura).
//  - Un efecto de habilidad (Aguante / Sombra) dura "golpes recibidos", como contra un monstruo: si el rival no pega, no se gasta.
public static class DuelEngine
{
    // Tope de acciones (de los dos) de una pelea: un duelo entre dos tanques con defensa enorme podría no terminar nunca (el daño mínimo
    // es 1). Al llegar acá gana quien tiene más vida (en proporción).
    public const int MaxActions = 100;

    public static DuelState Start(DuelFighter a, DuelFighter b, bool aStarts) =>
        new(new DuelSide(a, a.MaxHp, default), new DuelSide(b, b.MaxHp, default), aStarts, 0);

    public static AbilityAvailability CheckAbility(DuelState state) =>
        CombatTurnResolver.CheckAbility(state.Actor.Fighter.PlayerClass, state.Actor.Status);

    // El llamador chequea CheckAbility antes de pedir PlayerAction.Ability (igual que en el resto del combate).
    public static DuelStep Step(DuelState state, PlayerAction action)
    {
        if (state.Finished)
        {
            throw new InvalidOperationException("El duelo ya terminó.");
        }

        var actor = state.Actor;
        var target = state.Target;
        var attackerProfile = actor.Fighter.Profile;
        var defenderProfile = target.Fighter.Profile;

        // Ataque efectivo contra ESTE rival, y sin el Sifón (se resuelve más abajo sobre el daño real).
        var effective = attackerProfile with
        {
            Damage = Math.Max(1, attackerProfile.Damage - defenderProfile.Defense),
            Passives = attackerProfile.Passives with { LifestealChance = 0, LifestealRatio = 0 },
        };

        var strike = CombatTurnResolver.ResolveStrike(effective, actor.Status, action, actor.Hp, actor.Fighter.MaxHp);

        bool attacked = strike.Damage > 0; // Sombra no ataca
        bool dodged = false;
        int damage = 0;
        var targetStatus = target.Status;

        if (attacked)
        {
            // La defensa ya se restó arriba: el que recibe entra con Defense = 0 para no restarla dos veces.
            var incoming = CombatTurnResolver.ResolveIncoming(defenderProfile with { Defense = 0 }, target.Status, strike.Damage);
            dodged = incoming.Hit.Dodged;
            damage = incoming.Hit.Damage;
            targetStatus = incoming.Status;
        }

        int heal = !dodged && damage > 0
            ? CombatMath.RollLifesteal(damage, attackerProfile.Passives.LifestealChance, attackerProfile.Passives.LifestealRatio)
            : 0;
        int crits = dodged ? 0 : strike.CritCount;

        var newActor = actor with
        {
            Hp = Math.Min(actor.Fighter.MaxHp, actor.Hp + heal),
            Status = strike.StatusAfter,
            DamageDealt = actor.DamageDealt + damage,
            Crits = actor.Crits + crits,
        };
        var newTarget = target with
        {
            Hp = Math.Max(0, target.Hp - damage),
            Status = targetStatus,
            Dodges = target.Dodges + (dodged ? 1 : 0),
        };

        var next = state.ATurn
            ? new DuelState(newActor, newTarget, ATurn: false, state.Actions + 1)
            : new DuelState(newTarget, newActor, ATurn: true, state.Actions + 1);

        var ev = new DuelEvent(
            state.ATurn, actor.Fighter.Name, target.Fighter.Name, strike.AbilityUsed, strike.Ambush, attacked, dodged, damage, crits, heal);

        return new DuelStep(next, ev);
    }

    // La política de las peleas que nadie maneja (la arena): usar la habilidad apenas está lista. Es la misma con la que se midió el efecto
    // de cada habilidad (ver AbilityTuning), así que ninguna clase queda mal parada por una política tonta.
    public static PlayerAction ChooseAction(DuelState state) =>
        CheckAbility(state) == AbilityAvailability.Ready ? PlayerAction.Ability : PlayerAction.Attack;

    // Pelea completa sin intervención. Empieza el que sale en la moneda.
    public static DuelResult Simulate(DuelFighter a, DuelFighter b, Random? rng = null)
    {
        rng ??= Random.Shared;
        var state = Start(a, b, aStarts: rng.Next(2) == 0);

        while (!state.Finished && state.Actions < MaxActions)
        {
            state = Step(state, ChooseAction(state)).State;
        }

        return Conclude(state);
    }

    // Quién ganó. Si el duelo terminó, el que quedó en pie; si se acabó el tiempo, el que tiene más vida en proporción, después el de más
    // nivel y, si siguen parejos, A (en la arena A sale del sorteo, así que no favorece a nadie).
    public static DuelResult Conclude(DuelState state)
    {
        if (state.Winner is { } winner)
        {
            return new DuelResult(winner, ReferenceEquals(winner, state.A) ? state.B : state.A, state.Actions, Capped: false);
        }

        bool aWins = state.A.HpFraction > state.B.HpFraction
            || (state.A.HpFraction == state.B.HpFraction && state.A.Fighter.Level >= state.B.Fighter.Level);

        return aWins
            ? new DuelResult(state.A, state.B, state.Actions, Capped: true)
            : new DuelResult(state.B, state.A, state.Actions, Capped: true);
    }
}

// Cómo se cuenta cada acción en el chat (puro, para probarlo).
public static class DuelNarrator
{
    public static string Describe(DuelEvent e)
    {
        string a = $"**{e.ActorName}**";
        string b = $"**{e.TargetName}**";
        string heal = e.Heal > 0 ? $" 🩸 _(se curó {e.Heal})_" : string.Empty;
        string crit = e.Crits switch { 0 => string.Empty, 1 => " 💥 _crítico_", _ => $" 💥 _{e.Crits} críticos_" };

        // Sombra: no pega, se esconde.
        if (e.AbilityUsed?.Kind == AbilityKind.Sombra)
        {
            return $"🌑 {a} se desvanece en las **sombras**: {Pct(AbilityTuning.SombraTotalDodgeChance)} de esquive y su próximo golpe es una emboscada.";
        }

        if (e.Dodged)
        {
            string opener = e.AbilityUsed is { } used ? $"{used.Emoji} {a} usa **{used.Name}**, pero " : string.Empty;
            return $"{opener}💨 {b} esquivó el ataque{(opener.Length > 0 ? string.Empty : $" de {a}")}.";
        }

        if (e.Ambush)
        {
            return $"🥷 ¡Emboscada! {a} sale de las sombras y le pega a {b} por **{e.Damage}**.{crit}{heal}";
        }

        return e.AbilityUsed?.Kind switch
        {
            AbilityKind.Aguante => $"🛡️ {a} se planta con **Aguante** y le pega a {b} por **{e.Damage}** (recibe menos daño por {AbilityTuning.AguanteEffectTurns} golpes).{crit}{heal}",
            AbilityKind.BolaDeFuego => $"🔥 {a} lanza una **Bola de Fuego** y le pega a {b} por **{e.Damage}**.{crit}{heal}",
            AbilityKind.LluviaDeFlechas => $"🏹 {a} descarga una **Lluvia de Flechas** sobre {b}: **{e.Damage}** de daño.{crit}{heal}",
            _ => e.Crits > 0
                ? $"💥 ¡Crítico! {a} le pega a {b} por **{e.Damage}**.{heal}"
                : $"⚔️ {a} le pega a {b} por **{e.Damage}**.{heal}",
        };
    }

    private static string Pct(double value) => $"{(int)Math.Round(value * 100)}%";
}
