using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): el blackjack. Misma lógica que /play game:blackjack y los botones (BlackjackModule).
public partial class TextCommandModule
{
    // "aa bj 10k" reparte una mano apostando 10.000 (también un número, 1m o all). Con una mano en juego: "aa bj hit" pide, "aa bj stand" se planta, "aa bj double" dobla; "aa bj" a secas muestra la mano
    // (o explica cómo empezar). Los botones del mensaje hacen lo mismo: los comandos son para cuando un botón no responde.
    [Command("bj")]
    [Alias("blackjack", "21")]
    [Summary("Blackjack: \"aa bj 10k\" reparte una mano (también un número, 1m o all); después \"aa bj hit\", \"aa bj stand\" o \"aa bj double\", o los botones.")]
    public async Task BlackjackAsync(string accion = "")
    {
        try
        {
            await ReplyBlackjackAsync(await BlackjackCommand(accion));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! Algo falló en la mesa, intentá de nuevo en un momento.");
        }
    }

    private async Task<BlackjackReply> BlackjackCommand(string accion)
    {
        string word = accion.Trim().ToLowerInvariant();
        ulong id = Context.User.Id;

        BlackjackAction? action = word switch
        {
            "hit" or "pedir" or "carta" or "h" => BlackjackAction.Hit,
            "stand" or "plantarse" or "plantar" or "s" => BlackjackAction.Stand,
            "double" or "doblar" or "d" => BlackjackAction.Double,
            _ => null,
        };

        if (action is { } act)
        {
            return await BlackjackModule.ExecuteActAsync(blackjackService, id, null, act);
        }

        if (word.Length == 0)
        {
            return blackjackService.Peek(id) is { } view
                ? new BlackjackReply(null, BlackjackModule.BuildEmbed(view), BlackjackModule.BuildComponents(view, id))
                : new BlackjackReply("Blackjack: empezá con **aa bj <apuesta>** (por ejemplo `aa bj 10k`, `aa bj 1m` o `aa bj all`). Después **pedís**, **te plantás** o **doblás** con los botones, o con `aa bj hit` / `aa bj stand` / `aa bj double`.", null, null);
        }

        return await BlackjackModule.ExecuteStartAsync(blackjackService, id, accion);
    }

    private Task ReplyBlackjackAsync(BlackjackReply reply) => ReplyAsync(reply.PlainMessage, embed: reply.Embed, components: reply.Components);
}
