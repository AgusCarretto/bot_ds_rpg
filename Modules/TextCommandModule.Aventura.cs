using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

public partial class TextCommandModule
{
    // "aa aventura" / "aa adventure" / "aa av" — misma lógica que /aventura (AventuraModule.StartAsync).
    // v0.59.0: con un camino («aa av seguro», «aa av arriesgado», «aa av misterioso», o s / a / m) elige en la escena abierta, como «aa attack» en las peleas
    // (AventuraModule.ChooseByTextAsync, la misma jugada que el botón). El resultado sale en un mensaje nuevo; los botones del viejo ya no valen.
    [Command("aventura")]
    [Alias("adventure", "av")]
    [Summary("Una aventura de 5 escenas (cada 3 horas, sin comida). Elegí el camino escribiendo: \"aa av seguro\", \"aa av arriesgado\" o \"aa av misterioso\".")]
    public async Task AdventureAsync([Remainder] string? camino = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(camino))
            {
                var view = await AventuraModule.StartAsync(adventureDeps, Context.User.Id);
                await ReplyAsync(view.Error, embed: view.Embed, components: view.Components);
                return;
            }

            if (AdventureRules.ParsePath(camino) is not { } path)
            {
                await ReplyAsync(Loc.T("No conozco ese camino. Escribí `aa av seguro`, `aa av arriesgado` o `aa av misterioso` (en la última escena, `aa av guardian`)."));
                return;
            }

            // La pelea arranca ANTES de que exista el mensaje nuevo: el destino se reapunta a ese mensaje apenas se manda (como «aa attack»).
            var target = new SwitchableCombatReplyTarget(NoReplyTarget.Instance);
            var chosen = await AventuraModule.ChooseByTextAsync(adventureDeps, Context.User.Id, path, target);
            if (chosen.Embed is null)
            {
                await ReplyAsync(chosen.Error);
                return;
            }

            var message = await ReplyAsync(embed: chosen.Embed, components: chosen.Components);
            target.Current = new MessageCombatReplyTarget(message);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync(Loc.T("¡Upa! Algo falló en la aventura, intentá de nuevo en un momento."));
        }
    }
}
