using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;

// Sin gate de registro: es el único comando que tiene que funcionar para alguien que todavía
// no existe en la base (tanto en Program.cs para slash commands como para "aa start").
public class OnboardingModule(IUserRepository userRepository) : InteractionModuleBase<SocketInteractionContext>
{
    public static readonly Color BrandColor = new(0xE6, 0x51, 0x00); // Naranja oscuro / fuego

    // Comando barra: /start
    [SlashCommand("start", "Empezá tu aventura en Asado y Acero RPG: elegí tu clase inicial.")]
    public async Task HandleStartAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var existing = await userRepository.GetByDiscordIdAsync(Context.User.Id);
            if (existing is not null)
            {
                await FollowupAsync(BuildAlreadyRegisteredMessage(existing), ephemeral: true);
                return;
            }

            await FollowupAsync(embed: BuildWelcomeEmbed(), components: BuildClassButtons(), ephemeral: true);
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! No pude iniciar tu registro ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Se dispara al tocar cualquiera de los botones de arriba (custom ID "start-class:<Clase>")
    // sea cual sea el origen del mensaje ("/start" o "aa start").
    // Prefijo distinto al de /class ("class-select:*") para no pisar ese handler.
    [ComponentInteraction("start-class:*")]
    public async Task HandleClassSelectionAsync(string chosenClass)
    {
        // DeferAsync en una interacción de componente edita el mensaje original una vez resuelto,
        // en vez de crear uno nuevo (evita apilar mensajes de confirmación).
        await DeferAsync();

        var classDef = ClassCatalog.All.FirstOrDefault(c => c.Name == chosenClass);
        if (classDef is null)
        {
            await FollowupAsync("Esa clase no existe, probá de nuevo con /start.", ephemeral: true);
            return;
        }

        try
        {
            var existing = await userRepository.GetByDiscordIdAsync(Context.User.Id);
            if (existing is not null)
            {
                await ModifyOriginalResponseAsync(props =>
                {
                    props.Content = BuildAlreadyRegisteredMessage(existing);
                    props.Embed = null;
                    props.Components = new ComponentBuilder().Build();
                });
                return;
            }

            // Mismo upsert atómico que usa /class (Nivel 1, 0 EXP, 50 de oro, 100/100 HP).
            var player = await userRepository.SetClassAsync(Context.User.Id, classDef.Name);

            await ModifyOriginalResponseAsync(props =>
            {
                props.Embed = BuildConfirmationEmbed(Context.User.Username, player, classDef);
                props.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception)
        {
            await FollowupAsync("¡Upa! No pude completar tu registro, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Todo lo que sigue es estático (sin dependencia de Context) para que
    // Modules/TextCommandModule.cs arme exactamente los mismos mensajes en "aa start".
    public static string BuildAlreadyRegisteredMessage(BotDsRpg.Models.User existing) =>
        $"Ya estás registrado como **{existing.Class}** (Nivel {existing.Level}). Usá `/class` si querés cambiar de clase, o `/profile` para ver tu estado.";

    public static Embed BuildWelcomeEmbed()
    {
        var embed = new EmbedBuilder()
            .WithTitle("🔥 ¡Bienvenido a Asado y Acero RPG! ⚔️")
            .WithColor(BrandColor)
            .WithDescription(
                "Prepárate para adentrarte en un mundo donde el acero forja leyendas y un buen asado te " +
                "salva la vida. Aquí vas a talar bosques, picar piedra, cazar bestias salvajes, forjar " +
                "equipamiento pesado y sobrevivir a base de mate y milanesas.\n\n" +
                "Pero antes de empuñar tu primera espada o prender el fuego para la parrilla, necesitamos saber quién sos.")
            .WithFooter("Hacé clic en uno de los botones abajo para elegir tu camino y reclamar tus 50 monedas de oro iniciales.");

        foreach (var classDef in ClassCatalog.All)
        {
            embed.AddField($"{classDef.Emoji} {classDef.Name}", classDef.Description);
        }

        return embed.Build();
    }

    public static MessageComponent BuildClassButtons()
    {
        var components = new ComponentBuilder();
        foreach (var classDef in ClassCatalog.All)
        {
            components.WithButton(classDef.Name, $"start-class:{classDef.Name}", ButtonStyle.Primary, new Emoji(classDef.Emoji));
        }

        return components.Build();
    }

    private static Embed BuildConfirmationEmbed(string username, BotDsRpg.Models.User player, ClassDefinition classDef)
    {
        return new EmbedBuilder()
            .WithTitle("🔥 Bienvenido a las Tierras de Cenizas")
            .WithColor(BrandColor)
            .WithDescription(
                "El mundo \"Tierras de Cenizas\" no es para los débiles. Hace décadas, la Gran Fragua colapsó, " +
                "desatando criaturas salvajes que tomaron los bosques y las minas, dándole el nombre actual al " +
                "planeta. Hoy, los pocos sobrevivientes se refugian en campamentos fortificados, donde el calor " +
                "del fuego, el olor a carne asada y un buen mate mantienen viva la esperanza.\n\n" +
                "Has llegado al Campamento Base con las manos vacías. Tu misión es recolectar recursos, forjar " +
                "tu equipamiento y sobrevivir.\n\n" +
                "Escribí `/tutorial` o `aa tutorial` para aprender lo básico.")
            .AddField("🎭 Tu clase", $"**{player.Class}** {classDef.Emoji} — especialista en {classDef.WeaponType}", true)
            .AddField("📊 Estado inicial", $"Nivel {player.Level} · {player.CurrentHp}/{player.MaxHp} HP · {player.Gold} de oro", true)
            .WithFooter($"{username}, que el fuego te acompañe.")
            .Build();
    }
}
