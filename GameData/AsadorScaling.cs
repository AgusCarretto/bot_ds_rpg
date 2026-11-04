namespace BotDsRpg.GameData;

// El Asador Eterno desde las zonas 6 a 10 (v0.47.0). El dueño: «hoy el asador con las armas necesarias se deletea rápido, y ni hablar con la comida; cada zona nueva el asador sube stats, se hace más
// fuerte y además te pone a prueba con una mazmorra»; «desde la zona 6 el boss puede tener una habilidad, un 20 % de tirarla al azar, sin cooldown».
//   · Del FN 0 al 4 se lo pelea como siempre (la fila de monsters, zona 0). Desde que la zona tope es de mazmorra, sus números salen de acá según ESA zona (por posición 6..10),
//     calibrados con tools/balance2 parte N: con el equipo de la zona tope, nivel mínimo + 3, llegando con el 80 % de la vida de la mazmorra, sin comida y usando la habilidad, ~30 % de derrota.
//   · La llamarada: cada turno, un 20 % de que su golpe pegue ×1,75 (sin cooldown). Se resuelve dándole al turno un golpe más grande: el resto (defensa, esquive) es la cuenta de siempre.
public static class AsadorScaling
{
    public const int FlareChancePercent = 20;
    public const double FlareMultiplier = 1.75;

    // Vida y daño del Asador según la zona tope (posición 6..10 en la escalera). Valores de la parte N de tools/balance2 (ver arriba).
    private static readonly (int MinHp, int MaxHp, int MinDamage, int MaxDamage)[] ByTopRank =
    [
        (2286, 2794, 223, 273),   // 6  (vida ~10 golpes con el equipo de la zona; derrota 32 % llegando al 80 %, 15 % al 100 %, 51 % al 60 %)
        (3186, 3894, 275, 336),   // 7  (32 % / 17 % / 49 %)
        (4446, 5434, 337, 411),   // 8  (31 % / 18 % / 47 %)
        (6336, 7744, 410, 502),   // 9  (29 % / 17 % / 42 %)
        (9126, 11154, 510, 624),  // 10 (29 % / 19 % / 43 %)
    ];

    public const int FirstRank = 6;

    // Multiplicador de la VIDA sobre la tabla calibrada de arriba (el daño no se toca). Un solo número para retocar.
    // v0.48.1 (el dueño, jugando en FN 7: «ni armame hice de esa zona y lo liquidé»): la tabla se calibró para un jugador en el nivel mínimo + 3, pero quien llega con muchos niveles de más
    // lo pasa casi sin riesgo con el equipo de la zona anterior (zona tope 6, equipo de la 5, nivel 60: 2 % de derrota llegando al 100 %). Medido con tools/balance2 -- N hp (4 clases, la habilidad, sin comida):
    //   con la vida ×1,5, en la zona 6 el equipo de la zona anterior pierde 72 % (nivel mínimo + 20) y 36 % (mínimo + 30) llegando al 80 % (era 32 % y 10 %), y el equipo propio 11 % y 2 % (era 2 % y 0 %);
    //   con ×2 el equipo propio a mínimo + 20 ya pierde 29–44 % según la zona, demasiado para una mazmorra que cuesta una Reliquia. Quien llega justo al nivel mínimo + 3 con su equipo pasa de ~30 % a ~52–64 %.
    public const double HpMultiplier = 1.5;

    // Los números calibrados de la tabla, sin el multiplicador (los usa tools/balance2 para medir el efecto de cada valor). Null fuera de 6..10.
    public static (int MinHp, int MaxHp, int MinDamage, int MaxDamage)? BaseStatsFor(int topRank) =>
        topRank >= FirstRank && topRank < FirstRank + ByTopRank.Length ? ByTopRank[topRank - FirstRank] : null;

    // Lo que se pelea de verdad: la tabla con la vida multiplicada. Null fuera de 6..10 (el Asador de siempre).
    public static (int MinHp, int MaxHp, int MinDamage, int MaxDamage)? StatsFor(int topRank) =>
        BaseStatsFor(topRank) is { } b ? (ScaleHp(b.MinHp), ScaleHp(b.MaxHp), b.MinDamage, b.MaxDamage) : null;

    private static int ScaleHp(int hp) => (int)Math.Round(hp * HpMultiplier, MidpointRounding.AwayFromZero);

    public static bool RollsFlare(Random rng) => rng.Next(100) < FlareChancePercent;

    public static int FlareDamage(int damage) => (int)Math.Round(damage * FlareMultiplier, MidpointRounding.AwayFromZero);
}
