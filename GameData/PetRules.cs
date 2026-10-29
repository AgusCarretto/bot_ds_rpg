using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Lo que cada mascota puede dar (pet_species.bonus_kind). Texto, como en la base.
public static class PetBonusKind
{
    public const string Gold = "gold";       // % más de oro en lo que pagan /hunt, /travel, /boss, /raid y /autohunt
    public const string Xp = "xp";           // % más de EXP en esas mismas peleas
    public const string Defense = "defense"; // % más de la defensa total, en peleas contra monstruos (no en duelos ni en la Arena)
    public const string Drop = "drop";       // % MÁS chances de drop de los monstruos de cacería y viaje (relativo: 6 % sobre un 6 % da 6,36 %); no los cofres de jefe
    public const string Gather = "gather";   // % más unidades en cada /chop y /mine (v0.14.0, la mascota del Fogón Eterno: se queda en los Fuegos Nuevos, donde la recolección se vuelve a hacer)
}

// La suma de todas las mascotas que tiene un jugador: valen TODAS a la vez (pedido del dueño). Porcentajes (5,5 = 5,5 %).
public sealed record PetBonuses(double GoldPercent, double XpPercent, double DefensePercent, double DropPercent, double GatherPercent = 0)
{
    public static readonly PetBonuses None = new(0, 0, 0, 0);

    public bool IsNone => GoldPercent == 0 && XpPercent == 0 && DefensePercent == 0 && DropPercent == 0 && GatherPercent == 0;

    // Todos los bonus multiplicados por un factor (la bendición Manada: ×1,1 por nivel).
    public PetBonuses Scaled(double factor) =>
        factor == 1.0 ? this : new PetBonuses(GoldPercent * factor, XpPercent * factor, DefensePercent * factor, DropPercent * factor, GatherPercent * factor);
}

// Las reglas de las mascotas, puras (v0.10.0). Los números están acá y en la base (los topes de cada especie en pet_species.max_bonus_percent):
//   · Nivel 1 a 10. El bonus de una mascota es su tope × nivel / 10 (a nivel 1 ya da un 10 % de su tope, al 10 el tope entero).
//   · Crece COMIÉNDOSE la "Comida para Mascotas" (un consumible que se compra en la tienda), y cada mascota se puede alimentar UNA vez por hora: el cooldown es lo
//     que manda el ritmo (con el oro no alcanzaría para frenarlo y se rompería rápido). Comidas para subir de nivel: 1, 1, 2, 2, 3, 3, 4, 4, 5 = 25 en total = 25 horas.
//   · Todas las que tengas suman a la vez. Se conservan en la segunda vuelta; más adelante (reset 3 o 5) puede haber un bonus de fusión.
//   · TIERS (v0.40.0): al nivel máximo de su tier una mascota puede EVOLUCIONAR (EvolveAsync) y el tope sube de 10 a 20 (tier II) y a 30 (tier III). Pide horas de expedición de ESA mascota y huevos sobrantes
//     de su especie (nada de reglas de Fuego Nuevo: con un huevo sobrante por vuelta, 5 huevos son el FN 6 y 10 más el FN 16). Cada tier bonifica el doble del anterior (como mínimo) y el III es "una locura".
public static class PetRules
{
    // El tope del TIER I y el nivel que pide una expedición. Los topes de los otros tiers salen de LevelCap.
    public const int MaxLevel = 10;

    public const int MaxTier = 3;
    public const int LevelsPerTier = 10;

    // El tope de nivel de un tier: 10, 20, 30.
    public static int LevelCap(int tier) => Math.Clamp(tier, 1, MaxTier) * LevelsPerTier;

    // Horas de expedición ACUMULADAS de la mascota y huevos sobrantes de su especie que pide evolucionar AL tier dado (el II: 100 h y 5 huevos; el III: 250 h en total y 10 huevos más). El dueño (2026-10-29):
    // sin condición de FN, solo huevos (5 huevos = el FN 6, 15 = el FN 16), y solo al nivel máximo del tier.
    public static int EvolveHours(int toTier) => toTier switch { 2 => 100, 3 => 250, _ => int.MaxValue };

    public static int EvolveEggs(int toTier) => toTier switch { 2 => 5, 3 => 10, _ => int.MaxValue };

    // Cuánto multiplica cada tier el premio de las expediciones (oro, EXP, hallazgo): el II el doble, el III el CUÁDRUPLE (el dueño: "cada tier tiene que duplicar… el tier 3 tendría que ser una locura").
    public static int ExpeditionRewardMultiplier(int tier) => Math.Clamp(tier, 1, MaxTier) switch { 1 => 1, 2 => 2, _ => 4 };

    // Comidas que gasta CADA alimentada según el nivel que tiene la mascota: 1 hasta el 10, 3 del 10 al 20 y 6 del 20 al 30 (el oro pesa: "que cueste las mascotas", como el dragón).
    public static int FoodPerFeed(int level) => level < LevelsPerTier ? 1 : level < LevelsPerTier * 2 ? 3 : 6;

    // «I», «II», «III».
    public static string TierName(int tier) => Math.Clamp(tier, 1, MaxTier) switch { 1 => "I", 2 => "II", _ => "III" };

    // El nombre para mostrar: el de la especie y, desde el tier II, el número romano («Ñandusito II»). Lo que se busca y se guarda sigue siendo Species.Name.
    public static string DisplayName(OwnedPet pet) => pet.Tier <= 1 ? pet.Species.Name : $"{pet.Species.Name} {TierName(pet.Tier)}";

    // El nombre del ítem de la comida (Database/seed_pets.sql): es el único lugar que lo nombra en el código.
    public const string FoodItemName = "Comida para Mascotas";

    // Cuánto hay que esperar para volver a alimentar a la MISMA mascota.
    public static readonly TimeSpan FeedCooldown = TimeSpan.FromHours(1);

    // Alimentadas que hacen falta para pasar del nivel N al N+1 (índice 0 = del 1 al 2). Los primeros 9 pasos son el tier I (1, 1, 2, 2, 3, 3, 4, 4, 5 = 25 en total = 25 horas, como siempre); del 10 al 20 son 80
    // (6, 6, 7, 7, 8, 8, 9, 9, 10, 10) a 3 comidas cada una = 240 comidas, y del 20 al 30 son 130 (11…15 dos veces cada una) a 6 comidas = 780. Cada una sigue esperando su hora, así que el tier II son ≥ 80 horas y el III ≥ 130.
    private static readonly int[] FeedsToNextLevel = [1, 1, 2, 2, 3, 3, 4, 4, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15];

    // Las alimentadas del tier I (25): lo que usan los logros y la ayuda para "llegar al máximo".
    public static int TotalFeedsToMax => FeedsToNextLevel.Take(LevelsPerTier - 1).Sum();

    // El nivel (1..MaxLevel) de una mascota con esas comidas dadas.
    public static int LevelFor(int feedPoints)
    {
        int level = 1;
        int accumulated = 0;
        foreach (int needed in FeedsToNextLevel)
        {
            accumulated += needed;
            if (feedPoints < accumulated)
            {
                break;
            }

            level++;
        }

        return level;
    }

    // Cuántas alimentadas hay que haber dado en total para LLEGAR a ese nivel (nivel 1 = 0).
    public static int PointsToReach(int level) => FeedsToNextLevel.Take(Math.Clamp(level, 1, LevelCap(MaxTier)) - 1).Sum();

    // (alimentadas dadas dentro del nivel actual, alimentadas que pide el nivel actual para pasar al siguiente); null si ya está en el tope de su tier (tier = 1 si no se dice).
    public static (int Have, int Need)? ProgressToNextLevel(int feedPoints, int tier = 1)
    {
        int level = LevelFor(feedPoints);
        return level >= LevelCap(tier) ? null : (feedPoints - PointsToReach(level), FeedsToNextLevel[level - 1]);
    }

    // Lo que falta para que esa mascota pueda comer otra vez (cero = ya puede). cooldown: la espera del jugador (PlayerBonuses.PetFeedCooldown: la hora menos lo de Buen Pienso); null = la hora de siempre. nowUtc se pasa de afuera para probarlo; la regla de verdad la hace la base de datos
    // al alimentar (PetRepository.FeedAsync), esto es solo para MOSTRAR y para saber a cuáles vale la pena intentarlo.
    public static TimeSpan RemainingCooldown(OwnedPet pet, DateTime nowUtc, TimeSpan? cooldown = null) =>
        pet.LastFedAtUtc is not { } fedAt ? TimeSpan.Zero : TimeSpan.FromTicks(Math.Max(0, (fedAt + (cooldown ?? FeedCooldown) - nowUtc).Ticks));

    // "1 hora" / "35 min": la espera entre comidas de una mascota, para los textos de la pantalla (con la bendición Buen Pienso baja de la hora).
    public static string CooldownText(TimeSpan? cooldown)
    {
        var value = cooldown ?? FeedCooldown;
        return value == FeedCooldown ? "1 hora" : $"{(int)Math.Round(value.TotalMinutes)} min";
    }

    // En el tope de SU tier (10, 20 o 30): ya no come hasta que evolucione.
    public static bool IsMaxLevel(OwnedPet pet) => LevelFor(pet.FeedPoints) >= LevelCap(pet.Tier);

    // Puede comer ahora: no está en el tope de su tier y pasó la hora desde la última comida.
    public static bool CanEat(OwnedPet pet, DateTime nowUtc, TimeSpan? cooldown = null) => !IsMaxLevel(pet) && RemainingCooldown(pet, nowUtc, cooldown) == TimeSpan.Zero;

    // Cuántas veces su tope da una mascota a ese nivel: hasta el 10 es nivel/10 (a nivel 1 el 10 %, al 10 el tope entero); del 10 al 20 sigue subiendo hasta el DOBLE del tope (tier II) y del 20 al 30 hasta el
    // CUÁDRUPLE (tier III), siempre lineal por nivel (11 = ×1,1 … 20 = ×2 … 30 = ×4). El dueño: "cada tier tiene que duplicar… el tier 3 tendría que ser una locura en premios".
    public static double BonusMultiplier(int level)
    {
        int l = Math.Clamp(level, 1, LevelCap(MaxTier));
        if (l <= LevelsPerTier)
        {
            return l / (double)LevelsPerTier;
        }

        return l <= LevelsPerTier * 2 ? 1.0 + ((l - LevelsPerTier) / (double)LevelsPerTier) : 2.0 + (2.0 * (l - (LevelsPerTier * 2)) / LevelsPerTier);
    }

    // Lo que da esa especie al nivel dado: su tope × BonusMultiplier(nivel).
    public static double BonusPercent(PetSpecies species, int level) => species.MaxBonusPercent * BonusMultiplier(level);

    public static double BonusPercent(OwnedPet pet) => BonusPercent(pet.Species, LevelFor(pet.FeedPoints));

    // Todas las mascotas del jugador, sumadas por tipo de bonus.
    public static PetBonuses Total(IEnumerable<OwnedPet> pets)
    {
        double gold = 0, xp = 0, defense = 0, drop = 0, gather = 0;
        foreach (var pet in pets)
        {
            double percent = BonusPercent(pet);
            switch (pet.Species.BonusKind)
            {
                case PetBonusKind.Gold: gold += percent; break;
                case PetBonusKind.Xp: xp += percent; break;
                case PetBonusKind.Defense: defense += percent; break;
                case PetBonusKind.Drop: drop += percent; break;
                case PetBonusKind.Gather: gather += percent; break;
            }
        }

        return new PetBonuses(gold, xp, defense, drop, gather);
    }

    // Sube una cantidad un porcentaje (redondeando al más cercano: 38 con +5 % son 40). Con 0 % o menos no toca nada.
    public static int Boost(int amount, double percent) =>
        percent <= 0 ? amount : (int)Math.Round(amount * (1 + percent / 100.0), MidpointRounding.AwayFromZero);

    // "oro" / "EXP" / "defensa" / "drop de monstruos": cómo se nombra cada bonus ante el jugador.
    public static string KindName(string bonusKind) => bonusKind switch
    {
        PetBonusKind.Gold => "oro",
        PetBonusKind.Xp => "EXP",
        PetBonusKind.Defense => "defensa",
        PetBonusKind.Drop => "drop de monstruos",
        PetBonusKind.Gather => "recolección",
        _ => bonusKind,
    };

    // "+1,5 %" con coma decimal (un decimal como mucho).
    public static string PercentText(double percent) =>
        $"+{percent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',')} %";
}
