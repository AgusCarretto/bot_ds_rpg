namespace BotDsRpg.GameData;

// El jefe mundial del día (v0.24.0): cada día sale uno, igual para todos, y se pelea ENTRE TODOS los anotados a las 00:00 de Uruguay (como la Arena).
public sealed record WorldBossDef(string Key, string Name, string Emoji, string Intro);

// Un anotado en el momento de la pelea: su ataque de combate (ya con arma, sinergia y encantamiento) y la zona en que está, de donde sale con qué se lo compara.
public sealed record WorldBossFighter(ulong DiscordId, string Name, int Level, int ZoneRank, int ZoneMinLevel, int Attack);

// Cuánto le sacó cada uno al jefe: DamagePercent es el % de la vida del jefe (de 0 a 100 entre todos; pasarse de 100 lo mata).
public sealed record WorldBossHit(ulong DiscordId, string Name, int Level, int ZoneRank, double Strength, double Form, double DamagePercent);

// El resultado del día. Hits va de mayor a menor daño. Hunts es el premio de CADA anotado, en cacerías de oro de SU zona.
public sealed record WorldBossOutcome(IReadOnlyList<WorldBossHit> Hits, double TotalPercent, bool Defeated, int Hunts);

// Las reglas del jefe mundial (el dueño, 2026-10-22): «aa miniboss y que todos peleen contra él, o worldboss; mientras más gente, mejor; que anotarse gaste el cooldown del jefe; que dé entre 30 y 50 cacerías de esa zona».
//   · Anotarse (aa miniboss / /miniboss) gasta el cooldown del jefe y NO pide nivel: es lo que puede hacer quien todavía no llega al nivel 5 de /boss.
//   · A las 00:00 de Uruguay todos los anotados le pegan juntos. Cada uno aporta una fuerza que NO depende de su nivel o su zona en sí, sino de cuánto pega comparado con lo que se espera en SU zona (Strength): un nivel 1 sin arma
//     en la Zona 1 y un nivel 50 equipado en la Zona 5 aportan del mismo orden, y el que está mejor equipado para su zona, más. Una persona «normal» saca 1/10 de la vida del jefe (FullCrowd = 10), con una forma del día de ±30 %.
//   · El premio es el mismo para todos los anotados: de 30 cacerías de oro de su zona (casi nadie le pegó) hasta 50 (el jefe cayó), según qué parte de la vida le sacaron entre todos. O sea: mientras más gente, mejor para todos.
//     Con 1 jugador normal son ~32; con 5, ~40; con 10 el jefe cae la mitad de las veces y con 12 o más casi siempre (50).
// Pura: el azar sale del rng que se le pasa.
public static class WorldBossRules
{
    public const int MinHunts = 30;
    public const int MaxHunts = 50;

    // Cuántos jugadores «normales» hacen falta para voltear al jefe (cada uno saca 100/FullCrowd % de su vida).
    public const double FullCrowd = 10.0;

    // La forma del día de cada anotado: entre 1 − FormSwing y 1 + FormSwing del daño que le toca.
    public const double FormSwing = 0.30;

    // Cuánto puede valer la fuerza de uno: ni cero (se anotó y algo hace) ni un monstruo que lo resuelve solo.
    public const double MinStrength = 0.15;
    public const double MaxStrength = 3.0;

    // Lo que suma el arma de clase de cada zona (la escalera: Z1 +14, Z2 +30, Z3 +48, Z4 +75, Z5 +120; ver CLAUDE.md): con eso se arma «lo que se espera» de alguien equipado para su zona.
    // Son los 9/20/32/50/80 × 1,5 de antes de la v0.27.0 (cuando la sinergia se hizo parte del número del arma): el mismo resultado de siempre.
    private static readonly int[] WeaponLadder = [14, 30, 48, 75, 120];

    public static readonly IReadOnlyList<WorldBossDef> Bosses =
    [
        new("chancho", "El Chancho Colosal", "🐗", "Un chancho del tamaño de una casa arrasó las parrillas del pueblo y viene por las que quedan."),
        new("brasa", "La Brasa Madre", "🔥", "La primera brasa del mundo despertó con hambre y no hay asado que la llene."),
        new("toro", "El Toro de Hierro", "🐂", "Un toro de metal viejo carga contra todo lo que huela a humo."),
        new("ceniza", "El Gigante de Ceniza", "🌋", "Una montaña de ceniza se puso de pie y camina hacia el fogón."),
        new("dragon", "El Dragón de la Pampa", "🐉", "Algo muy antiguo salió de la tierra a reclamar su campo."),
        new("serpiente", "La Serpiente del Humo", "🐍", "Se enroscó alrededor de todas las chimeneas y no deja prender ni un fósforo."),
        new("parrillero", "El Parrillero Infernal", "👹", "Hace el asado más grande del mundo y los invitados son ustedes."),
    ];

    // El jefe del día: sale solo de la fecha (el mismo para todos y siempre el mismo para ese día).
    public static WorldBossDef BossOf(DateOnly day) => Bosses[day.DayNumber % Bosses.Count];

    // Lo que se espera que pegue alguien equipado para su zona: el ataque base de un nivel 3 por encima del mínimo de la zona más el arma de su clase.
    public static int ReferenceAttack(int zoneMinLevel, int zoneRank) =>
        CombatStats.BaseAttack(Math.Max(1, zoneMinLevel) + 3) + WeaponLadder[Math.Clamp(zoneRank, 1, WeaponLadder.Length) - 1];

    // Cuánto aporta alguien: su ataque dividido por lo que se espera en su zona (1,0 = lo esperado), acotado.
    public static double Strength(int attack, int zoneMinLevel, int zoneRank) =>
        Math.Clamp((double)Math.Max(0, attack) / ReferenceAttack(zoneMinLevel, zoneRank), MinStrength, MaxStrength);

    // El premio de cada anotado, en cacerías de oro de su zona: 50 si el jefe cayó; si no, de 30 a 50 según la parte de la vida que le sacaron.
    public static int HuntsFor(double totalPercent, bool defeated) =>
        defeated ? MaxHunts : Math.Clamp((int)Math.Round(MinHunts + ((MaxHunts - MinHunts) * Math.Clamp(totalPercent, 0, 100) / 100.0)), MinHunts, MaxHunts);

    public static WorldBossOutcome Resolve(IReadOnlyList<WorldBossFighter> fighters, Random rng)
    {
        var hits = fighters
            .Select(f =>
            {
                double strength = Strength(f.Attack, f.ZoneMinLevel, f.ZoneRank);
                double form = 1 + (((rng.NextDouble() * 2) - 1) * FormSwing);
                return new WorldBossHit(f.DiscordId, f.Name, f.Level, f.ZoneRank, strength, form, Math.Round(100.0 * strength * form / FullCrowd, 2));
            })
            .OrderByDescending(h => h.DamagePercent)
            .ThenBy(h => h.DiscordId)
            .ToList();

        double total = Math.Round(hits.Sum(h => h.DamagePercent), 2);
        bool defeated = total >= 100;
        return new WorldBossOutcome(hits, total, defeated, HuntsFor(total, defeated));
    }

    // Cómo se cuenta el golpe de uno de los que más le pegaron. La frase se elige con el rng que se pasa (en las pruebas, uno con semilla).
    public static string HitLine(WorldBossHit hit, WorldBossDef boss, Random rng)
    {
        string name = Short(hit.Name);
        string pct = hit.DamagePercent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        string[] templates = hit.DamagePercent >= 100.0 / FullCrowd * 1.4
            ?
            [
                $"💥 **{name}** (nivel {hit.Level}) le mete un golpazo a {boss.Name}: **{pct} %** de su vida.",
                $"🔥 **{name}** (nivel {hit.Level}) se juega entero y le arranca **{pct} %** a {boss.Name}.",
            ]
            : hit.DamagePercent >= 100.0 / FullCrowd * 0.7
                ?
                [
                    $"⚔️ **{name}** (nivel {hit.Level}) le pega bien: **{pct} %**.",
                    $"🗡️ **{name}** (nivel {hit.Level}) encuentra el hueco y saca **{pct} %**.",
                ]
                :
                [
                    $"🩹 **{name}** (nivel {hit.Level}) le hace lo que puede: **{pct} %**.",
                    $"🪵 **{name}** (nivel {hit.Level}) lo rasguña por **{pct} %**.",
                ];

        return templates[rng.Next(templates.Length)];
    }

    private static string Short(string text) => text.Length <= 20 ? text : text[..19] + "…";
}
