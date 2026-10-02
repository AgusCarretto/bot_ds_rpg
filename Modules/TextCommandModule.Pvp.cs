using BotDsRpg.Services;
using Discord;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): PvP, el duelo amistoso y la Arena.
public partial class TextCommandModule
{
    // "aa fight @jugador" — misma lógica que /fight. Los botones del desafío y del duelo son los mismos (los clicks llegan como interacciones).
    [Command("fight")]
    [Alias("duel", "duelo", "pelear")]
    [Summary("Desafiá a otro jugador a un duelo amistoso (no se gana ni se pierde nada): \"aa fight @jugador\".")]
    public async Task FightAsync(IUser player)
    {
        try
        {
            var result = await DuelModule.ExecuteChallengeAsync(userRepository, duelService, Context.User, player);
            await ReplyAsync(result.PlainMessage ?? result.Content, embed: result.Embed, components: result.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude armar el desafío, intentá de nuevo en un momento.");
        }
    }

    // "aa arena join" — misma lógica que /arena join.
    [Command("arena join")]
    [Summary("Anotate en el torneo de hoy de la Arena (se juega a las 00:00, hora de Uruguay): \"aa arena join\".")]
    public async Task ArenaJoinAsync()
    {
        try
        {
            await ReplyAsync(embed: await ArenaModule.ExecuteJoinAsync(arenaService, Context.User, Context.Channel.Id, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude anotarte en la Arena, intentá de nuevo en un momento.");
        }
    }

    // "aa arena listplayers" — misma lógica que /arena listplayers.
    [Command("arena listplayers")]
    [Alias("arena players", "arena list", "arena")]
    [Summary("Mirá quiénes están anotados en el torneo de hoy: \"aa arena listplayers\".")]
    public async Task ArenaListAsync()
    {
        try
        {
            await ReplyAsync(embed: await ArenaModule.ExecuteListAsync(arenaService, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude armar la lista de la Arena, intentá de nuevo en un momento.");
        }
    }

    // "aa arena results" — misma lógica que /arena results.
    [Command("arena results")]
    [Alias("arena resultados")]
    [Summary("Mirá la llave y el campeón del último torneo de la Arena: \"aa arena results\".")]
    public async Task ArenaResultsAsync()
    {
        try
        {
            await ReplyAsync(embed: await ArenaModule.ExecuteResultsAsync(arenaService));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude traer los resultados de la Arena, intentá de nuevo en un momento.");
        }
    }
}
