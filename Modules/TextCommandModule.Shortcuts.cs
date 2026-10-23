using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): los atajos que no pasan por «shop», «bank» ni «play» (v0.23.1). Misma lógica que los comandos largos: acá solo se lee lo que escribió el jugador
// (ver Modules/TextShortcuts.cs). Los comandos largos ("aa shop sell", "aa bank deposit", "aa play coinflip") siguen andando.
public partial class TextCommandModule
{
    // "aa sell all" vende todo lo vendible de la mochila; "aa sell arma" / "aa sell amuleto" venden lo que llevás puesto; "aa sell <ítem>" y "aa sell <cantidad> <ítem>" venden por nombre (la cantidad puede ser 10k o all).
    [Command("sell")]
    [Alias("vender")]
    [Summary("Vendé sin pasar por la tienda: \"aa sell all\", \"aa sell arma\", \"aa sell amuleto\", \"aa sell <ítem>\" o \"aa sell <cantidad> <ítem>\".")]
    public async Task SellAsync([Remainder] string args = "")
    {
        try
        {
            var result = await TextShortcuts.ExecuteSellAsync(userRepository, itemRepository, inventoryRepository, shopRepository, gameEvents, Context.User.Id, args);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude procesar la venta, intentá de nuevo en un momento.");
        }
    }

    // "aa deposit 500" / "aa deposit 10k" / "aa deposit all": lo mismo que "aa bank deposit".
    [Command("deposit")]
    [Alias("depositar", "dep")]
    [Summary("Guardá oro en el banco sin escribir \"bank\": \"aa deposit 500\", \"aa deposit 10k\" o \"aa deposit all\".")]
    public async Task DepositAsync(string cantidad = "")
    {
        try
        {
            var result = await BankModule.ExecuteDepositAsync(userRepository, bankRepository, gameEvents, Context.User.Id, cantidad);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude hacer el depósito, intentá de nuevo en un momento.");
        }
    }

    // "aa withdraw 500" / "aa withdraw 1m" / "aa withdraw all": lo mismo que "aa bank withdraw".
    [Command("withdraw")]
    [Alias("retirar", "sacar", "wd")]
    [Summary("Sacá oro del banco sin escribir \"bank\": \"aa withdraw 500\", \"aa withdraw 1m\" o \"aa withdraw all\".")]
    public async Task WithdrawAsync(string cantidad = "")
    {
        try
        {
            var result = await BankModule.ExecuteWithdrawAsync(userRepository, bankRepository, Context.User.Id, cantidad);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude hacer el retiro, intentá de nuevo en un momento.");
        }
    }

    // "aa cf h 100" / "aa cf t 10k" / "aa cf 100 h": cara o cruz. h = heads, t = tails (también heads/tails/cara/cruz), en cualquier orden con la apuesta.
    [Command("cf")]
    [Alias("coinflip", "flip", "moneda")]
    [Summary("Cara o cruz: \"aa cf h 100\" (h = heads, t = tails). La apuesta puede ser un número, 10k, 1m o all.")]
    public async Task CoinflipAsync(string primero = "", string segundo = "")
    {
        try
        {
            if (TextShortcuts.ParseCoinflip(primero, segundo) is not { } request)
            {
                await ReplyAsync(TextShortcuts.CoinflipHelp);
                return;
            }

            var result = await CasinoModule.ExecutePlayAsync(userRepository, casinoRepository, casinoService, Context.User.Id, "coinflip", request.Amount, request.Side, gameEvents);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! Algo falló en el casino, intentá de nuevo en un momento.");
        }
    }

    // "aa slots 100" / "aa sl 10k": la tragamonedas sin escribir "play".
    [Command("slots")]
    [Alias("sl", "tragamonedas")]
    [Summary("Tragamonedas sin escribir \"play\": \"aa slots 100\", \"aa slots 10k\" o \"aa slots all\".")]
    public async Task SlotsAsync(string apuesta = "")
    {
        try
        {
            var result = await CasinoModule.ExecutePlayAsync(userRepository, casinoRepository, casinoService, Context.User.Id, "slots", apuesta, null, gameEvents);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! Algo falló en el casino, intentá de nuevo en un momento.");
        }
    }
}
