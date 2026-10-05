using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): el banco.
public partial class TextCommandModule
{
    // "aa bank" (alias "aa banco") — ver la cuenta; "aa bank open"; "aa bank deposit 500" / "aa bank withdraw all". Misma lógica que /bank.
    [Command("bank")]
    [Alias("banco")]
    [Summary("El banco: \"aa bank\" para verlo, \"aa bank open\" para abrir tu cuenta, \"aa bank deposit 500\" y \"aa bank withdraw 500\" (o all).")]
    public async Task BankAsync(string accion = "", string cantidad = "")
    {
        try
        {
            string action = accion.Trim().ToLowerInvariant();
            var result = action switch
            {
                "" or "view" or "ver" => await BankModule.BuildViewAsync(userRepository, Context.User.Id),
                "open" or "abrir" => await BankModule.ExecuteOpenAsync(userRepository, bankRepository, gameEvents, Context.User.Id),
                "deposit" or "depositar" or "dep" => await BankModule.ExecuteDepositAsync(userRepository, bankRepository, gameEvents, Context.User.Id, cantidad),
                "withdraw" or "retirar" or "sacar" => await BankModule.ExecuteWithdrawAsync(userRepository, bankRepository, Context.User.Id, cantidad),
                _ => new BankModule.BankResult("No entendí: probá **aa bank**, **aa bank open**, **aa bank deposit 500** o **aa bank withdraw 500**.", null),
            };

            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude hacer la operación del banco, intentá de nuevo en un momento.");
        }
    }
}
