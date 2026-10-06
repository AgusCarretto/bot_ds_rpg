using BotDsRpg.GameData;
using Discord.Commands;
using BotDsRpg.Services;

// Tercera parte de TextCommandModule (ver el comentario en TextCommandModule.cs): recolección.
public partial class TextCommandModule
{
    // "aa chop" / "aa ch" — misma lógica que GatheringModule.HandleChopAsync. "aa chop avanzada" (o "adv") es la tala avanzada del oficio Leñador al nivel 100.
    [Command("chop")]
    [Alias("ch")]
    [Summary("Talá madera cercana (cooldown de 5 minutos). \"aa chop avanzada\" con el oficio Leñador al nivel 100.")]
    public Task ChopAsync(string modo = "") => GatherAsync(CooldownCatalog.Chop, itemType: "Madera", modo);

    // "aa mine" / "aa m" — misma lógica que GatheringModule.HandleMineAsync. "aa mine avanzada" es la minería avanzada del oficio Minero al nivel 100.
    [Command("mine")]
    [Alias("m")]
    [Summary("Miná materiales cercanos (cooldown de 5 minutos). \"aa mine avanzada\" con el oficio Minero al nivel 100.")]
    public Task MineAsync(string modo = "") => GatherAsync(CooldownCatalog.Mine, itemType: "Mineral", modo);

    private async Task GatherAsync(CooldownDefinition definition, string itemType, string modo)
    {
        try
        {
            if (GatheringModule.ParseAdvanced(modo) is not bool advanced)
            {
                await ReplyAsync($"No entendí **{modo}**: usá `aa {definition.CommandName}` o `aa {definition.CommandName} avanzada`.");
                return;
            }

            var result = await GatheringModule.ExecuteGatherAsync(
                cooldownRepository, gatheringRepository, userRepository, itemRepository, bonusService, gameEvents, Context.User.Id, definition, itemType, advanced);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! Algo falló procesando la recolección, intentá de nuevo en un momento.");
        }
    }
}
