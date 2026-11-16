using BotDsRpg.GameData;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

// Mantiene al día GameData/ItemEmojiRegistry (el emoji de cada ítem por nombre) leyendo el catálogo: una vez al arrancar y cada 10 minutos, para que un apply_emojis.sql nuevo (que es solo
// datos y se corre con el bot andando) se vea en las pantallas sin reiniciar. Nunca tira: si la base no responde queda la tabla que ya había (y se registra).
public sealed class ItemEmojiRefresher(IItemRepository itemRepository)
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(10);

    public async Task RefreshAsync()
    {
        try
        {
            // El catálogo ENTERO (v0.57.1): hasta acá era una lista fija de tipos y se quedaban afuera la Reliquia, el Hacha, el Pico y la Caña (el dueño: «al craftear una
            // reliquia no aparece el dibujito en la opción»). En el texto se veían igual (el emoji sale del ítem), pero los desplegables preguntan a esta tabla.
            var all = (await itemRepository.GetAllAsync()).Select(item => (item.Name, item.Emoji)).ToList();
            ItemEmojiRegistry.Set(all);
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
        }
    }

    public void Start() =>
        _ = Task.Run(async () =>
        {
            await RefreshAsync();

            using var timer = new PeriodicTimer(Every);
            while (await timer.WaitForNextTickAsync())
            {
                await RefreshAsync();
            }
        });
}
