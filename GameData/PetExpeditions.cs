using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Qué trae una expedición ADEMÁS del oro y la EXP de siempre, según la especie de la mascota (su tipo de bonus): oro extra, EXP extra, Polvo, drops de cacería de tu zona o materiales de recolección.
public enum ExpeditionFindKind
{
    GoldBag,    // Ñandusito (oro): +50 % de oro
    XpBoost,    // Cachorro de Puma y Quimerita (EXP): +50 % de EXP
    Dust,       // Gólem de bolsillo (defensa): Polvo
    HuntDrops,  // Salamandrita (drop): drops de los monstruos de cacería de TU zona
    Gather,     // Chispa del Asador (recolección): madera y mineral como si hubieras talado y minado
}

// Una de las tres duraciones. El oro es un RANGO fijo (GoldMin–GoldMax, de a 100) que NO depende de la zona; la EXP es un % del nivel que el jugador tiene hoy (XpPercent); FoodCost son las Comidas para
// Mascotas que cuesta salir; FindChancePercent es la chance del hallazgo especial de la especie y DustFind / DropQuantity / GatherRolls, cuánto trae si sale.
public sealed record ExpeditionOption(int Hours, int GoldMin, int GoldMax, int XpPercent, int FindChancePercent, int FoodCost, int DustFind, int DropQuantity, int GatherRolls)
{
    public TimeSpan Duration => TimeSpan.FromHours(Hours);
}

// Lo que salió del hallazgo, para mostrarlo. Amount: Polvo o la cantidad de drops/materiales; Items: lo que se agregó a la mochila (nombre, cantidad).
public sealed record ExpeditionFindResult(ExpeditionFindKind Kind, int Amount, IReadOnlyList<(string ItemName, int Quantity)> Items);

// Las expediciones de las mascotas (v0.39.0), el primer uso de una mascota de nivel 10: se la manda a una expedición de 1, 4 u 8 horas, y mientras más tiempo, más oro, más EXP y más chance de que traiga algo.
// Reglas (el dueño, 2026-10-29): solo mascotas de NIVEL 10, de a UNA por jugador, y cada duración gasta sus Comidas para Mascotas (2 / 10 / 25). El premio es el MISMO para todos los que llegan a ese punto
// (el dueño: «que no dependa de la zona, es un premio para todo el que llegue a ese punto»): el oro es un rango fijo pensado para la Zona 5 y la EXP un % del nivel (5 / 20 / 50 %), sin importar la zona ni la especie.
// La especie solo decide el HALLAZGO. Todo lo que se retoca está en Options y en las constantes de acá.
// Desde la v0.39.1 NINGUNA pantalla (el menú, la salida, la ayuda) muestra el premio ni el hallazgo de cada especie: el dueño quiere que los jugadores lo descubran de a poco. Solo la pantalla de la vuelta cuenta lo que trajo.
public static class PetExpeditions
{
    // Nivel mínimo de la mascota: el máximo de hoy (PetRules.MaxLevel).
    public const int RequiredLevel = PetRules.MaxLevel;

    // El hallazgo de oro o de EXP suma este % a lo de la expedición (el de oro: +50 % del oro que salió; el de EXP: +50 % de la EXP).
    public const int FindBonusPercent = 50;

    // Calibrado pensando en la Zona 5 (el dueño: «siempre pensá en zona 5»; una sesión activa perfecta de 2 h paga ~90.000 allá): la de 8 horas rinde de media 45.000 de oro (la mitad de esa sesión), la de 4
    // horas 15.000 y la de 1 hora 3.000, o sea 3.000 / 3.750 / 5.625 por hora: la espera larga rinde más por hora. La comida (2 / 10 / 25 a 100 de oro) es el 6-7 % de lo que paga.
    // OJO, lo vio el dueño: como no depende de la zona, en las zonas bajas es un premio enorme (la de 8 horas paga más que horas de cacería de la Zona 2).
    public static readonly IReadOnlyList<ExpeditionOption> Options =
    [
        new(1, 2_000, 4_000, 5, 10, 2, 30, 1, 2),
        new(4, 10_000, 20_000, 20, 35, 10, 120, 1, 4),
        new(8, 30_000, 60_000, 50, 70, 25, 240, 2, 8),
    ];

    public static ExpeditionOption? OptionFor(int hours) => Options.FirstOrDefault(o => o.Hours == hours);

    // Lo más barato que cuesta salir (para saber si vale la pena mostrar el menú activo).
    public static int CheapestFoodCost => Options.Min(o => o.FoodCost);

    // Puede salir de expedición: nivel 10.
    public static bool IsEligible(OwnedPet pet) => PetRules.LevelFor(pet.FeedPoints) >= RequiredLevel;

    public static ExpeditionFindKind FindKindOf(string bonusKind) => bonusKind switch
    {
        PetBonusKind.Gold => ExpeditionFindKind.GoldBag,
        PetBonusKind.Xp => ExpeditionFindKind.XpBoost,
        PetBonusKind.Defense => ExpeditionFindKind.Dust,
        PetBonusKind.Drop => ExpeditionFindKind.HuntDrops,
        PetBonusKind.Gather => ExpeditionFindKind.Gather,
        _ => ExpeditionFindKind.GoldBag,
    };

    // El oro de ESTA expedición: un valor al azar del rango, redondo a 100, ×1 / ×2 / ×4 según el tier de la mascota (PetRules.ExpeditionRewardMultiplier). Con el hallazgo de oro, +50 %. rng se inyecta para probarlo.
    public static int RollGold(ExpeditionOption option, bool goldFind, Random rng, int tier = 1)
    {
        int steps = ((option.GoldMax - option.GoldMin) / 100) + 1;
        int gold = (option.GoldMin + (rng.Next(steps) * 100)) * PetRules.ExpeditionRewardMultiplier(tier);
        return goldFind ? gold + (gold * FindBonusPercent / 100) : gold;
    }

    // El % de nivel de EXP de ESTA expedición, ×1 / ×2 / ×4 según el tier; con el hallazgo de EXP, +50 % (redondeado hacia arriba: 5 % → 8 %).
    public static int XpPercentFor(ExpeditionOption option, bool xpFind, int tier = 1)
    {
        int percent = option.XpPercent * PetRules.ExpeditionRewardMultiplier(tier);
        return xpFind ? (int)Math.Round(percent * (1 + (FindBonusPercent / 100.0)), MidpointRounding.AwayFromZero) : percent;
    }

    // ¿Salió el hallazgo? rng se inyecta para probarlo.
    public static bool RollFind(ExpeditionOption option, Random rng) => rng.Next(100) < option.FindChancePercent;

    // Lo que trae el hallazgo según el tier (×1 / ×2 / ×4): el Polvo del Gólem, los drops de la Salamandrita y las tiradas de la Chispa.
    public static int DustFor(ExpeditionOption option, int tier = 1) => option.DustFind * PetRules.ExpeditionRewardMultiplier(tier);

    public static int DropQuantityFor(ExpeditionOption option, int tier = 1) => option.DropQuantity * PetRules.ExpeditionRewardMultiplier(tier);

    public static int GatherRollsFor(ExpeditionOption option, int tier = 1) => option.GatherRolls * PetRules.ExpeditionRewardMultiplier(tier);

    // El hallazgo de la Chispa: tiradas alternadas de madera y mineral con las chances de /chop y /mine (RarityCatalog) y sus unidades (GatheringYield), SIN el Mítico (la Corteza y el Meteorito siguen siendo de
    // /chop, /mine, el Brasero del Fogón y el Tabernero del Inframundo): si sale Mítico cuenta como Legendario. Devuelve lo que se suma a la mochila, juntando repetidos.
    public static IReadOnlyList<(GatherCandidate Item, int Quantity)> RollGather(IReadOnlyList<GatherCandidate> pool, int rolls, Random rng)
    {
        var totals = new Dictionary<int, (GatherCandidate Item, int Quantity)>();
        for (int i = 0; i < rolls; i++)
        {
            string type = i % 2 == 0 ? "Madera" : "Mineral";
            string rarity = RollGatherRarity(rng);
            var candidates = pool.Where(p => p.Type == type && p.Rarity == rarity).ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            var item = candidates[rng.Next(candidates.Count)];
            int quantity = GatheringYield.RollScaled(rarity, 1.0, rng);
            totals[item.ItemId] = totals.TryGetValue(item.ItemId, out var have) ? (item, have.Quantity + quantity) : (item, quantity);
        }

        return totals.Values.OrderBy(t => RarityCatalog.RankOf(t.Item.Rarity)).ThenBy(t => t.Item.Name).ToList();
    }

    private static string RollGatherRarity(Random rng)
    {
        double roll = rng.NextDouble() * 100.0;
        double cumulative = 0;
        foreach (var (rarity, percent) in RarityCatalog.GatheringChances)
        {
            cumulative += percent;
            if (roll < cumulative)
            {
                return rarity == "Mítico" ? "Legendario" : rarity;
            }
        }

        return "Común";
    }

    // «1 hora» / «4 horas» / «8 horas».
    public static string HoursText(int hours) => hours == 1 ? "1 hora" : $"{hours} horas";
}
