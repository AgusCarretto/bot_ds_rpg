namespace BotDsRpg.GameData;

// Las dos peleas de una pareja ya preparadas con el azar de la Arena: los luchadores que de verdad se enfrentan, la forma del día de cada uno y si hubo milagro.
public sealed record ArenaSetup(DuelFighter A, DuelFighter B, double FormA, double FormB, bool Miracle);

// El azar de la Arena (v0.23.2). El dueño: «la Arena la gana siempre el de más nivel… que pueda ganar alguien de nivel 1 contra el del 20; si no, llega un punto en que se hace imposible».
// Medido con el mismo motor (DuelEngine.Simulate, misma clase y mismo equipo): sin azar, con +1 nivel el de arriba gana 65 a 90 % de las peleas y con +5 niveles 89 a 100 %. Y un nivel 1 SIN equipo contra un
// nivel 20 con equipo de zona 2 no gana NUNCA aunque se le sume mucha suerte: la defensa se resta de cada golpe y le deja el daño mínimo. Por eso no alcanza con unos puntos de azar: son tres capas, cada una medida.
//   1) COMPRESIÓN DE PODER (PowerCompression = 0,5): en cada pareja, el daño, la defensa y la vida de los dos se acercan a la media geométrica de ambos (valor' = media × (valor/media)^0,5). La diferencia grande se achica
//      a su raíz cuadrada, pero el de más poder sigue siendo el de más poder.
//   2) FORMA DEL DÍA (FormSwing = ±30 %): cada luchador sortea, para ESA pelea, un factor entre 0,7 y 1,3 que multiplica su daño y su vida.
//   3) MILAGRO (MiracleChance = 30 %): en 3 de cada 10 peleas los astros se alinean y la pelea se juega con el poder IGUALADO (compresión total): ahí cualquiera le puede ganar a cualquiera.
// Resultado medido (4 clases, 2000 peleas por celda; el de MÁS nivel gana): +1 nivel ~54 %, +5 ~63 %, +10 ~70 %, +19 ~79 %, y nivel 1 sin equipo contra nivel 20 con equipo de zona 2 o de zona 4: ~85 % (o sea que el de nivel 1
// gana ~15 de cada 100). Sigue valiendo subir de nivel y de equipo, pero ya no hay una pelea decidida de antemano. Si se quiere más o menos azar, las tres constantes de abajo son el único lugar; hay que volver a medirlo
// (el arnés es un doble bucle sobre Prepare + DuelEngine). SOLO la Arena usa esto: el duelo amistoso (/fight) sigue con el poder real de cada uno.
public static class ArenaTuning
{
    public const double PowerCompression = 0.5;
    public const double FormSwing = 0.30;
    public const double MiracleChance = 0.30;

    // Prepara una pareja con el azar de la Arena. rng decide el milagro y las dos formas. Las clases, las pasivas y los ids no cambian: solo el daño, la defensa y la vida.
    public static ArenaSetup Prepare(
        DuelFighter a, DuelFighter b, Random rng, double compression = PowerCompression, double swing = FormSwing, double miracleChance = MiracleChance)
    {
        bool miracle = rng.NextDouble() < miracleChance;
        double k = miracle ? 0.0 : compression;
        double formA = 1 + (((rng.NextDouble() * 2) - 1) * swing);
        double formB = 1 + (((rng.NextDouble() * 2) - 1) * swing);

        var fighterA = Rebuild(a, Compress(a.Profile.Damage, b.Profile.Damage, k) * formA, Compress(a.Profile.Defense + 1, b.Profile.Defense + 1, k) - 1, Compress(a.MaxHp, b.MaxHp, k) * formA);
        var fighterB = Rebuild(b, Compress(b.Profile.Damage, a.Profile.Damage, k) * formB, Compress(b.Profile.Defense + 1, a.Profile.Defense + 1, k) - 1, Compress(b.MaxHp, a.MaxHp, k) * formB);
        return new ArenaSetup(fighterA, fighterB, formA, formB, miracle);
    }

    // value acercado a la media geométrica de (value, other): media × (value / media)^k. Con k = 1 no cambia nada y con k = 0 quedan los dos en la media. Un valor que no es positivo se deja como está.
    public static double Compress(double value, double other, double k)
    {
        if (value <= 0 || other <= 0)
        {
            return value;
        }

        double mean = Math.Sqrt(value * other);
        return mean * Math.Pow(value / mean, k);
    }

    private static DuelFighter Rebuild(DuelFighter fighter, double damage, double defense, double hp) =>
        fighter with
        {
            Profile = fighter.Profile with { Damage = Math.Max(1, (int)Math.Round(damage)), Defense = Math.Max(0, (int)Math.Round(defense)) },
            MaxHp = Math.Max(1, (int)Math.Round(hp)),
        };
}
