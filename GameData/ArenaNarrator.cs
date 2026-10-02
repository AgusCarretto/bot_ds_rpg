using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Cómo se cuenta cada pelea de la Arena en el canal: no es lo mismo una paliza que una que se definió por un pelo. Puro y determinista (la
// frase de cada pelea sale de su ronda y su lugar, no de un azar: contarla dos veces dice lo mismo), así se prueba sin Discord.
public static class ArenaNarrator
{
    // Con cuánta vida terminó el ganador para contar la pelea como una paliza (nunca lo tocaron mucho) o como "por un pelo".
    public const int CrushingHpPercent = 85;
    public const int NarrowHpPercent = 15;

    // Una pelea larga: las acciones son de los DOS, así que 50 son 25 golpes cada uno.
    public const int LongFightActions = 50;

    public static string Describe(ArenaMatchRow match)
    {
        string p1 = Short(match.P1Name);

        if (match.P2Id is null || match.P2Name is null)
        {
            return $"💤 **{p1}** no tuvo rival y pasa directo.";
        }

        bool firstWon = match.WinnerId == match.P1Id;
        string winner = Short(firstWon ? match.P1Name : match.P2Name);
        string loser = Short(firstWon ? match.P2Name : match.P1Name);
        int hp = match.WinnerHpPercent;

        string[] templates = hp <= NarrowHpPercent
            ?
            [
                $"😮 ¡Por un pelo! **{winner}** le ganó a {loser} con apenas {hp}% de vida.",
                $"😰 Final de infarto: **{winner}** aguantó lo justo ({hp}%) y dejó afuera a {loser}.",
                $"🩹 **{winner}** sobrevivió con {hp}% de vida y se llevó la pelea contra {loser}.",
            ]
            : hp >= CrushingHpPercent
                ?
                [
                    $"🔥 **{winner}** le pasó por encima a {loser} ({hp}% de vida, ni se despeinó).",
                    $"💥 Paliza: {loser} no tuvo chance contra **{winner}**.",
                    $"🧨 **{winner}** aplastó a {loser} sin sufrir casi nada.",
                ]
                : match.Actions >= LongFightActions
                    ?
                    [
                        $"⏳ Batalla larguísima ({match.Actions} golpes): **{winner}** terminó venciendo a {loser}.",
                        $"🥵 {loser} y **{winner}** se dieron con todo; ganó **{winner}** al final.",
                    ]
                    :
                    [
                        $"⚔️ **{winner}** venció a {loser}.",
                        $"🏁 **{winner}** dejó afuera a {loser} ({hp}% de vida).",
                        $"✅ **{winner}** se quedó con la pelea contra {loser}.",
                    ];

        return templates[Seed(match) % templates.Length];
    }

    // La línea de la final, para cerrar: quién se coronó y contra quién.
    public static string Crowning(ArenaMatchRow final)
    {
        bool firstWon = final.WinnerId == final.P1Id;
        string champion = Short(firstWon ? final.P1Name : final.P2Name ?? final.P1Name);
        string runnerUp = Short(firstWon ? final.P2Name ?? "?" : final.P1Name);

        return $"👑 **{champion}** se coronó campeón de la Arena tras vencer en la final a {runnerUp}.";
    }

    private static int Seed(ArenaMatchRow match) => (int)(((uint)match.Round * 31 + (uint)match.Slot * 17 + (uint)(match.WinnerId % 97)) % 1009);

    private static string Short(string text) => text.Length <= 20 ? text : text[..19] + "…";
}
