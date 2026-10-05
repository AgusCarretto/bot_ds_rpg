using BotDsRpg.GameData;
using Discord;

public static class ItemEmbedExtensions
{
    // Pone la imagen del emoji del ítem como miniatura del embed (ver ItemDisplay.ImageUrl). Si el ítem no tiene emoji todavía, no toca nada.
    public static EmbedBuilder WithItemThumbnail(this EmbedBuilder embed, string? itemEmoji)
    {
        string? url = ItemDisplay.ImageUrl(itemEmoji);
        return url is null ? embed : embed.WithThumbnailUrl(url);
    }

    // Lo mismo para la CARA de un monstruo (monsters.portrait_emoji, ver Database/update_monster_portraits.sql): miniatura de los mensajes de combate.
    // Es la misma mecánica que la del ítem (la imagen del emoji de la aplicación en el CDN); un monstruo sin cara deja el mensaje como estaba.
    public static EmbedBuilder WithMonsterPortrait(this EmbedBuilder embed, string? portraitEmoji) => embed.WithItemThumbnail(portraitEmoji);
}
