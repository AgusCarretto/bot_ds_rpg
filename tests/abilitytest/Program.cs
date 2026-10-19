using BotDsRpg.GameData;

// Arnés de verificación de GameData/CombatTurnResolver.cs + ClassAbilities: reglas exactas
// (cooldown, duración de efectos), multiplicadores estadísticos, equivalencia del ataque básico con
// la lógica vieja, e invariantes con fuzz. Referencia directa al .dll del bot.
int failures = 0;

void Check(string name, bool ok, string detail)
{
    Console.WriteLine($"{(ok ? "OK   " : "FALLA")} {name} — {detail}");
    if (!ok) failures++;
}

bool Near(double actual, double expected, double relTol) => Math.Abs(actual - expected) <= Math.Abs(expected) * relTol;

CombatantProfile P(string cls, int damage = 1000, int defense = 0) => new(damage, defense, cls, ClassPassives.For(cls));

const int Big = 1_000_000;
string[] classes = ["Guerrero", "Hechicero", "Ninja", "Arquero"];

// ---------- 1) Catálogo ----------
foreach (var cls in classes)
{
    var a = ClassAbilities.For(cls);
    Check($"catálogo {cls}", a is { CooldownTurns: 3 } && a.Name.Length > 0 && a.Description.Length > 0, a is null ? "sin habilidad" : $"{a.Emoji} {a.Name} cd={a.CooldownTurns}");
}

Check("clase desconocida sin habilidad", ClassAbilities.For("Bardo") is null && CombatTurnResolver.CheckAbility("Bardo", default) == AbilityAvailability.NoAbility, "Bardo");

// ---------- 2) Cooldown exacto: usar en el turno 1 => indisponible en los turnos 2,3,4 y lista en el 5 ----------
foreach (var cls in classes)
{
    var p = P(cls);
    var st = CombatTurnResolver.ResolveTurn(p, default, PlayerAction.Ability, Big, Big, Big, 10).StatusAfter;
    bool afterUse = st.CooldownRemaining == 3;
    var seen = new List<AbilityAvailability>();
    for (int turn = 2; turn <= 5; turn++)
    {
        seen.Add(CombatTurnResolver.CheckAbility(cls, st));
        st = CombatTurnResolver.ResolveTurn(p, st, PlayerAction.Attack, Big, Big, Big, 10).StatusAfter;
    }

    bool ok = afterUse && seen[0] == AbilityAvailability.OnCooldown && seen[1] == AbilityAvailability.OnCooldown
        && seen[2] == AbilityAvailability.OnCooldown && seen[3] == AbilityAvailability.Ready;
    Check($"cooldown {cls}", ok, $"tras usar cd={(afterUse ? 3 : -1)}; turnos 2..5: {string.Join(",", seen)}");
}

// Un turno de consumible (/use) también hace correr el enfriamiento.
{
    var c = CombatTurnResolver.ResolveCounterTurn(P("Hechicero"), new AbilityState(3, 0), 10);
    Check("consumible cuenta para el cooldown", c.Status.CooldownRemaining == 2, $"cd 3 -> {c.Status.CooldownRemaining}");
}

// Pedir la habilidad sin estar disponible es un error de programación: tiene que explotar, no degradar en silencio.
{
    bool threwCooldown = false, threwNoAbility = false;
    try { CombatTurnResolver.ResolveTurn(P("Ninja"), new AbilityState(2, 0), PlayerAction.Ability, Big, Big, Big, 10); } catch (InvalidOperationException) { threwCooldown = true; }
    try { CombatTurnResolver.ResolveTurn(P("Bardo"), default, PlayerAction.Ability, Big, Big, Big, 10); } catch (InvalidOperationException) { threwNoAbility = true; }
    Check("habilidad no disponible lanza", threwCooldown && threwNoAbility, $"en cooldown={threwCooldown}, sin habilidad={threwNoAbility}");
}

// ---------- 3) Aguante (Guerrero): -60% durante EXACTAMENTE 2 contraataques ----------
{
    int n = 20000;
    bool exact = true, strikeInRange = true;
    double strikeSum = 0;
    int minStrike = int.MaxValue, maxStrike = 0;
    int hits36 = 0, hits90 = 0;
    for (int i = 0; i < n; i++)
    {
        var p = P("Guerrero", 1000, 0);
        var a = CombatTurnResolver.ResolveTurn(p, default, PlayerAction.Ability, Big, Big, Big, 100);
        var b = CombatTurnResolver.ResolveTurn(p, a.StatusAfter, PlayerAction.Attack, Big, Big, Big, 100);
        var c = CombatTurnResolver.ResolveTurn(p, b.StatusAfter, PlayerAction.Attack, Big, Big, Big, 100);

        // monsterDamage 100, defensa 0: mitigado 100 -> x0.9 (pasiva) x0.4 (Aguante) = 36 ; sin Aguante = 90
        if (a.MonsterHit!.Damage is not (0 or 36)) exact = false;
        if (b.MonsterHit!.Damage is not (0 or 36)) exact = false;
        if (c.MonsterHit!.Damage is not (0 or 90)) exact = false;
        if (a.StatusAfter.EffectTurnsRemaining != 1 || b.StatusAfter.EffectTurnsRemaining != 0 || c.StatusAfter.EffectTurnsRemaining != 0) exact = false;
        if (a.MonsterHit.Damage == 36) hits36++;
        if (c.MonsterHit.Damage == 90) hits90++;

        strikeSum += a.DamageDealt;
        minStrike = Math.Min(minStrike, a.DamageDealt);
        maxStrike = Math.Max(maxStrike, a.DamageDealt);
    }

    strikeInRange = minStrike >= 560 && maxStrike <= 1680;
    double meanStrike = strikeSum / n;
    Check("Aguante: mitigación en exactamente 2 contraataques", exact && hits36 > n * 0.9 && hits90 > n * 0.9, $"daño recibido solo 36 (con Aguante) y luego 90; efecto 2->1->0");
    Check("Aguante: el golpe es al 70%", strikeInRange && Near(meanStrike, 700 * 1.1, 0.03), $"rango {minStrike}..{maxStrike}, media {meanStrike:F0} (esperado ~{700 * 1.1:F0})");
}

// ---------- 4) Bola de Fuego (Hechicero): 220% ----------
{
    int n = 60000;
    double fire = 0, basic = 0;
    for (int i = 0; i < n; i++)
    {
        var p = P("Hechicero");
        fire += CombatTurnResolver.ResolveTurn(p, default, PlayerAction.Ability, Big, Big, Big, 10).DamageDealt;
        basic += CombatTurnResolver.ResolveTurn(p, default, PlayerAction.Attack, Big, Big, Big, 10).DamageDealt;
    }

    Check("Bola de Fuego: 220% del ataque básico", Near(fire / basic, 2.2, 0.03), $"cociente de medias {fire / basic:F3} (esperado 2.200)");
}

// ---------- 5) Lluvia de Flechas (Arquero): 3 flechas al 60%, crítico individual ----------
{
    int n = 60000;
    double rain = 0, basic = 0, rainCrits = 0;
    for (int i = 0; i < n; i++)
    {
        var p = P("Arquero");
        var r = CombatTurnResolver.ResolveTurn(p, default, PlayerAction.Ability, Big, Big, Big, 10);
        rain += r.DamageDealt;
        rainCrits += r.CritCount;
        basic += CombatTurnResolver.ResolveTurn(p, default, PlayerAction.Attack, Big, Big, Big, 10).DamageDealt;
    }

    Check("Lluvia de Flechas: 3 x 60% => 180% del ataque básico", Near(rain / basic, 1.8, 0.03), $"cociente {rain / basic:F3} (esperado 1.800)");
    Check("Lluvia de Flechas: crítico por flecha (25% c/u)", Near(rainCrits / n, 0.75, 0.05), $"críticos por uso {rainCrits / n:F3} (esperado 0.750)");
}

// ---------- 6) Sombra (Ninja): no ataca; 75% de esquive x2 contraataques; UNA emboscada ----------
{
    int n = 60000;
    int d1 = 0, d2 = 0, d3 = 0, ambushes = 0, wrongAmbush = 0, useDamageNonZero = 0, notCrit = 0;
    double ambushSum = 0;
    int ambushMin = int.MaxValue, ambushMax = 0;
    bool effectSeq = true;
    for (int i = 0; i < n; i++)
    {
        var p = P("Ninja", 1000, 0);
        var r1 = CombatTurnResolver.ResolveTurn(p, default, PlayerAction.Ability, Big, Big, Big, 10);
        var r2 = CombatTurnResolver.ResolveTurn(p, r1.StatusAfter, PlayerAction.Attack, Big, Big, Big, 10);
        var r3 = CombatTurnResolver.ResolveTurn(p, r2.StatusAfter, PlayerAction.Attack, Big, Big, Big, 10);

        if (r1.DamageDealt != 0) useDamageNonZero++;
        if (r1.StatusAfter.EffectTurnsRemaining != 1 || r2.StatusAfter.EffectTurnsRemaining != 0) effectSeq = false;
        if (r1.MonsterHit!.Dodged) d1++;
        if (r2.MonsterHit!.Dodged) d2++;
        if (r3.MonsterHit!.Dodged) d3++;
        if (r2.Ambush) { ambushes++; ambushSum += r2.DamageDealt; ambushMin = Math.Min(ambushMin, r2.DamageDealt); ambushMax = Math.Max(ambushMax, r2.DamageDealt); if (r2.CritCount != 1) notCrit++; }
        if (r3.Ambush || r1.Ambush) wrongAmbush++;
    }

    Check("Sombra: desaparecer no ataca", useDamageNonZero == 0, $"turnos con daño al usarla: {useDamageNonZero}");
    Check("Sombra: exactamente una emboscada por uso", ambushes == n && wrongAmbush == 0 && effectSeq, $"emboscadas {ambushes}/{n}, fuera de lugar {wrongAmbush}, efecto 2->1->0 {effectSeq}");
    Check("Sombra: la emboscada es crítico garantizado x2.5", notCrit == 0 && ambushMin >= 2000 && ambushMax <= 3000 && Near(ambushSum / n, 2500, 0.02), $"rango {ambushMin}..{ambushMax}, media {ambushSum / n:F0} (esperado ~2500)");
    Check("Sombra: 75% de esquive en 2 contraataques y luego 20%", Near(d1 / (double)n, 0.75, 0.02) && Near(d2 / (double)n, 0.75, 0.02) && Near(d3 / (double)n, 0.20, 0.05),
        $"esquive {d1 / (double)n:P1} / {d2 / (double)n:P1} / {d3 / (double)n:P1} (esperado 75% / 75% / 20%)");
}

// ---------- 7) El ATAQUE BÁSICO del resolver == la lógica vieja (composición directa de CombatMath) ----------
foreach (var cls in classes)
{
    int n = 300000;
    var passives = ClassPassives.For(cls);
    double nDmg = 0, oDmg = 0, nTaken = 0, oTaken = 0;
    int nCrit = 0, oCrit = 0, nHeal = 0, oHeal = 0, nDodge = 0, oDodge = 0;
    var p = P(cls, 1000, 20);
    for (int i = 0; i < n; i++)
    {
        var t = CombatTurnResolver.ResolveTurn(p, default, PlayerAction.Attack, 5000, 5000, Big, 100);
        nDmg += t.DamageDealt;
        if (t.CritCount > 0) nCrit++;
        if (t.LifestealHeal > 0) nHeal++;
        if (t.MonsterHit!.Dodged) nDodge++;
        nTaken += t.MonsterHit.Damage;

        var hit = CombatMath.ResolvePlayerHit(1000, passives.CritChanceBonus);
        int heal = CombatMath.RollLifesteal(hit.Damage, passives.LifestealChance, passives.LifestealRatio);
        var mh = CombatMath.ResolveMonsterHit(100, 20, passives.DodgeChanceBonus, passives.DamageTakenMultiplier);
        oDmg += hit.Damage;
        if (hit.Critical) oCrit++;
        if (heal > 0) oHeal++;
        if (mh.Dodged) oDodge++;
        oTaken += mh.Damage;
    }

    bool ok = Near(nDmg / n, oDmg / n, 0.01)
        && Math.Abs(nCrit - oCrit) / (double)n < 0.006
        && Math.Abs(nHeal - oHeal) / (double)n < 0.006
        && Math.Abs(nDodge - oDodge) / (double)n < 0.006
        && Near(nTaken / n, oTaken / n, 0.02);
    Check($"ataque básico ≡ lógica vieja ({cls})", ok,
        $"daño {nDmg / n:F1} vs {oDmg / n:F1}; crit {nCrit / (double)n:P1} vs {oCrit / (double)n:P1}; sifón {nHeal / (double)n:P1} vs {oHeal / (double)n:P1}; esquive {nDodge / (double)n:P1} vs {oDodge / (double)n:P1}; recibido {nTaken / n:F1} vs {oTaken / n:F1}");
}

// ---------- 8) Fuzz de invariantes ----------
{
    var rnd = new Random(12345);
    string[] pool = ["Guerrero", "Hechicero", "Ninja", "Arquero", "Bardo"];
    int turns = 0, bad = 0;
    for (int fight = 0; fight < 4000; fight++)
    {
        string cls = pool[rnd.Next(pool.Length)];
        var p = P(cls, rnd.Next(5, 400), rnd.Next(0, 60));
        int playerMax = rnd.Next(20, 500), playerHp = playerMax, monsterHp = rnd.Next(10, 800), monsterDamage = rnd.Next(1, 120);
        var st = default(AbilityState);

        while (playerHp > 0 && monsterHp > 0)
        {
            bool useAbility = CombatTurnResolver.CheckAbility(cls, st) == AbilityAvailability.Ready && rnd.NextDouble() < 0.5;
            var r = CombatTurnResolver.ResolveTurn(p, st, useAbility ? PlayerAction.Ability : PlayerAction.Attack, playerHp, playerMax, monsterHp, monsterDamage);
            turns++;

            bool ok = r.DamageDealt >= 0
                && r.MonsterHpAfter >= 0 && r.MonsterHpAfter <= monsterHp
                && r.PlayerHpAfter >= 0 && r.PlayerHpAfter <= playerMax
                && r.PlayerHpAfterStrike <= playerMax
                && r.StatusAfter.CooldownRemaining is >= 0 and <= 3
                && r.StatusAfter.EffectTurnsRemaining is >= 0 and <= 2
                && (r.MonsterDefeated ? r.MonsterHit is null && r.PlayerHpAfter == r.PlayerHpAfterStrike : r.MonsterHit is not null);
            if (!ok) bad++;

            st = r.StatusAfter;
            playerHp = r.PlayerHpAfter;
            monsterHp = r.MonsterHpAfter;
        }
    }

    Check("fuzz de invariantes", bad == 0, $"{turns} turnos simulados, {bad} violaciones");
}

// ---------- 9) Impacto en el balance (informativo): daño por turno con la habilidad vs solo ataque ----------
Console.WriteLine();
Console.WriteLine("Balance (política: usar la habilidad apenas está lista, vs. solo ataque básico; 400000 turnos):");
foreach (var cls in classes)
{
    var p = P(cls, 1000, 0);
    double dmgAbility = 0, dmgBasic = 0, takenAbility = 0, takenBasic = 0;
    var st = default(AbilityState);
    int turns = 400000;
    for (int i = 0; i < turns; i++)
    {
        bool use = CombatTurnResolver.CheckAbility(cls, st) == AbilityAvailability.Ready;
        var r = CombatTurnResolver.ResolveTurn(p, st, use ? PlayerAction.Ability : PlayerAction.Attack, Big, Big, Big, 100);
        st = r.StatusAfter;
        dmgAbility += r.DamageDealt;
        takenAbility += r.MonsterHit!.Damage;

        var b = CombatTurnResolver.ResolveTurn(p, default, PlayerAction.Attack, Big, Big, Big, 100);
        dmgBasic += b.DamageDealt;
        takenBasic += b.MonsterHit!.Damage;
    }

    Console.WriteLine($"  {cls,-9} daño hecho x{dmgAbility / dmgBasic:F2}   daño recibido x{takenAbility / takenBasic:F2}");
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "RESULTADO: OK" : $"RESULTADO: FALLA ({failures})");
