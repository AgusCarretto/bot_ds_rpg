namespace BotDsRpg.GameData;

// Cómo se contó una pelea de la Arena: antes de empezar (la forma del día de cada uno y, si hubo, el milagro) y los golpes que más se notaron. Se arma al jugar el torneo y vive solo en memoria hasta que el reloj lo anuncia
// (lo que se guarda en la base sigue siendo la llave: quién ganó, cuántas acciones y con cuánta vida). P1Id/P2Id son los de la pareja tal como se cruzó.
public sealed record ArenaFightStory(ulong P1Id, ulong P2Id, bool Miracle, IReadOnlyList<string> Opening, IReadOnlyList<string> Highlights);

public sealed record ArenaFightResult(DuelResult Result, ArenaFightStory Story);

// Juega una pelea de la Arena con su azar (ArenaTuning) y la va contando. Puro: toda la aleatoriedad sale del rng que se le pasa.
public static class ArenaFightRunner
{
    // maxHighlights: cuántos golpes guarda (0 = ninguno, solo la apertura).
    public static ArenaFightResult Play(DuelFighter a, DuelFighter b, Random rng, int maxHighlights = ArenaStoryteller.DetailedHighlights)
    {
        var setup = ArenaTuning.Prepare(a, b, rng);
        var state = DuelEngine.Start(setup.A, setup.B, aStarts: rng.Next(2) == 0);
        var events = new List<DuelEvent>();

        while (!state.Finished && state.Actions < DuelEngine.MaxActions)
        {
            var step = DuelEngine.Step(state, DuelEngine.ChooseAction(state));
            state = step.State;
            events.Add(step.Event);
        }

        var result = DuelEngine.Conclude(state);
        return new ArenaFightResult(result, ArenaStoryteller.Compose(setup, events, maxHighlights, rng));
    }
}

// Los textos de la Arena. Las frases se eligen con el rng que se pasa (en las pruebas, uno con semilla).
public static class ArenaStoryteller
{
    // Cuántos golpes cuenta como máximo cada pelea: se guardan siempre, y el anuncio (ArenaModule.BuildBroadcast) los muestra en las semifinales y la final; las rondas de antes solo cuentan la apertura y el resultado.
    public const int DetailedHighlights = 8;

    // Desde qué diferencia (en la forma del día) se cuenta que alguien llegó muy bien o muy mal: ±12 %.
    public const double NoticeableForm = 0.12;

    public static ArenaFightStory Compose(ArenaSetup setup, IReadOnlyList<DuelEvent> events, int maxHighlights, Random rng)
    {
        var opening = new List<string>();
        string nameA = Short(setup.A.Name), nameB = Short(setup.B.Name);

        if (setup.Miracle)
        {
            string[] miracles =
            [
                $"🍀 ¡Milagro en la Arena! Los astros se alinearon y **{nameA}** y **{nameB}** pelean hoy de igual a igual.",
                $"🌟 Algo raro pasa en el aire: el destino igualó a **{nameA}** y **{nameB}**. Hoy nadie es favorito.",
                $"✨ ¡Día de suerte! La Arena emparejó las fuerzas de **{nameA}** y **{nameB}**: quien juegue mejor se lleva la pelea.",
            ];
            opening.Add(miracles[rng.Next(miracles.Length)]);
        }

        foreach (var (name, form) in new[] { (nameA, setup.FormA), (nameB, setup.FormB) })
        {
            if (form - 1 >= NoticeableForm)
            {
                string[] great =
                [
                    $"💪 **{name}** llegó en un día brillante (+{Percent(form - 1)} %).",
                    $"🔥 **{name}** se levantó con todo (+{Percent(form - 1)} %).",
                ];
                opening.Add(great[rng.Next(great.Length)]);
            }
            else if (1 - form >= NoticeableForm)
            {
                string[] poor =
                [
                    $"🤕 **{name}** se levantó con el pie izquierdo (−{Percent(1 - form)} %).",
                    $"😴 **{name}** llegó con la cabeza en otro lado (−{Percent(1 - form)} %).",
                ];
                opening.Add(poor[rng.Next(poor.Length)]);
            }
        }

        return new ArenaFightStory(setup.A.DiscordId, setup.B.DiscordId, setup.Miracle, opening, PickHighlights(events, maxHighlights));
    }

    // Los golpes que se cuentan, en el orden en que pasaron. Prioridad cuando no entran todos: el golpe final, el primero, el más fuerte, las habilidades, los críticos y, si sobra lugar, los esquives.
    public static IReadOnlyList<string> PickHighlights(IReadOnlyList<DuelEvent> events, int max)
    {
        if (events.Count == 0 || max <= 0)
        {
            return [];
        }

        var chosen = new SortedSet<int>();
        void Add(int index)
        {
            if (chosen.Count < max && index >= 0 && index < events.Count)
            {
                chosen.Add(index);
            }
        }

        Add(events.Count - 1);
        Add(0);
        int strongest = 0;
        for (int i = 1; i < events.Count; i++)
        {
            if (events[i].Damage > events[strongest].Damage)
            {
                strongest = i;
            }
        }

        Add(strongest);
        for (int i = 0; i < events.Count; i++) { if (events[i].AbilityUsed is not null) { Add(i); } }
        for (int i = 0; i < events.Count; i++) { if (events[i].Crits > 0) { Add(i); } }
        for (int i = 0; i < events.Count; i++) { if (events[i].Dodged) { Add(i); } }

        return chosen.Select(i => DuelNarrator.Describe(events[i])).ToList();
    }

    private static int Percent(double fraction) => (int)Math.Round(fraction * 100);

    private static string Short(string text) => text.Length <= 20 ? text : text[..19] + "…";
}
