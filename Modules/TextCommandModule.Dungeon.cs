using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord;
using Discord.Commands;

// Las mazmorras por texto (v0.47.0): «aa boss» en una zona de mazmorra la abre (TextCommandModule.Combat.cs), y «aa d <acción>» juega una jugada cuando un botón no responde
// (la misma lógica que el botón: Modules/DungeonModule.cs). El mensaje nuevo trae botones nuevos.
public partial class TextCommandModule
{
    [Command("dungeon")]
    [Alias("d", "mazmorra", "dg")]
    [Summary("Jugá una jugada de la mazmorra escribiéndola: \"aa d agua\", \"aa d arriba\", \"aa d b3\", \"aa d probar\" (por si un botón no responde).")]
    public async Task DungeonAsync([Remainder] string? action = null)
    {
        try
        {
            var (problem, result) = await DungeonModule.ExecuteTypedAsync(dungeonService, Context.User.Id, action);
            if (problem is not null || result?.Session is null)
            {
                await ReplyAsync(problem ?? Loc.T("No estás en ninguna mazmorra."));
                return;
            }

            var (embed, components) = await DungeonModule.RenderAsync(zoneRepository, result);
            var message = await ReplyAsync(embed: embed, components: components);
            if (result.End == DungeonEnd.None)
            {
                AdventureModule.AttachDungeonTimeout(result.Session, message, zoneRepository);
            }

            if (result.Asador is { } fight)
            {
                await DungeonModule.StartAsadorAsync(combatSessions, inventoryRepository, buffRepository, Context.User.Id, fight, (e, c) => ReplyAsync(embed: e, components: c));
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! Algo falló en la mazmorra, probá de nuevo en un momento."));
        }
    }

    private async Task StartDungeonTypedAsync(bool withCompanions)
    {
        try
        {
            if (withCompanions)
            {
                await ReplyAsync(Loc.T("La mazmorra se hace **solo**: usá `aa boss` sin compañeros."));
                return;
            }

            var (problem, embed, components, session) = await DungeonModule.ExecuteStartAsync(dungeonService, Context.User.Id, Context.Channel.Id);
            if (problem is not null)
            {
                await ReplyAsync(problem);
                return;
            }

            var message = await ReplyAsync(embed: embed, components: components);
            AdventureModule.AttachDungeonTimeout(session!, message, zoneRepository);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude abrir la mazmorra, probá de nuevo en un momento."));
        }
    }
}
