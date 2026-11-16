using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): cambiar materiales con otro jugador.
public partial class TextCommandModule
{
    // aa trade @jugador "Madera de Roble" Hierro 10 — misma lógica que /trade. Los nombres con espacios van entre comillas; la cantidad (al final) es opcional.
    [Command("trade")]
    [Alias("cambiar")]
    [Summary("Cambiá materiales de la misma rareza con otro jugador, la misma cantidad de cada lado: aa trade @jugador \"Madera de Roble\" Hierro 10 (los nombres con espacios, entre comillas; sin cantidad, 1).")]
    public async Task TradeAsync(IUser player, string give, string get, int cantidad = 1)
    {
        try
        {
            var result = await TradeModule.ExecuteOfferAsync(
                userRepository, itemRepository, inventoryRepository, tradeOffers, combatSessions, Context.User.Id, player, give, get, cantidad);
            await ReplyAsync(result.PlainMessage ?? result.Content, embed: result.Embed, components: result.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude armar el cambio, intentá de nuevo en un momento."));
        }
    }
}
