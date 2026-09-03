using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;

// Requiere estar registrado (chequeo centralizado en Program.cs): /start es la única forma
// de crear la cuenta la primera vez, /class queda para re-elegir clase una vez que ya existís.
public class ClassModule(IUserRepository userRepository) : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /class
    [SlashCommand("class", "Elegí tu clase en Asado y Acero RPG.")]
    public Task HandleClassCommandAsync() =>
        // Ephemeral: solo lo ve quien ejecutó el comando, así varios jugadores pueden
        // usar /class en el mismo canal sin pisarse los botones entre ellos.
        RespondAsync(embed: BuildPromptEmbed(), components: BuildPromptButtons(), ephemeral: true);

    // Públicos (sin dependencia de Context) para que Modules/TextCommandModule.cs arme el mismo
    // mensaje en "aa class".
    public static Embed BuildPromptEmbed()
    {
        var embed = new EmbedBuilder()
            .WithTitle("🎭 Elegí tu clase")
            .WithColor(Color.Gold)
            .WithDescription("Cada clase usa un arma exclusiva y define tu estilo de combate. Tocá un botón para elegir.");

        foreach (var classDef in ClassCatalog.All)
        {
            embed.AddField($"{classDef.Emoji} {classDef.Name} — {classDef.WeaponType}", classDef.Description);
        }

        return embed.Build();
    }

    public static MessageComponent BuildPromptButtons()
    {
        var components = new ComponentBuilder();

        foreach (var classDef in ClassCatalog.All)
        {
            components.WithButton(classDef.Name, $"class-select:{classDef.Name}", ButtonStyle.Primary, new Emoji(classDef.Emoji));
        }

        return components.Build();
    }

    // Se dispara al tocar cualquiera de los botones de arriba (custom ID "class-select:<Clase>")
    [ComponentInteraction("class-select:*")]
    public async Task HandleClassSelectionAsync(string chosenClass)
    {
        // DeferAsync en una interacción de componente edita el mensaje original una vez resuelto,
        // en vez de crear uno nuevo (evita apilar mensajes de confirmación).
        await DeferAsync();

        var classDef = ClassCatalog.All.FirstOrDefault(c => c.Name == chosenClass);
        if (classDef is null)
        {
            await FollowupAsync("Esa clase no existe, probá de nuevo con /class.", ephemeral: true);
            return;
        }

        try
        {
            var player = await userRepository.SetClassAsync(Context.User.Id, classDef.Name);

            var confirmationEmbed = new EmbedBuilder()
                .WithTitle("✅ ¡Clase asignada!")
                .WithDescription($"{Context.User.Username} ahora es **{player.Class}** {classDef.Emoji}, especialista en {classDef.WeaponType}.")
                .WithColor(Color.Green)
                .Build();

            await ModifyOriginalResponseAsync(props =>
            {
                props.Embed = confirmationEmbed;
                props.Components = new ComponentBuilder().Build(); // saca los botones tras elegir
            });
        }
        catch (Exception)
        {
            // Si la base falla, avisamos sin tirar abajo el bot.
            await FollowupAsync("No pude guardar tu clase, intentá de nuevo en un momento.", ephemeral: true);
        }
    }
}
