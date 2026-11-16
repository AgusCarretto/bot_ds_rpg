using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

public partial class TextCommandModule
{
    // "aa aventura" / "aa adventure" / "aa av" — misma lógica que /aventura (AventuraModule.StartAsync). Los caminos se eligen con los botones del mensaje.
    [Command("aventura")]
    [Alias("adventure", "av")]
    [Summary("Una aventura de 5 escenas: elegí caminos, peleá y llegá al Guardián del Camino (cada 3 horas, sin comida).")]
    public async Task AdventureAsync()
    {
        try
        {
            var view = await AventuraModule.StartAsync(adventureDeps, Context.User.Id);
            await ReplyAsync(view.Error, embed: view.Embed, components: view.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude arrancar la aventura, intentá de nuevo en un momento."));
        }
    }
}
