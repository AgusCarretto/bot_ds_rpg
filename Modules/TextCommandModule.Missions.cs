using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): misiones y logros.
public partial class TextCommandModule
{
    // "aa missions" muestra tus misiones (con el botón de reclamar); "aa missions claim" cobra lo que esté listo.
    // Misma lógica que /missions.
    [Command("missions")]
    [Alias("misiones", "mision")]
    [Summary("Tus misiones del día y de la semana: \"aa missions\" para verlas, \"aa missions claim\" para cobrar.")]
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

            var view = await MissionsModule.BuildViewAsync(userRepository, zoneRepository, missionRepository, zoneBoxService, Context.User.Id, DateTime.UtcNow);
            await ReplyAsync(embed: view.Embed, components: view.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude mostrar tus misiones, intentá de nuevo en un momento.");
        }
    }

    // "aa achievements" muestra tus logros (con el desplegable de páginas); "aa achievements 2" o "aa logros oficios" abre esa página; "aa achievements claim"
    // cobra los desbloqueados. Misma lógica que /achievements.
    [Command("achievements")]
    [Alias("logros", "logro")]
    [Summary("Tus logros: \"aa achievements\" para verlos (o \"aa achievements oficios\" para una página), \"aa achievements claim\" para cobrar los que ya desbloqueaste.")]
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

            var view = await AchievementsModule.BuildViewAsync(
                gameEventRepository, achievementRepository, Context.User.Id, AchievementCatalog.ParsePage(accion) ?? 0);
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
