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
}
