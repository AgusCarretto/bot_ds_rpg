using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// La pantalla de las Crónicas del Fogón: el índice y la lectura de cada capítulo o escena que el jugador ya abrió (GameData/Lore.cs tiene los textos y las reglas).
public sealed record StoryView(Embed Embed, MessageComponent? Components);

// /story (aa historia): un mensaje con el índice y una lista desplegable para leer lo que ya abriste. Lo que todavía no viste sale "🔒 sellado" y no cuenta nada (ni el título), así nadie se
// spoilea. Es solo para mirar: no cuesta nada ni toca la base (qué abriste sale de tu progreso: zona más alta vencida, haber vencido al Asador y tus Fuegos Nuevos; ver StoryProgress).
// El id del desplegable lleva a quién pertenece, como el resto de las escenas: nadie lee con el mensaje de otro.
public class StoryModule(IUserRepository userRepository, IZoneRepository zoneRepository) : InteractionModuleBase<SocketInteractionContext>
{
    private const string MenuPrefix = "story_pick";
    private const string IndexKey = "indice";

    [SlashCommand("story", "Las Crónicas del Fogón: la historia que te cuenta el Tabernero, capítulo por capítulo.")]
    public async Task HandleStoryAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var view = await BuildViewAsync(userRepository, zoneRepository, Context.User.Id, null);
            await FollowupAsync(embed: view.Embed, components: view.Components, ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! El Tabernero no encuentra sus crónicas ahora, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction($"{MenuPrefix}:*")]
    public async Task HandlePickAsync(string ownerRaw, string[] selected)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Esa charla es de otra persona: escuchá al Tabernero con **/story**.", ephemeral: true);
                return;
            }

            var view = await BuildViewAsync(userRepository, zoneRepository, Context.User.Id, selected.FirstOrDefault());
            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = view.Embed;
                p.Components = view.Components ?? new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude abrir ese capítulo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // La pantalla para ese jugador: el índice (key null o "indice") o la lectura de un capítulo o escena. Una clave que no existe, o que todavía está sellada, vuelve al índice (nunca
    // se lee algo sin abrir aunque alguien arme el id a mano). Pública y sin Context para que "aa historia" muestre exactamente lo mismo.
    public static async Task<StoryView> BuildViewAsync(IUserRepository userRepository, IZoneRepository zoneRepository, ulong discordId, string? key)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new StoryView(
                new EmbedBuilder().WithTitle("📖 Las Crónicas del Fogón").WithColor(OnboardingModule.BrandColor)
                    .WithDescription("Primero tenés que empezar tu aventura con **/start**; después el Tabernero te cuenta.").Build(),
                null);
        }

        var zones = ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync());
        return Compose(StoryProgress.For(player, zones), discordId, key);
    }

    // La pantalla a partir del progreso (pura, para probarla sin base de datos).
    public static StoryView Compose(StoryProgress progress, ulong discordId, string? key)
    {
        EmbedBuilder embed;
        if (key == Lore.PrologueKey)
        {
            embed = ReadingEmbed($"📜 Prólogo — «{Lore.PrologueTitle}»", Lore.Prologue, "Las Crónicas del Fogón · Acto I");
        }
        else if (Lore.FindChapter(key) is { } chapter && Lore.IsUnlocked(chapter, progress))
        {
            embed = ReadingEmbed($"📖 Capítulo {chapter.Number} — «{chapter.Title}»", chapter.Text, "Las Crónicas del Fogón · Acto I");
        }
        else if (Lore.FindScene(key) is { } scene && Lore.IsUnlocked(scene, progress))
        {
            embed = ReadingEmbed(
                $"🔥 «{scene.Title}»", scene.Text,
                scene.FuegoNuevo >= Lore.FinalFuegoNuevo
                    ? "Las Crónicas del Fogón · Acto III · Lo cuenta el fuego"
                    : $"Las Crónicas del Fogón · Acto II · Lo cuenta el fuego · {PesarLine(progress.FuegoNuevo)}");
        }
        else
        {
            embed = IndexEmbed(progress);
            key = IndexKey;
        }

        return new StoryView(embed.Build(), new ComponentBuilder().WithSelectMenu(Menu(progress, discordId, key)).Build());
    }

    private static EmbedBuilder ReadingEmbed(string title, string text, string footer) =>
        new EmbedBuilder().WithTitle(title).WithColor(OnboardingModule.BrandColor).WithDescription(text).WithFooter(footer);

    // El pesar del Asador y la mesa, en una línea corta ("🔥 Pesar del Asador 97 % · 🍽️ 38 platos").
    public static string PesarLine(int fuegoNuevo) =>
        $"🔥 Pesar del Asador {Lore.Pesar(fuegoNuevo)} % · 🍽️ {Lore.Plates(fuegoNuevo)} platos";

    // El índice: los tres actos con lo que ya abriste. Lo sellado no muestra título.
    public static EmbedBuilder IndexEmbed(StoryProgress progress)
    {
        var embed = new EmbedBuilder()
            .WithTitle("📖 Las Crónicas del Fogón")
            .WithColor(OnboardingModule.BrandColor)
            .WithDescription("Lo que cuenta el Tabernero junto al fuego. Elegí qué leer en la lista de abajo; lo sellado se abre jugando.");

        var actOne = new List<string> { $"📜 Prólogo — «{Lore.PrologueTitle}»" };
        foreach (var chapter in Lore.Chapters)
        {
            actOne.Add(Lore.IsUnlocked(chapter, progress)
                ? $"📖 Capítulo {chapter.Number} — «{chapter.Title}»"
                : $"🔒 Capítulo {chapter.Number} — sellado");
        }

        embed.AddField("Acto I · Subir", string.Join('\n', actOne), false);

        var actTwoScenes = Lore.Scenes.Where(s => s.FuegoNuevo < Lore.FinalFuegoNuevo).ToList();
        if (progress.FuegoNuevo < 1)
        {
            embed.AddField("Acto II · Soltar", "🔒 Se abre con tu primer **Fuego Nuevo**.", false);
        }
        else
        {
            var lines = new List<string>
            {
                $"{PesarLine(progress.FuegoNuevo)}",
                $"{ProgressBar.Render(Lore.Pesar(progress.FuegoNuevo), 100, 20)}",
            };
            lines.AddRange(actTwoScenes.Where(s => Lore.IsUnlocked(s, progress)).Select(s => $"🔥 «{s.Title}»"));

            if (Lore.NextSceneFuegoNuevo(progress.FuegoNuevo) is int next && next < Lore.FinalFuegoNuevo)
            {
                lines.Add($"🔒 Próxima escena: en el Fuego Nuevo **{next}**.");
            }

            embed.AddField("Acto II · Soltar", string.Join('\n', lines), false);
        }

        var final = Lore.FindScene("fn100")!;
        embed.AddField(
            "Acto III · El que volvió",
            Lore.IsUnlocked(final, progress) ? $"🪑 «{final.Title}»" : $"🔒 Se abre en el Fuego Nuevo **{Lore.FinalFuegoNuevo}**.",
            false);

        return embed;
    }

    // La lista desplegable: el índice y solo lo que ya abriste. Sin emojis en las opciones (si Discord rechaza uno, rechaza el mensaje entero).
    private static SelectMenuBuilder Menu(StoryProgress progress, ulong discordId, string? current)
    {
        var menu = new SelectMenuBuilder()
            .WithCustomId($"{MenuPrefix}:{discordId}")
            .WithPlaceholder("📖 ¿Qué querés leer?")
            .WithMinValues(1)
            .WithMaxValues(1);

        menu.AddOption("Índice", IndexKey, isDefault: current == IndexKey);
        menu.AddOption($"Prólogo — {Lore.PrologueTitle}", Lore.PrologueKey, isDefault: current == Lore.PrologueKey);

        foreach (var chapter in Lore.Chapters.Where(c => Lore.IsUnlocked(c, progress)))
        {
            menu.AddOption(Fit($"Capítulo {chapter.Number} — {chapter.Title}"), chapter.Key, isDefault: current == chapter.Key);
        }

        foreach (var scene in Lore.Scenes.Where(s => Lore.IsUnlocked(s, progress)))
        {
            string label = scene.FuegoNuevo >= Lore.FinalFuegoNuevo
                ? $"Final — {scene.Title}"
                : $"Fuego Nuevo {scene.FuegoNuevo} — {scene.Title}";
            menu.AddOption(Fit(label), scene.Key, isDefault: current == scene.Key);
        }

        return menu;
    }

    // Discord corta las etiquetas de las opciones en 100 caracteres.
    private static string Fit(string label) => label.Length <= 100 ? label : label[..99] + "…";
}
