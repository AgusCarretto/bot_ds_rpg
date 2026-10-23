using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): el jefe mundial. Misma lógica que /miniboss (WorldBossModule).
public partial class TextCommandModule
{
    // "aa miniboss" te anota contra el jefe mundial de hoy (gasta tu cooldown del jefe); si ya estás anotado, te dice cómo viene.
    [Command("miniboss")]
    [Alias("worldboss", "wb", "jefemundial")]
    [Summary("Anotate al jefe mundial de hoy: se pelea entre todos a las 00:00 y cada uno cobra de 30 a 50 cacerías de oro de su zona (gasta tu cooldown del jefe).")]
    public async Task MinibossAsync()
    {
        try
        {
            await ReplyAsync(embed: await WorldBossModule.ExecuteJoinAsync(worldBossService, Context.User, Context.Channel.Id, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude anotarte contra el jefe mundial, intentá de nuevo en un momento.");
        }
    }
}
