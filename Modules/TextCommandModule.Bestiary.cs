using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

public partial class TextCommandModule
{
    // "aa bestiario [zona]" — misma lógica que /bestiary (BestiaryModule): abre en la Zona 1 (o en la pedida) y los menús cambian de zona.
    [Command("bestiario")]
    [Alias("bestiary", "bestias", "bes")]
    [Summary("Tu bestiario: los monstruos de cada zona que ya venciste (y los que te faltan). \"aa bestiario 3\" abre la Zona 3.")]
    public async Task BestiaryAsync(int? zona = null)
    {
        try
        {
            var view = await BestiaryModule.BuildAsync(userRepository, zoneRepository, monsterRepository, bestiaryRepository, Context.User.Id, zona);
            await ReplyAsync(view.Error, embed: view.Embed, components: view.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude abrir el bestiario, intentá de nuevo en un momento."));
        }
    }
}
