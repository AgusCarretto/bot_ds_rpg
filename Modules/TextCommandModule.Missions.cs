using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): misiones y logros.
public partial class TextCommandModule
{
    // "aa misiones" muestra tus misiones (con el botón de reclamar); "aa misiones reclamar" cobra lo que esté listo.
    // Misma lógica que /misiones.
    [Command("misiones")]
    [Alias("missions", "mision")]
    [Summary("Tus misiones del día y de la semana: \"aa misiones\" para verlas, \"aa misiones reclamar\" para cobrar.")]
    public async Task MissionsAsync([Remainder] string accion = "")
    {
        try
        {
            if (IsClaimAction(accion))
            {
                var claim = await MissionsModule.ExecuteClaimAsync(
                    userRepository, zoneRepository, missionRepository, gameEvents, Context.User.Id, DateTime.UtcNow);
                await ReplyAsync(claim.PlainMessage, embed: claim.Embed);
                return;
            }

            var view = await MissionsModule.BuildViewAsync(userRepository, zoneRepository, missionRepository, Context.User.Id, DateTime.UtcNow);
            await ReplyAsync(embed: view.Embed, components: view.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude mostrar tus misiones, intentá de nuevo en un momento.");
        }
    }

    // "aa logros" muestra tus logros; "aa logros reclamar" cobra los desbloqueados. Misma lógica que /logros.
    [Command("logros")]
    [Alias("achievements", "logro")]
    [Summary("Tus logros: \"aa logros\" para verlos, \"aa logros reclamar\" para cobrar los que ya desbloqueaste.")]
    public async Task AchievementsAsync([Remainder] string accion = "")
    {
        try
        {
            if (IsClaimAction(accion))
            {
                var claim = await AchievementsModule.ExecuteClaimAsync(
                    userRepository, zoneRepository, gameEventRepository, achievementRepository, gameEvents, Context.User.Id);
                await ReplyAsync(claim.PlainMessage, embed: claim.Embed);
                return;
            }

            var view = await AchievementsModule.BuildViewAsync(gameEventRepository, achievementRepository, Context.User.Id);
            await ReplyAsync(embed: view.Embed, components: view.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude mostrar tus logros, intentá de nuevo en un momento.");
        }
    }

    // "reclamar", "claim", "cobrar"... en cualquier mayúscula; cualquier otra cosa muestra la pantalla.
    private static bool IsClaimAction(string accion) =>
        accion.Trim().ToLowerInvariant() is "reclamar" or "claim" or "cobrar" or "recoger";
}
