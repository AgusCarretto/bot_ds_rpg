using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;

public class ClassModule(IUserRepository userRepository) : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /class
    [SlashCommand("class", "Elegí tu clase en Asado y Acero RPG.")]
    public async Task HandleClassCommandAsync()
    {
        var embed = new EmbedBuilder()
            .WithTitle("🎭 Elegí tu clase")
            .WithColor(Color.Gold)
            .WithDescription("Cada clase usa un arma exclusiva y define tu estilo de combate. Tocá un botón para elegir.");

        var components = new ComponentBuilder();

        foreach (var classDef in ClassCatalog.All)
        {
            embed.AddField($"{classDef.Emoji} {classDef.Name} — {classDef.WeaponType}", classDef.Description);
            components.WithButton(classDef.Name, $"class-select:{classDef.Name}", ButtonStyle.Primary, new Emoji(classDef.Emoji));
        }

        // Ephemeral: solo lo ve quien ejecutó el comando, así varios jugadores pueden
        // usar /class en el mismo canal sin pisarse los botones entre ellos.
        await RespondAsync(embed: embed.Build(), components: components.Build(), ephemeral: true);
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
