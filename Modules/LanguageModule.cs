using System.Globalization;
using System.Text;
using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Interactions;

// /language (aa language, alias aa idioma): el idioma del bot para cada jugador, español o inglés (GameData/Loc.cs). Sin argumento dice cuál tenés; con uno lo guarda y contesta YA en el idioma nuevo.
// La lógica vive en el método estático (el slash y el comando de texto lo llaman); el idioma que muestra sale del GUARDADO del jugador, no del que haya en el momento, así se prueba sin Discord.
public class LanguageModule(IPlayerLanguageService languageService) : InteractionModuleBase<SocketInteractionContext>
{
    // La descripción es bilingüe a propósito (la ve quien todavía no eligió idioma) y mide menos de 100 caracteres: ese es el límite de Discord y pasarse tumba el registro de TODOS los comandos.
    [SlashCommand("language", "Idioma / Language: español o English")]
    public async Task HandleLanguageAsync(
        [Summary("idioma", "Español o English / Spanish or English")] [Choice("Español", "es"), Choice("English", "en")] string? idioma = null)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            string message = await ExecuteLanguageAsync(languageService, Context.User.Id, idioma, Context.Interaction.UserLocale);
            await FollowupAsync(message, ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude cambiar tu idioma, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // requested: lo que escribió el jugador (null o vacío = solo mirar). Devuelve el mensaje ya en el idioma que corresponde.
    public static async Task<string> ExecuteLanguageAsync(IPlayerLanguageService service, ulong discordId, string? requested, string? discordLocale)
    {
        string current = await service.ResolveAsync(discordId, discordLocale);

        if (string.IsNullOrWhiteSpace(requested))
        {
            using (Loc.Use(current))
            {
                return Loc.F($"🌐 Tu idioma es **{NameOf(current)}**. Para cambiarlo usá `/language` (o `aa language es` / `aa language en`).");
            }
        }

        string? wanted = Parse(requested);
        if (wanted is null)
        {
            using (Loc.Use(current))
            {
                return Loc.T("No conozco ese idioma. Elegí **es** (español) o **en** (English).");
            }
        }

        try
        {
            await service.SetAsync(discordId, wanted);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            using (Loc.Use(current))
            {
                return Loc.T("¡Upa! No pude guardar tu idioma, probá de nuevo en un momento.");
            }
        }

        using (Loc.Use(wanted))
        {
            return Loc.F($"✅ Listo: el bot te va a hablar en **{NameOf(wanted)}**.");
        }
    }

    // «es», «español», «espanol», «spanish», «castellano» → es; «en», «english», «ingles», «inglés» → en; lo demás, null. Sin mirar mayúsculas ni tildes.
    public static string? Parse(string? text)
    {
        string word = RemoveAccents((text ?? string.Empty).Trim()).ToLowerInvariant();
        return word switch
        {
            "es" or "espanol" or "spanish" or "castellano" => Loc.Spanish,
            "en" or "english" or "ingles" => Loc.English,
            _ => null,
        };
    }

    // Los nombres de los idiomas no se traducen: cada uno se escribe en su propio idioma.
    private static string NameOf(string language) => language == Loc.English ? "English" : "Español";

    private static string RemoveAccents(string text)
    {
        var builder = new StringBuilder();
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
