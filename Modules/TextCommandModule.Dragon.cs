using BotDsRpg.Services;
using Discord;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): el dragón. Misma lógica que /dragon y /dragon-breed (DragonModule): acá solo se lee lo que escribió el jugador.
public partial class TextCommandModule
{
    // "aa dragon" — mirá tu dragón.
    [Command("dragon")]
    [Alias("dragón", "dragones")]
    [Summary("Mirá tu dragón: su tipo, su tier y lo que te da. Se compra en la taberna: \"aa dragon\".")]
    public async Task DragonAsync()
    {
        try
        {
            var view = await DragonModule.BuildDragonViewAsync(dragonRepository, Context.User.Id);
            await ReplyAsync(embed: view.Embed, components: view.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude mirar tu dragón, intentá de nuevo en un momento.");
        }
    }

    // "aa dragon train [veces]" — entrená a tu dragón con oro: cada nivel suma al bono de su tipo y sale más caro.
    [Command("dragon train")]
    [Alias("dragon entrenar", "dragón entrenar")]
    [Summary("Entrená a tu dragón con oro (cada nivel sale más caro): \"aa dragon train [veces]\".")]
    public async Task DragonTrainAsync(int times = 1)
    {
        try
        {
            await ReplyAsync(await DragonModule.ExecuteTrainAsync(dragonRepository, Context.User.Id, times));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude entrenar a tu dragón, intentá de nuevo en un momento.");
        }
    }

    // "aa dragon breed @amigo" — invitá a criar tu dragón con el de un amigo del mismo tier.
    [Command("dragon breed")]
    [Alias("dragon criar", "dragón criar")]
    [Summary("Criá tu dragón con el de un amigo que tenga tu mismo tier: \"aa dragon breed @amigo\".")]
    public async Task DragonBreedAsync(IUser friend)
    {
        try
        {
            var invite = await DragonModule.PrepareBreedAsync(dragonRepository, dragonOffers, Context.User.Id, friend.Id, friend.IsBot);
            if (invite.Error is not null)
            {
                await ReplyAsync(invite.Error);
                return;
            }

            await ReplyAsync(invite.Content, embed: invite.Embed, components: invite.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude armar la invitación, intentá de nuevo en un momento.");
        }
    }
}
