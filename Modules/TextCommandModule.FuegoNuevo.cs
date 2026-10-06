using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): el Fuego Nuevo y las bendiciones (v0.12.0). Los botones de las pantallas son los mismos de /fuegonuevo
// (sus ids llevan el dueño y los atiende FuegoNuevoModule), así que "aa fuegonuevo" y "/fuegonuevo" hacen exactamente lo mismo.
public partial class TextCommandModule
{
    // "aa fuegonuevo" / "aa fn" — misma lógica que FuegoNuevoModule.HandleFuegoNuevoAsync.
    [Command("fuegonuevo")]
    [Alias("fn", "renacer")]
    [Summary("Fuego Nuevo: volvé a empezar con bonus permanentes y una bendición nueva (se habilita al vencer al Asador Eterno).")]
    public Task FuegoNuevoAsync() =>
        RunFuegoNuevoAsync(() => FuegoNuevoModule.ExecuteStatusAsync(fuegoNuevoRepository, blessingRepository, Context.User.Id));

    // "aa bendiciones" / "aa blessings" — misma lógica que FuegoNuevoModule.HandleBlessingsAsync.
    [Command("bendiciones")]
    [Alias("blessings", "bendicion")]
    [Summary("Tus bendiciones (una por cada Fuego Nuevo) y la que tengas para elegir.")]
    public Task BlessingsAsync() =>
        RunFuegoNuevoAsync(() => FuegoNuevoModule.ExecuteBlessingsAsync(blessingRepository, Context.User.Id));

    private async Task RunFuegoNuevoAsync(Func<Task<FuegoNuevoModule.FnResult>> action)
    {
        try
        {
            var result = await action();
            await ReplyAsync(result.PlainMessage, embed: result.Embed, components: result.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude abrir la pantalla del Fuego Nuevo, intentá de nuevo en un momento.");
        }
    }
}
