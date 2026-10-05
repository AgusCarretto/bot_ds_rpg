using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /bank: el banco. Se COMPRA la cuenta una sola vez (GameData/BankRules.cs) y después se deposita y se retira oro; lo que está en el banco no lo toca la
// penalidad por muerte (GameData/DeathPenalty.cs): ese es todo su valor. Sin interés ni tope por ahora. Las reglas viven en los métodos estáticos de abajo
// (sin Context) para que "aa bank" haga exactamente lo mismo; cada operación de dinero es un UPDATE guardado en IBankRepository.
[Group("bank", "Tu cuenta del banco: guardá oro a salvo de la penalidad por morir.")]
public class BankModule(IUserRepository userRepository, IBankRepository bankRepository, IGameEvents gameEvents)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("view", "Mirá tu billetera y lo que tenés en el banco.")]
    public Task HandleViewAsync() => RunAsync(() => BuildViewAsync(userRepository, Context.User.Id));

    [SlashCommand("open", "Abrí tu cuenta del banco (se paga una sola vez).")]
    public Task HandleOpenAsync() => RunAsync(() => ExecuteOpenAsync(userRepository, bankRepository, gameEvents, Context.User.Id));

    [SlashCommand("deposit", "Guardá oro en el banco.")]
    public Task HandleDepositAsync([Summary("cantidad", "Cuánto guardás, o all para todo el oro que llevás.")] string amount) =>
        RunAsync(() => ExecuteDepositAsync(userRepository, bankRepository, gameEvents, Context.User.Id, amount));

    [SlashCommand("withdraw", "Sacá oro del banco.")]
    public Task HandleWithdrawAsync([Summary("cantidad", "Cuánto sacás, o all para todo lo que hay en el banco.")] string amount) =>
        RunAsync(() => ExecuteWithdrawAsync(userRepository, bankRepository, Context.User.Id, amount));

    private async Task RunAsync(Func<Task<BankResult>> action)
    {
        await DeferAsync();

        try
        {
            var result = await action();
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: result.Embed is null);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude hacer la operación del banco, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Exactamente uno de los dos campos viene con valor (mismo patrón que GiveModule.GiveResult).
    public sealed record BankResult(string? PlainMessage, Embed? Embed);

    private const string NoAccountText = "Todavía no tenés cuenta en el banco: abrila con **/bank open** (cuesta {0} de oro, una sola vez).";

    public static async Task<BankResult> BuildViewAsync(IUserRepository userRepository, ulong discordId)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new BankResult("Todavía no tenés cuenta: empezá con **/start**.", null);
        }

        var embed = new EmbedBuilder().WithTitle("🏦 El Banco").WithColor(Color.Teal);

        if (!player.HasBank)
        {
            embed.WithDescription(
                $"Todavía no tenés cuenta. Abrirla cuesta **{GameHistory.Number(BankRules.AccountPrice)}** de oro (una sola vez) y guarda tu oro **a salvo**: " +
                "si perdés un combate, lo que está en el banco no se toca.\n\nAbrila con **/bank open**.")
                .AddField("💰 Tu billetera", GameHistory.Number(player.Gold), true);
            return new BankResult(null, embed.Build());
        }

        embed.WithDescription(
                "Lo que guardás acá **no lo toca la penalidad por morir** (la billetera sí: pierde el 5 % cada vez que perdés un combate).\n\n" +
                "Guardá con **/bank deposit** y sacá con **/bank withdraw** (podés escribir **all**).")
            .AddField("💰 Billetera", GameHistory.Number(player.Gold), true)
            .AddField("🏦 En el banco", GameHistory.Number(player.BankGold), true);
        return new BankResult(null, embed.Build());
    }

    public static async Task<BankResult> ExecuteOpenAsync(IUserRepository userRepository, IBankRepository bankRepository, IGameEvents gameEvents, ulong discordId)
    {
        await userRepository.GetOrCreateUserAsync(discordId);
        var outcome = await bankRepository.OpenAccountAsync(discordId, BankRules.AccountPrice);

        switch (outcome.Status)
        {
            case BankStatus.AlreadyHasAccount:
                return new BankResult("Ya tenés tu cuenta del banco: mirala con **/bank view**.", null);
            case BankStatus.NotEnoughGold:
                return new BankResult($"No te alcanza el oro: abrir la cuenta cuesta **{GameHistory.Number(BankRules.AccountPrice)}**.", null);
            case not BankStatus.Ok:
                return new BankResult("Todavía no tenés cuenta de jugador: empezá con **/start**.", null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.BankOpened);

        var embed = new EmbedBuilder()
            .WithTitle("🏦 ¡Cuenta abierta!")
            .WithColor(Color.Green)
            .WithDescription($"Pagaste **{GameHistory.Number(BankRules.AccountPrice)}** de oro. Desde ahora lo que guardes en el banco está a salvo de la penalidad por morir.\n\nProbá con **/bank deposit**.")
            .AddField("💰 Billetera", GameHistory.Number(outcome.User!.Gold), true)
            .AddField("🏦 En el banco", GameHistory.Number(outcome.User.BankGold), true);
        return new BankResult(null, embed.Build());
    }

    public static async Task<BankResult> ExecuteDepositAsync(
        IUserRepository userRepository, IBankRepository bankRepository, IGameEvents gameEvents, ulong discordId, string amountText)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new BankResult("Todavía no tenés cuenta de jugador: empezá con **/start**.", null);
        }

        if (!player.HasBank)
        {
            return new BankResult(string.Format(NoAccountText, GameHistory.Number(BankRules.AccountPrice)), null);
        }

        var parsed = AmountParser.Parse(amountText, player.Gold);
        if (!parsed.Ok)
        {
            return new BankResult(parsed.Error, null);
        }

        var outcome = await bankRepository.DepositAsync(discordId, parsed.Amount);
        if (outcome.Status != BankStatus.Ok)
        {
            return new BankResult(outcome.Status == BankStatus.NotEnoughGold
                ? $"No tenés tanto oro en la billetera: tenés **{GameHistory.Number(player.Gold)}**."
                : string.Format(NoAccountText, GameHistory.Number(BankRules.AccountPrice)), null);
        }

        await gameEvents.RecordAsync(discordId, GameEventKinds.BankDeposit, amount: parsed.Amount);
        return new BankResult(null, MovedEmbed("🏦 Guardaste oro", $"Guardaste **{GameHistory.Number(parsed.Amount)}** de oro en el banco.", outcome.User!));
    }

    public static async Task<BankResult> ExecuteWithdrawAsync(
        IUserRepository userRepository, IBankRepository bankRepository, ulong discordId, string amountText)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new BankResult("Todavía no tenés cuenta de jugador: empezá con **/start**.", null);
        }

        if (!player.HasBank)
        {
            return new BankResult(string.Format(NoAccountText, GameHistory.Number(BankRules.AccountPrice)), null);
        }

        var parsed = AmountParser.Parse(amountText, player.BankGold);
        if (!parsed.Ok)
        {
            return new BankResult(parsed.IsAll ? "No tenés oro en el banco para sacar." : parsed.Error, null);
        }

        var outcome = await bankRepository.WithdrawAsync(discordId, parsed.Amount);
        if (outcome.Status != BankStatus.Ok)
        {
            return new BankResult(outcome.Status == BankStatus.NotEnoughBank
                ? $"No tenés tanto en el banco: tenés **{GameHistory.Number(player.BankGold)}**."
                : string.Format(NoAccountText, GameHistory.Number(BankRules.AccountPrice)), null);
        }

        return new BankResult(null, MovedEmbed("🏦 Sacaste oro", $"Sacaste **{GameHistory.Number(parsed.Amount)}** de oro del banco.", outcome.User!));
    }

    // Público y puro: se prueba sin Discord.
    public static Embed MovedEmbed(string title, string text, BotDsRpg.Models.User after) =>
        new EmbedBuilder()
            .WithTitle(title)
            .WithColor(Color.Teal)
            .WithDescription(text)
            .AddField("💰 Billetera", GameHistory.Number(after.Gold), true)
            .AddField("🏦 En el banco", GameHistory.Number(after.BankGold), true)
            .Build();
}
