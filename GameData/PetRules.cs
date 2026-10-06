using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Lo que cada mascota puede dar (pet_species.bonus_kind). Texto, como en la base.
public static class PetBonusKind
{
    public const string Gold = "gold";       // % más de oro en lo que pagan /hunt, /travel, /boss, /raid y /autohunt
    public const string Xp = "xp";           // % más de EXP en esas mismas peleas
    public const string Defense = "defense"; // % más de la defensa total, en peleas contra monstruos (no en duelos ni en la Arena)
    public const string Drop = "drop";       // % MÁS chances de drop de los monstruos de cacería y viaje (relativo: 6 % sobre un 6 % da 6,36 %); no los cofres de jefe
}

// La suma de todas las mascotas que tiene un jugador: valen TODAS a la vez (pedido del dueño). Porcentajes (5,5 = 5,5 %).
public sealed record PetBonuses(double GoldPercent, double XpPercent, double DefensePercent, double DropPercent)
{
    public static readonly PetBonuses None = new(0, 0, 0, 0);

    public bool IsNone => GoldPercent == 0 && XpPercent == 0 && DefensePercent == 0 && DropPercent == 0;
}

// Las reglas de las mascotas, puras (v0.10.0). Los números están acá y en la base (los topes de cada especie en pet_species.max_bonus_percent):
//   · Nivel 1 a 10. El bonus de una mascota es su tope × nivel / 10 (a nivel 1 ya da un 10 % de su tope, al 10 el tope entero).
//   · Crece COMIÉNDOSE la "Comida para Mascotas" (un consumible que se compra en la tienda), y cada mascota se puede alimentar UNA vez por hora: el cooldown es lo
//     que manda el ritmo (con el oro no alcanzaría para frenarlo y se rompería rápido). Comidas para subir de nivel: 1, 1, 2, 2, 3, 3, 4, 4, 5 = 25 en total = 25 horas.
//   · Todas las que tengas suman a la vez. Se conservan en la segunda vuelta; más adelante (reset 3 o 5) puede haber un bonus de fusión.
public static class PetRules
{
    public const int MaxLevel = 10;

    // El nombre del ítem de la comida (Database/seed_pets.sql): es el único lugar que lo nombra en el código.
    public const string FoodItemName = "Comida para Mascotas";

    // Cuánto hay que esperar para volver a alimentar a la MISMA mascota.
    public static readonly TimeSpan FeedCooldown = TimeSpan.FromHours(1);

    // Comidas que hacen falta para pasar del nivel N al N+1 (índice 0 = del 1 al 2).
    private static readonly int[] FeedsToNextLevel = [1, 1, 2, 2, 3, 3, 4, 4, 5];

    public static int TotalFeedsToMax => FeedsToNextLevel.Sum();

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

    // Cuántas comidas hay que haber dado en total para LLEGAR a ese nivel (nivel 1 = 0).
    public static int PointsToReach(int level) => FeedsToNextLevel.Take(Math.Clamp(level, 1, MaxLevel) - 1).Sum();

    // (comidas dadas dentro del nivel actual, comidas que pide el nivel actual para pasar al siguiente); null si ya está al máximo.
    public static (int Have, int Need)? ProgressToNextLevel(int feedPoints)
    {
        int level = LevelFor(feedPoints);
        return level >= MaxLevel ? null : (feedPoints - PointsToReach(level), FeedsToNextLevel[level - 1]);
    }

    // Lo que falta para que esa mascota pueda comer otra vez (cero = ya puede). nowUtc se pasa de afuera para probarlo; la regla de verdad la hace la base de datos
    // al alimentar (PetRepository.FeedAsync), esto es solo para MOSTRAR y para saber a cuáles vale la pena intentarlo.
    public static TimeSpan RemainingCooldown(OwnedPet pet, DateTime nowUtc) =>
        pet.LastFedAtUtc is not { } fedAt ? TimeSpan.Zero : TimeSpan.FromTicks(Math.Max(0, (fedAt + FeedCooldown - nowUtc).Ticks));

    public static bool IsMaxLevel(OwnedPet pet) => LevelFor(pet.FeedPoints) >= MaxLevel;

    // Puede comer ahora: no está al máximo y pasó la hora desde la última comida.
    public static bool CanEat(OwnedPet pet, DateTime nowUtc) => !IsMaxLevel(pet) && RemainingCooldown(pet, nowUtc) == TimeSpan.Zero;

    // Lo que da esa especie al nivel dado: su tope × nivel / 10.
    public static double BonusPercent(PetSpecies species, int level) => species.MaxBonusPercent * Math.Clamp(level, 1, MaxLevel) / MaxLevel;

    public static double BonusPercent(OwnedPet pet) => BonusPercent(pet.Species, LevelFor(pet.FeedPoints));

    // Todas las mascotas del jugador, sumadas por tipo de bonus.
    public static PetBonuses Total(IEnumerable<OwnedPet> pets)
    {
        double gold = 0, xp = 0, defense = 0, drop = 0;
        foreach (var pet in pets)
        {
            double percent = BonusPercent(pet);
            switch (pet.Species.BonusKind)
            {
                case PetBonusKind.Gold: gold += percent; break;
                case PetBonusKind.Xp: xp += percent; break;
                case PetBonusKind.Defense: defense += percent; break;
                case PetBonusKind.Drop: drop += percent; break;
            }
        }

        return new PetBonuses(gold, xp, defense, drop);
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
        _ => bonusKind,
    };

    // "+1,5 %" con coma decimal (un decimal como mucho).
    public static string PercentText(double percent) =>
        $"+{percent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',')} %";
}
