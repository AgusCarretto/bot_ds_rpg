using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): los títulos. Misma lógica que /titles (TitlesModule): acá solo se lee lo que escribió el jugador.
public partial class TextCommandModule
{
    // "aa titles" muestra tus títulos con el desplegable de páginas; "aa titles 6" o "aa titulos jefes" abre esa página.
    [Command("titles")]
    [Alias("titulos", "títulos")]
    [Summary("Tus títulos: \"aa titles\" para verlos (o \"aa titles jefes\" para una página) y \"aa title <nombre>\" para ponerte uno.")]
    public async Task TitlesAsync([Remainder] string pagina = "")
    {
        try
        {
            var page = TitleCatalog.ParsePage(pagina) is { } index ? TitleCatalog.Pages[index] : TitleCatalog.Pages[0];
            var view = await TitlesModule.BuildViewAsync(userRepository, zoneRepository, titleRepository, Context.User.Id, page.Key);
            await ReplyAsync(view.PlainMessage, embed: view.Embed, components: view.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude mostrar tus títulos, intentá de nuevo en un momento.");
        }
    }

    // "aa title Matajefes" te lo pone (sin mirar mayúsculas ni tildes, alcanza con parte del nombre si es una sola); "aa title quitar" te lo saca; sin nada, muestra la lista.
    [Command("title")]
    [Alias("titulo", "título")]
    [Summary("Ponete un título en el perfil: \"aa title Matajefes\" (o \"aa title quitar\" para sacarlo).")]
    public async Task TitleAsync([Remainder] string nombre = "")
    {
        try
        {
            string wanted = nombre.Trim();
            if (wanted.Length == 0)
            {
                await TitlesAsync();
                return;
            }

            if (wanted.ToLowerInvariant() is "quitar" or "sacar" or "ninguno" or "clear" or "off")
            {
                var cleared = await TitlesModule.ExecuteClearAsync(userRepository, titleRepository, Context.User.Id);
                await ReplyAsync(cleared.Message);
                return;
            }

            var matches = TitleCatalog.FindByName(wanted);
            if (matches.Count == 0)
            {
                await ReplyAsync("No encontré ningún título con ese nombre. Mirá la lista con **aa titles**.");
                return;
            }

            if (matches.Count > 1)
            {
                await ReplyAsync($"Hay varios que se parecen: {string.Join(", ", matches.Take(6).Select(t => $"**{t.Name}**"))}{(matches.Count > 6 ? "…" : string.Empty)}. Escribí el nombre entero.");
                return;
            }

            var result = await TitlesModule.ExecuteEquipAsync(userRepository, zoneRepository, titleRepository, Context.User.Id, matches[0].Key);
            await ReplyAsync(result.Message);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude ponerte ese título, intentá de nuevo en un momento.");
        }
    }
}
