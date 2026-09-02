using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;

// Segunda mitad de ShopModule (ver el comentario en ShopModule.cs): acá van las consultas de
// solo lectura del grupo "shop", separadas de las acciones (buy/sell/sellall) por claridad,
// sin duplicar el atributo [Group] ni el constructor primario (ya declarados en ShopModule.cs;
// itemRepository queda accesible acá también porque es la misma clase de C#).
public partial class ShopModule
{
    // Comando barra: /shop view
    [SlashCommand("view", "Mostrá el catálogo de consumibles en venta.")]
    public async Task HandleViewAsync()
    {
        await DeferAsync();

        try
        {
            var items = await itemRepository.GetAllByTypeAsync("Consumable");

            var embed = new EmbedBuilder()
                .WithTitle("🏪 Tienda de Consumibles")
                .WithColor(Color.Gold);

            if (items.Count == 0)
            {
                embed.WithDescription("No hay consumibles cargados en la tienda todavía.");
            }
            else
            {
                foreach (var item in items.OrderBy(i => RarityCatalog.RankOf(i.Rarity)).ThenBy(i => i.Name))
                {
                    embed.AddField(
                        $"[{item.Rarity}] {item.Name}",
                        $"❤️ Cura: {item.StatValue} HP | 💰 Compra: {item.BuyPrice} Oro | 💸 Venta: {item.SellPrice} Oro");
                }
            }

            await FollowupAsync(embed: embed.Build());
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("No pude cargar la tienda ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }
}
