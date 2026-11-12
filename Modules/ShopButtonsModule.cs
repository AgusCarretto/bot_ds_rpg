using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// Los dos botones de la pregunta de «vender todo» (v0.53.0). NO está dentro de [Group("shop")]: Discord.Net antepone el nombre del grupo al id de los componentes de un grupo y los ids no llegarían.
// Solo los toca quien pidió la pregunta (su id viaja en el custom id); el resultado queda escrito en el MISMO mensaje, sin botones.
public class ShopButtonsModule(IShopRepository shopRepository, IGameEvents gameEvents) : InteractionModuleBase<SocketInteractionContext>
{
    [ComponentInteraction($"{ShopModule.SellAllYesId}:*:*")]
    public async Task HandleSellAllYesAsync(string ownerRaw, string goldRaw)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || !int.TryParse(goldRaw, out int expectedGold))
            {
                await FollowupAsync(Loc.T("No entendí esa confirmación: pedí la venta de nuevo con **/shop sellall**."), ephemeral: true);
                return;
            }

            var result = await ShopModule.ResolveSellAllConfirmAsync(shopRepository, gameEvents, ownerId, Context.User.Id, expectedGold);

            // Otra persona apretó: se le avisa a ella sola y el mensaje de quien pidió la pregunta no se toca.
            if (Context.User.Id != ownerId)
            {
                await FollowupAsync(result.PlainMessage, ephemeral: true);
                return;
            }

            await ModifyOriginalResponseAsync(p =>
            {
                p.Content = result.PlainMessage ?? string.Empty;
                p.Embeds = result.Embed is null ? Array.Empty<Embed>() : [result.Embed];
                p.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude procesar la venta, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    [ComponentInteraction($"{ShopModule.SellAllNoId}:*")]
    public async Task HandleSellAllNoAsync(string ownerRaw)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Esa pregunta es de otra persona: pedí la tuya con **/shop sellall**."), ephemeral: true);
                return;
            }

            await ModifyOriginalResponseAsync(p =>
            {
                p.Content = Loc.T("👍 Listo, no vendí nada.");
                p.Embeds = Array.Empty<Embed>();
                p.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude cancelar eso, intentá de nuevo en un momento."), ephemeral: true);
        }
    }
}
