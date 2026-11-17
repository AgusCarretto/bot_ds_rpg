using BotDsRpg.GameData;
using Discord;

// Las pantallas de la Navidad se arman con el emoji unicode y, al final, BuildChristmas pinta el arte propio (GameData/ChristmasEmojis.cs) en la descripción y en el valor de
// cada campo: lo único donde Discord dibuja un emoji de la aplicación. Título, nombres de campo y pie quedan como estaban.
public static class ChristmasEmbedExtensions
{
    public static Embed BuildChristmas(this EmbedBuilder embed)
    {
        embed.Description = ChristmasEmojis.Paint(embed.Description);
        foreach (var field in embed.Fields)
        {
            field.Value = ChristmasEmojis.Paint(field.Value?.ToString());
        }

        return embed.Build();
    }
}
