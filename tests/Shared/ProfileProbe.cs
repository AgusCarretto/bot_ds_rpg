using Discord;

// Lee el embed del perfil (GameModule.BuildProfileEmbedAsync) para los arneses. Desde la v0.25.2 el bloque de números no tiene título («Estado» se sacó): es el campo cuyo nombre es
// GameModule.ProfileStatsFieldName, y cada línea es «<ícono> **Nombre:** valor». Un solo lugar que sabe eso, así el próximo cambio de formato toca esto y no diez arneses.
public static class ProfileProbe
{
    public static string Stats(Embed e) => e.Fields.Single(f => f.Name == GameModule.ProfileStatsFieldName).Value;

    // La línea de un número («ATQ», «DEF», «Oro», «Banco», «Polvo», «Racha»); vacía si no hay.
    public static string Line(Embed e, string label) =>
        Stats(e).Split('\n').FirstOrDefault(l => l.Contains($"**{label}:**")) ?? string.Empty;

    // El número que sigue al nombre en esa línea («⚔️ **ATQ:** 1.234 ⚡» → 1234); 0 si no hay línea.
    public static int Number(Embed e, string label)
    {
        string line = Line(e, label);
        int at = line.IndexOf($"**{label}:**", StringComparison.Ordinal);
        if (at < 0)
        {
            return 0;
        }

        string rest = line[(at + label.Length + 5)..].TrimStart();
        string digits = new string(rest.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Replace(".", string.Empty);
        return digits.Length == 0 ? 0 : int.Parse(digits);
    }

    // La línea de la zona, que va en la descripción; vacía si no hay.
    public static string ZoneLine(Embed e) =>
        e.Description.Split('\n').FirstOrDefault(l => l.Contains("**Zona:**")) ?? string.Empty;
}
