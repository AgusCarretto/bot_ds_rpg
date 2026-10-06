using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using BotDsRpg.Services;

// Requiere estar registrado (chequeo centralizado en Program.cs): /start es la única forma
// de crear la cuenta la primera vez. Desde la v0.12.0 /class solo sirve empezando de cero (nivel 1 y 0 de EXP): la clase es una decisión de cada vuelta y se vuelve a elegir al hacer un
// Fuego Nuevo (/fuegonuevo). Antes se podía cambiar en cualquier momento y con las vueltas eso rompía la idea de que la clase se bloquea por run.
public class ClassModule(IUserRepository userRepository) : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /class
    [SlashCommand("class", "Elegí tu clase en Asado y Acero RPG (solo con nivel 1; después, con cada Fuego Nuevo).")]
    public async Task HandleClassCommandAsync()
    {
        var player = await userRepository.GetByDiscordIdAsync(Context.User.Id);
        if (player is not null && !FuegoNuevoRules.CanChangeClass(player.Level, player.Xp))
        {
            await RespondAsync(BuildLockedMessage(player), ephemeral: true);
            return;
        }

        // Ephemeral: solo lo ve quien ejecutó el comando, así varios jugadores pueden
        // usar /class en el mismo canal sin pisarse los botones entre ellos.
        await RespondAsync(embed: BuildPromptEmbed(), components: BuildPromptButtons(), ephemeral: true);
    }

    // Lo que se le dice a quien ya no puede cambiar de clase.
    public static string BuildLockedMessage(BotDsRpg.Models.User player) =>
        $"🔒 Tu clase (**{player.Class}**) ya no se cambia: solo se elige empezando de cero (nivel 1) y se vuelve a elegir al hacer un **Fuego Nuevo** (**/fuegonuevo**).";

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
            // La condición (nivel 1 y 0 de EXP) se comprueba en el mismo UPDATE: un botón viejo no cambia la clase de quien ya juega.
            var player = await userRepository.TrySetClassWhileFreshAsync(Context.User.Id, classDef.Name);
            if (player is null)
            {
                var current = await userRepository.GetByDiscordIdAsync(Context.User.Id);
                await FollowupAsync(current is null ? "Todavía no tenés cuenta: empezá con **/start**." : BuildLockedMessage(current), ephemeral: true);
                return;
            }

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
        catch (Exception ex)
        {
            BotLog.Error(ex);
            // Si la base falla, avisamos sin tirar abajo el bot.
            await FollowupAsync("No pude guardar tu clase, intentá de nuevo en un momento.", ephemeral: true);
        }
    }
}
