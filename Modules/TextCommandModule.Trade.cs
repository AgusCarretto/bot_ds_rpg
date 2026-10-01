using BotDsRpg.Services;
using Discord;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): cambiar materiales con otro jugador.
public partial class TextCommandModule
{
    // aa trade @jugador "Madera de Roble" Hierro — misma lógica que /trade. Los nombres con espacios van entre comillas.
    [Command("trade")]
    [Alias("cambiar")]
    [Summary("Cambiá 1 material por 1 de la misma rareza con otro jugador: aa trade @jugador \"Madera de Roble\" Hierro (los nombres con espacios, entre comillas).")]
    public async Task TradeAsync(IUser player, string give, string get)
    {
        try
        {
            var result = await TradeModule.ExecuteOfferAsync(
                userRepository, itemRepository, inventoryRepository, tradeOffers, combatSessions, Context.User.Id, player, give, get);
            await ReplyAsync(result.PlainMessage ?? result.Content, embed: result.Embed, components: result.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude armar el cambio, intentá de nuevo en un momento.");
        }
    }
}
