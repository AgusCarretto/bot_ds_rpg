namespace BotDsRpg.GameData;

// Una comida que el jugador tiene en la mochila, para planear cuánto comer.
public sealed record FoodStock(int ItemId, string Name, string? Emoji, int HealPerUnit, int Quantity);

public sealed record PlannedFood(FoodStock Food, int Quantity);

// Qué comer para curarse. Covers = la comida alcanza para llenar la vida; si no alcanza, el plan es TODA la comida que había.
public sealed record HealPlan(IReadOnlyList<PlannedFood> Foods, int TotalHeal, int Missing, bool Covers)
{
    public int Units => Foods.Sum(f => f.Quantity);

    // Cuánto de la curación sobra (el último bocado que se pasa de lo que faltaba).
    public int Overshoot => Math.Max(0, TotalHeal - Missing);
}

// El plan de /heal ("curate del todo"): dada la vida que falta y la comida disponible, elige QUÉ y CUÁNTO comer para llenar la vida
// desperdiciando lo menos posible. PURO (sin base ni Discord), así se prueba con todos los casos raros.
//
// Regla: de todas las formas de llegar a la vida llena, la que come MENOS curación en total (menor sobrante); a igual sobrante, la que
// usa menos unidades. Es un problema de mochila acotada, y con unos pocos tipos de comida y unos cientos de HP se resuelve exacto en
// microsegundos (programación dinámica): nada de adivinar con un "empezá por la más chica", que a veces se pasa de largo.
//   - Ej: faltan 100 HP, con 10 mates (15) y 2 empanadas (45): come 2 empanadas + 1 mate (105), no 7 mates ni 3 empanadas.
//   - Si la comida no alcanza, el plan es TODA la comida (Covers = false): cura lo que se pueda.
//   - Si le pasás UN solo tipo de comida (el jugador eligió cuál), el resultado es simplemente cuántas unidades de esa hacen falta.
public static class HealPlanner
{
    private const int Infinite = int.MaxValue / 2;

    public static HealPlan Plan(int missingHp, IReadOnlyList<FoodStock> stock)
    {
        var usable = stock.Where(f => f.Quantity > 0 && f.HealPerUnit > 0).OrderBy(f => f.HealPerUnit).ThenBy(f => f.Name, StringComparer.Ordinal).ToList();

        if (missingHp <= 0 || usable.Count == 0)
        {
            return new HealPlan([], 0, Math.Max(0, missingHp), Covers: missingHp <= 0);
        }

        long available = usable.Sum(f => (long)f.HealPerUnit * f.Quantity);
        if (available < missingHp)
        {
            // No alcanza: se come todo y se cura lo que se pueda.
            return new HealPlan(usable.Select(f => new PlannedFood(f, f.Quantity)).ToList(), (int)available, missingHp, Covers: false);
        }

        // Nunca hace falta pasarse más de lo que cura la comida más grande: con missing + maxHeal alcanza como tope de la suma.
        int maxHeal = usable.Max(f => f.HealPerUnit);
        int cap = missingHp + maxHeal;

        // Mochila acotada con "binary splitting": cada comida se parte en paquetes de 1, 2, 4... unidades (más un resto) y cada paquete se toma o no.
        // De una comida no se pueden querer más unidades de las que caben en el tope.
        var chunks = new List<(int FoodIndex, int Units, int Weight)>();
        for (int i = 0; i < usable.Count; i++)
        {
            int left = Math.Min(usable[i].Quantity, (cap + usable[i].HealPerUnit - 1) / usable[i].HealPerUnit);
            for (int pack = 1; left > 0; pack <<= 1)
            {
                int take = Math.Min(pack, left);
                chunks.Add((i, take, take * usable[i].HealPerUnit));
                left -= take;
            }
        }

        // dp[s] = la menor cantidad de unidades para sumar EXACTAMENTE s de curación; taken[c][s] dice si el paquete c fue el último que la mejoró.
        var dp = new int[cap + 1];
        Array.Fill(dp, Infinite);
        dp[0] = 0;
        var taken = new bool[chunks.Count][];

        for (int c = 0; c < chunks.Count; c++)
        {
            taken[c] = new bool[cap + 1];
            var (_, units, weight) = chunks[c];
            for (int s = cap; s >= weight; s--)
            {
                if (dp[s - weight] + units < dp[s])
                {
                    dp[s] = dp[s - weight] + units;
                    taken[c][s] = true;
                }
            }
        }

        int best = -1;
        for (int s = missingHp; s <= cap; s++)
        {
            if (dp[s] < Infinite)
            {
                best = s;
                break;
            }
        }

        if (best < 0)
        {
            // No debería pasar (available >= missingHp), pero un plan inválido es peor que comer todo.
            return new HealPlan(usable.Select(f => new PlannedFood(f, f.Quantity)).ToList(), (int)available, missingHp, Covers: true);
        }

        var perFood = new int[usable.Count];
        int remaining = best;
        for (int c = chunks.Count - 1; c >= 0; c--)
        {
            if (taken[c][remaining])
            {
                perFood[chunks[c].FoodIndex] += chunks[c].Units;
                remaining -= chunks[c].Weight;
            }
        }

        var foods = Enumerable.Range(0, usable.Count)
            .Where(i => perFood[i] > 0)
            .Select(i => new PlannedFood(usable[i], perFood[i]))
            .ToList();

        return new HealPlan(foods, best, missingHp, Covers: true);
    }
}
