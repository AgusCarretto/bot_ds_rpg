using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): la ficha de stats (v0.49.0). La lógica está en StatsModule (la comparte /stats).
public partial class TextCommandModule
{
    // "aa stats" — tu ataque, defensa y vida con cuánto aporta cada cosa (equipo, encantamientos, platos, dragón, mascotas, bendiciones). «aa equipamiento» ya es el atajo del Carpintero.
    // De OTRO jugador (v0.53.0): "aa stats @jugador" (mención, ID o nombre exacto) o respondiendo su mensaje con "aa stats" (ver Services/ReplyUserInjection.cs); "aa stats mas" abre la segunda pantalla, también con jugador.
    [Command("stats")]
    [Alias("estadisticas", "ficha")]
    [AcceptsReplyUser]
    [Summary("Tu ataque, defensa y vida con cuánto aporta cada cosa (equipo, platos, dragón...); \"aa stats @jugador\", o respondiendo su mensaje, las de otro.")]
    public async Task StatsAsync(string first = "", [Remainder] string rest = "")
    {
        try
        {
            var (page, who) = StatsModule.ParseTextArgs(first, rest);

            IGuildUser? target = null;
            if (who is not null)
            {
                target = Context.Guild is null ? null : await GuildUserTypeReader.ResolveAsync(Context.Guild, who);
                if (target is null)
                {
                    await ReplyAsync(GuildUserTypeReader.NotFoundMessage());
                    return;
                }
            }

            // Mirarse a uno mismo («aa stats @yo») es el «aa stats» de siempre.
            var reply = target is not null && target.Id != Context.User.Id
                ? await StatsModule.BuildStatsAsync(userRepository, itemRepository, buffRepository, bonusService, cookingRepository, dragonRepository, target.Id, zoneBoxService, page, GameModule.GetDisplayName(target), Context.User.Id)
                : await StatsModule.BuildStatsAsync(userRepository, itemRepository, buffRepository, bonusService, cookingRepository, dragonRepository, Context.User.Id, zoneBoxService, page);
            await ReplyAsync(reply.PlainMessage, embed: reply.Embed, components: reply.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! No pude armar tus stats ahora mismo, intentá de nuevo en un momento."));
        }
    }
}
