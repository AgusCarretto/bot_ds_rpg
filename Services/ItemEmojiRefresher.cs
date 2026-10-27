using BotDsRpg.GameData;
using BotDsRpg.Repositories;

namespace BotDsRpg.Services;

// Mantiene al día GameData/ItemEmojiRegistry (el emoji de cada ítem por nombre) leyendo el catálogo: una vez al arrancar y cada 10 minutos, para que un apply_emojis.sql nuevo (que es solo
// datos y se corre con el bot andando) se vea en las pantallas sin reiniciar. Nunca tira: si la base no responde queda la tabla que ya había (y se registra).
public sealed class ItemEmojiRefresher(IItemRepository itemRepository)
{
    // Todos los tipos de ítem del catálogo (items.type).
    private static readonly string[] Types = ["Weapon", "Amulet", "Consumable", "Madera", "Mineral", "Material", "Caja", "Huevo", "PetFood", "DragonFood"];

    public static readonly TimeSpan Every = TimeSpan.FromMinutes(10);

    public async Task RefreshAsync()
    {
        try
        {
            var all = new List<(string Name, string? Emoji)>();
            foreach (string type in Types)
            {
                foreach (var item in await itemRepository.GetAllByTypeAsync(type))
                {
                    all.Add((item.Name, item.Emoji));
                }
            }

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
