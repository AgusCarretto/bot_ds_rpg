using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): el blackjack. Misma lógica que /play game:blackjack y los botones (BlackjackModule).
public partial class TextCommandModule
{
    // "aa bj 10k" reparte una mano apostando 10.000 (también un número, 1m o all). Con una mano en juego: "aa bj hit" pide y "aa bj stand" se planta ("aa bj double" avisa que ya no se dobla, v0.64.1); "aa bj" a secas muestra la mano
    // (o explica cómo empezar). Los botones del mensaje hacen lo mismo: los comandos son para cuando un botón no responde.
    [Command("bj")]
    [Alias("blackjack", "21")]
    [Summary("Blackjack: \"aa bj 10k\" reparte una mano (también un número, 1m o all); después \"aa bj hit\" o \"aa bj stand\", o los botones.")]
    public async Task BlackjackAsync(string accion = "")
    {
        try
        {
            await ReplyBlackjackAsync(await BlackjackCommand(accion));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! Algo falló en la mesa, intentá de nuevo en un momento."));
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
            _ => null,
        };

        // Doblar se sacó en la v0.64.1 (el dueño: «nadie lo usa»): quien lo escriba de costumbre recibe el aviso, no un «esa apuesta no es un número».
        if (word is "double" or "doblar" or "d")
        {
            return new BlackjackReply(Loc.T("En el blackjack ya no se dobla: **pedí** otra carta (`aa bj hit`) o **plantate** (`aa bj stand`)."), null, null);
        }

        if (action is { } act)
        {
            return await BlackjackModule.ExecuteActAsync(blackjackService, id, null, act);
        }

        if (word.Length == 0)
        {
            return blackjackService.Peek(id) is { } view
                ? new BlackjackReply(null, BlackjackModule.BuildEmbed(view), BlackjackModule.BuildComponents(view, id))
                : new BlackjackReply(Loc.T("Blackjack: empezá con **aa bj <apuesta>** (por ejemplo `aa bj 10k`, `aa bj 1m` o `aa bj all`). Después **pedís** o **te plantás** con los botones, o con `aa bj hit` / `aa bj stand`."), null, null);
        }

        return await BlackjackModule.ExecuteStartAsync(blackjackService, id, accion);
    }

    private Task ReplyBlackjackAsync(BlackjackReply reply) => ReplyAsync(reply.PlainMessage, embed: reply.Embed, components: reply.Components);
}
