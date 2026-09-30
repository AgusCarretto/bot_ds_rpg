namespace BotDsRpg.GameData;

// Estado de la habilidad de UN jugador dentro de UN combate. default == lista para usar y sin efectos
// activos. EffectTurnsRemaining son los turnos que le quedan al efecto de la habilidad (Aguante /
// Sombra) — cuál es depende de la clase, por eso alcanza un solo contador.
public readonly record struct AbilityState(int CooldownRemaining, int EffectTurnsRemaining);

public enum PlayerAction { Attack, Ability }

public enum AbilityAvailability { Ready, OnCooldown, NoAbility }

// Lo mínimo del jugador que necesita el resolver (todos los datos ya vienen calculados por
// PlayerCombatProfileCalculator: daño y defensa totales, pasivas de clase).
public sealed record CombatantProfile(int Damage, int Defense, string PlayerClass, ClassPassiveProfile Passives);

// Todo lo que pasó en un turno del jugador. Si el monstruo cae en el golpe, MonsterHit es null (no
// hay contraataque) y PlayerHpAfter == PlayerHpAfterStrike.
public sealed record TurnResult(
    ClassAbility? AbilityUsed,
    bool Ambush, // el ataque salió desde las sombras del Ninja (crítico garantizado)
    int DamageDealt,
    int CritCount,
    int LifestealHeal,
    int MonsterHpAfter,
    int PlayerHpAfterStrike, // tras el Sifón de Almas, antes del contraataque
    MonsterHitOutcome? MonsterHit,
    int PlayerHpAfter,
    AbilityState StatusAfter)
{
    public bool MonsterDefeated => MonsterHpAfter <= 0;
    public bool AnyCritical => CritCount > 0;

    // Prefijo de una línea del log del combate, o vacío para un ataque común.
    public string ActionFlavor => (AbilityUsed?.Kind, Ambush) switch
    {
        (AbilityKind.Aguante, _) => "🛡️ ¡Aguante! ",
        (AbilityKind.BolaDeFuego, _) => "🔥 ¡Bola de Fuego! ",
        (AbilityKind.Sombra, _) => "🌑 ¡Sombra! Te desvanecés entre las sombras. ",
        (AbilityKind.LluviaDeFlechas, _) => "🏹 ¡Lluvia de Flechas! ",
        (null, true) => "🥷 ¡Emboscada desde las sombras! ",
        _ => string.Empty,
    };
}

public sealed record CounterResult(MonsterHitOutcome Hit, AbilityState Status);

// UNA sola implementación del turno de un jugador contra un monstruo (golpe → Sifón de Almas →
// contraataque), que comparten el combate solitario (Modules/AdventureModule.cs), el jefe
// cooperativo (Modules/RaidModule.cs) y el autohunt (Modules/AutoHuntModule.cs). Antes esa lógica
// estaba copiada en cada uno, y sumarle habilidades a las copias por separado era receta para que
// divergieran. Pura: no toca base ni Discord; compone GameData/CombatMath.cs sin modificarlo.
public static class CombatTurnResolver
{
    public static AbilityAvailability CheckAbility(string playerClass, AbilityState status)
    {
        if (ClassAbilities.For(playerClass) is null)
        {
            return AbilityAvailability.NoAbility;
        }

        return status.CooldownRemaining > 0 ? AbilityAvailability.OnCooldown : AbilityAvailability.Ready;
    }

    // El llamador TIENE que haber chequeado CheckAbility antes de pedir PlayerAction.Ability (un
    // click viejo sobre un botón ya en enfriamiento se rechaza en la capa de Discord, con un aviso
    // al jugador); acá un pedido inválido es un error de programación.
    public static TurnResult ResolveTurn(
        CombatantProfile player, AbilityState status, PlayerAction action,
        int playerHp, int playerMaxHp, int monsterHp, int monsterDamage)
    {
        ClassAbility? used = null;
        if (action == PlayerAction.Ability)
        {
            if (CheckAbility(player.PlayerClass, status) != AbilityAvailability.Ready)
            {
                throw new InvalidOperationException("La habilidad no está disponible: chequear CheckAbility antes de pedir la acción.");
            }

            used = ClassAbilities.For(player.PlayerClass);
        }

        int damage = 0;
        int crits = 0;
        bool ambush = false;
        double critBonus = player.Passives.CritChanceBonus;

        void Strike(double multiplier, double critChanceBonus)
        {
            var hit = CombatMath.ResolvePlayerHit(Scale(player.Damage, multiplier), critChanceBonus);
            damage += hit.Damage;
            if (hit.Critical)
            {
                crits++;
            }
        }

        switch (used?.Kind)
        {
            case AbilityKind.Aguante:
                Strike(AbilityTuning.AguanteStrikeMultiplier, critBonus);
                break;

            case AbilityKind.BolaDeFuego:
                Strike(AbilityTuning.BolaDeFuegoMultiplier, critBonus);
                break;

            case AbilityKind.LluviaDeFlechas:
                for (int arrow = 0; arrow < AbilityTuning.LluviaArrows; arrow++)
                {
                    Strike(AbilityTuning.LluviaArrowMultiplier, critBonus);
                }

                break;

            case AbilityKind.Sombra:
                break; // Desaparecer ocupa el turno: no ataca.

            default:
                if (IsStealthed(player.PlayerClass, status))
                {
                    // Bonus de crítico 1.0 => siempre crítico; el +25% se aplica encima del ×2.
                    var ambushHit = CombatMath.ResolvePlayerHit(player.Damage, 1.0);
                    damage = Scale(ambushHit.Damage, AbilityTuning.SombraAmbushMultiplier);
                    crits = 1;
                    ambush = true;
                }
                else
                {
                    Strike(1.0, critBonus);
                }

                break;
        }

        // Sifón de Almas (Hechicero): cura al pegar, golpe final incluido, antes del contraataque.
        int lifestealHeal = CombatMath.RollLifesteal(damage, player.Passives.LifestealChance, player.Passives.LifestealRatio);
        int playerHpAfterStrike = Math.Min(playerMaxHp, playerHp + lifestealHeal);
        int monsterHpAfter = Math.Max(0, monsterHp - damage);

        // Usar la habilidad arranca su enfriamiento y su efecto; cualquier otro turno lo hace bajar 1.
        var statusAfterStrike = used is not null
            ? new AbilityState(used.CooldownTurns, EffectTurnsFor(used.Kind))
            : status with { CooldownRemaining = Math.Max(0, status.CooldownRemaining - 1) };

        if (monsterHpAfter <= 0)
        {
            return new TurnResult(used, ambush, damage, crits, lifestealHeal, 0, playerHpAfterStrike, null, playerHpAfterStrike, statusAfterStrike);
        }

        var counter = ResolveCounter(player, statusAfterStrike, monsterDamage);
        int playerHpAfter = Math.Max(0, playerHpAfterStrike - counter.Hit.Damage);

        return new TurnResult(used, ambush, damage, crits, lifestealHeal, monsterHpAfter, playerHpAfterStrike, counter.Hit, playerHpAfter, counter.Status);
    }

    // Turno en el que el jugador NO ataca (usó un consumible, ver Modules/UseModule.cs): el monstruo
    // contraataca igual, con los efectos activos aplicados, y el turno cuenta para el enfriamiento.
    public static CounterResult ResolveCounterTurn(CombatantProfile player, AbilityState status, int monsterDamage) =>
        ResolveCounter(player, status with { CooldownRemaining = Math.Max(0, status.CooldownRemaining - 1) }, monsterDamage);

    // Contraataque del monstruo: aplica el efecto activo (esquive de Sombra / mitigación de Aguante)
    // y consume un turno de efecto. No toca el enfriamiento.
    private static CounterResult ResolveCounter(CombatantProfile player, AbilityState status, int monsterDamage)
    {
        double dodgeBonus = player.Passives.DodgeChanceBonus;
        double takenMultiplier = player.Passives.DamageTakenMultiplier;

        if (status.EffectTurnsRemaining > 0)
        {
            switch (ClassAbilities.For(player.PlayerClass)?.Kind)
            {
                case AbilityKind.Sombra:
                    dodgeBonus = Math.Max(dodgeBonus, AbilityTuning.SombraTotalDodgeChance - CombatMath.DodgeChance);
                    break;
                case AbilityKind.Aguante:
                    takenMultiplier *= AbilityTuning.AguanteDamageTakenMultiplier;
                    break;
            }
        }

        var hit = CombatMath.ResolveMonsterHit(monsterDamage, player.Defense, dodgeBonus, takenMultiplier);
        return new CounterResult(hit, status with { EffectTurnsRemaining = Math.Max(0, status.EffectTurnsRemaining - 1) });
    }

    private static bool IsStealthed(string playerClass, AbilityState status) =>
        ClassAbilities.For(playerClass)?.Kind == AbilityKind.Sombra && status.EffectTurnsRemaining > 0;

    private static int EffectTurnsFor(AbilityKind kind) => kind switch
    {
        AbilityKind.Aguante => AbilityTuning.AguanteEffectTurns,
        AbilityKind.Sombra => AbilityTuning.SombraEffectTurns,
        _ => 0,
    };

    private static int Scale(int value, double multiplier) => Math.Max(1, (int)Math.Round(value * multiplier));
}
