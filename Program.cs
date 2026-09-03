using BotDsRpg.Data;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

class Program
{
    private const string TextCommandPrefix = "aa ";

    private static ServiceProvider _services = null!;
    private static DiscordSocketClient _client = null!;
    private static InteractionService _commands = null!;
    private static CommandService _textCommands = null!;

    static async Task Main(string[] args)
    {
        // 1. Cargamos configuración: .env local (gitignored) -> User Secrets -> variables de entorno reales.
        //    Nunca hardcodear el token ni la cadena de conexión acá ni en ningún archivo trackeado por git.
        if (File.Exists(".env"))
        {
            DotNetEnv.Env.Load(); // Vuelca las claves del .env como variables de entorno del proceso
        }

        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        string? token = configuration["Discord:Token"];
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "No se encontró el token del bot. Configuralo con " +
                "'dotnet user-secrets set \"Discord:Token\" \"<tu-token>\"' en desarrollo, " +
                "o con la variable de entorno 'Discord__Token' en producción.");
        }

        // 2. Configuramos los permisos del bot (Gateway Intents)
        //    MessageContent ya estaba habilitado (lo usa Discord.Net para leer el contenido de
        //    los mensajes normales), imprescindible para los comandos de texto "aa ...".
        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
        };

        _client = new DiscordSocketClient(config);
        _commands = new InteractionService(_client.Rest);
        _textCommands = new CommandService(new CommandServiceConfig { CaseSensitiveCommands = false });

        // 3. Inyectamos dependencias
        _services = ServiceProviderBuilder.BuildServiceProvider(_client, _commands, configuration);

        // 4. Registramos los eventos del sistema
        _client.Log += LogAsync;
        _commands.Log += LogAsync;
        _textCommands.Log += LogAsync;
        _client.Ready += () => ReadyAsync(configuration);
        _client.InteractionCreated += HandleInteractionAsync;
        _client.MessageReceived += HandleTextMessageAsync;

        // 5. Nos logueamos y arrancamos el bot
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        // Mantenemos la aplicación corriendo
        await Task.Delay(Timeout.Infinite);
    }

    private static Task LogAsync(LogMessage message)
    {
        Console.WriteLine(message.ToString());
        return Task.CompletedTask;
    }

    // Discord.Net dispara Ready en cada reconexión (no solo en el login inicial); sin esta
    // guarda, AddModulesAsync intentaría re-registrar los mismos módulos y tiraría una excepción
    // sin manejar tras cualquier corte de red.
    private static bool _modulesRegistered;

    private static async Task ReadyAsync(IConfiguration configuration)
    {
        Console.WriteLine($"\n[ÉXITO] ¡Asado y Acero RPG ({_client.CurrentUser.Username}) está en línea!");

        if (_modulesRegistered)
        {
            return;
        }

        _modulesRegistered = true;

        await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        await _textCommands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        Console.WriteLine("[INFO] Comandos de texto (prefijo \"aa \") registrados.");

        // Si hay un servidor de pruebas configurado, registramos los comandos ahí:
        // se propagan al instante, ideal para iterar rápido en desarrollo.
        // Sin esa config, se registran globalmente (pueden tardar hasta 1h en aparecer).
        string? testGuildIdRaw = configuration["Discord:TestGuildId"];
        if (ulong.TryParse(testGuildIdRaw, out ulong testGuildId) && testGuildId != 0)
        {
            await _commands.RegisterCommandsToGuildAsync(testGuildId, deleteMissing: true);
            Console.WriteLine($"[INFO] Comandos de barra registrados en el servidor de pruebas ({testGuildId}).");
        }
        else
        {
            await _commands.RegisterCommandsGloballyAsync();
            Console.WriteLine("[INFO] Comandos de barra registrados globalmente (pueden tardar hasta 1h en aparecer).");
        }
    }

    private static async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        try
        {
            // Chequeo directo ANTES de despachar el comando: nada de inspeccionar el resultado
            // de una precondición (ese enfoque dependía de que Discord.Net devolviera el
            // ErrorReason exacto, y no coincidía — la interacción quedaba sin ninguna respuesta
            // y Discord terminaba mostrando "la aplicación no responde" a los 3 segundos).
            // /start es la excepción: es el único comando que tiene que funcionar sin cuenta.
            if (interaction is SocketSlashCommand slashCommand
                && slashCommand.CommandName != "start"
                && await RejectIfNotRegisteredAsync(interaction.User.Id, respondAsync: (text) => interaction.RespondAsync(text, ephemeral: false)))
            {
                return;
            }

            var context = new SocketInteractionContext(_client, interaction);
            var result = await _commands.ExecuteCommandAsync(context, _services);

            if (!result.IsSuccess)
                Console.WriteLine($"[ERROR DE COMANDO] {result.ErrorReason}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EXCEPCIÓN] {ex.Message}");
            if (interaction.Type == InteractionType.ApplicationCommand)
            {
                try
                {
                    var msg = await interaction.GetOriginalResponseAsync();
                    if (msg == null)
                        await interaction.RespondAsync("¡Upa! Ocurrió un error inesperado al procesar tu comando.", ephemeral: true);
                }
                catch
                {
                    // Puede fallar si la excepción original ocurrió antes de Defer/Respond
                    // (nunca hubo una respuesta que consultar); no hay nada más que hacer acá.
                }
            }
        }
    }

    // Puente de comandos de texto tradicionales ("aa hunt", "aa daily", "aa shop view"), pensado
    // para quienes no pueden usar el selector de slash commands cómodamente (ej. desde el celular).
    private static async Task HandleTextMessageAsync(SocketMessage rawMessage)
    {
        // Ignora mensajes de otros bots, webhooks y del sistema (edits, pins, etc.) — solo
        // procesamos mensajes de texto reales escritos por una persona.
        if (rawMessage is not SocketUserMessage message || message.Source != MessageSource.User)
        {
            return;
        }

        int argPos = 0;
        if (!message.HasStringPrefix(TextCommandPrefix, ref argPos, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            // Mismo gate de registro que los slash commands, para que "aa hunt" no esquive /start.
            // "aa start" queda exento (es el único comando de texto que tiene que funcionar sin cuenta).
            string commandText = message.Content[argPos..].TrimStart();
            bool isStartCommand = commandText.Equals("start", StringComparison.OrdinalIgnoreCase)
                || commandText.StartsWith("start ", StringComparison.OrdinalIgnoreCase);

            if (!isStartCommand
                && await RejectIfNotRegisteredAsync(message.Author.Id, respondAsync: (text) => message.Channel.SendMessageAsync(text)))
            {
                return;
            }

            var context = new SocketCommandContext(_client, message);
            var result = await _textCommands.ExecuteAsync(context, argPos, _services);

            if (!result.IsSuccess)
            {
                await HandleTextCommandFailureAsync(context, message, argPos, result);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EXCEPCIÓN COMANDO DE TEXTO] {ex.Message}");
        }
    }

    // Manejo global de errores para "aa ...": cada [Command] individual ya atrapa sus propias
    // fallas de negocio (oro insuficiente, faltan materiales, etc.) devolviendo un resultado
    // tipado y respondiendo con un mensaje amigable — ver Modules/TextCommandModule*.cs. Ninguna
    // de esas reglas se modela como excepción a propósito. Lo que llega hasta acá son fallas de
    // DESPACHO, previas a que el método del comando llegue a ejecutarse: comando inexistente,
    // argumentos de más/menos o del tipo equivocado; y como red de seguridad final, cualquier
    // excepción que se le haya escapado a un comando sin pasar por su propio try/catch.
    private static async Task HandleTextCommandFailureAsync(SocketCommandContext context, SocketUserMessage message, int argPos, Discord.Commands.IResult result)
    {
        switch (result.Error)
        {
            case CommandError.UnknownCommand:
                // Antes esto se ignoraba (por miedo a contestarle a charla normal que arranca con
                // "aa "), pero en la práctica los typos de comandos reales ("aa sell" en vez de
                // "aa shop sell") son mucho más comunes que un choque real con una frase de chat
                // — y quedarse callado ahí es peor UX que un aviso corto. Se loguea también.
                Console.WriteLine($"[COMANDO DESCONOCIDO] \"{message.Content}\" de {message.Author.Username}");
                await message.Channel.SendMessageAsync(
                    "❌ No reconozco ese comando. Probá `/info` o `aa info` para ver la lista completa.");
                return;

            case CommandError.BadArgCount:
            case CommandError.ParseFailed:
            case CommandError.ObjectNotFound:
                // Search() vuelve a matchear el comando por NOMBRE (a diferencia del ParseResult
                // que ya falló, esto no depende de que los argumentos sean válidos), así
                // recuperamos qué comando se quiso usar para mostrar su sintaxis correcta —
                // incluso en BadArgCount, donde el error no apunta a un parámetro puntual.
                var searchResult = _textCommands.Search(context, argPos);
                CommandInfo? command = searchResult.IsSuccess && searchResult.Commands.Count > 0
                    ? searchResult.Commands[0].Command
                    : null;

                await message.Channel.SendMessageAsync(embed: BuildUsageErrorEmbed(command));
                return;

            default:
                // UnmetPrecondition, Exception, Unsuccessful: no debería pasar casi nunca (las
                // reglas de negocio ya se manejan como resultado tipado dentro de cada comando),
                // pero si se escapa una excepción de verdad la logueamos completa en vez de
                // dejarla morir en silencio, y avisamos sin tirar abajo el bot.
                string detail = result is Discord.Commands.ExecuteResult { Exception: { } ex } ? ex.ToString() : result.ErrorReason;
                Console.WriteLine($"[ERROR DE COMANDO DE TEXTO] {detail}");
                await message.Channel.SendMessageAsync("¡Upa! Algo falló procesando ese comando, intentá de nuevo en un momento.");
                return;
        }
    }

    private static Embed BuildUsageErrorEmbed(CommandInfo? command)
    {
        var embed = new EmbedBuilder()
            .WithTitle("⚠️ Faltan parámetros o son incorrectos")
            .WithColor(Color.Orange);

        if (command is not null)
        {
            embed.AddField("Uso correcto", $"`{BuildUsageLine(command)}`", false);

            if (!string.IsNullOrWhiteSpace(command.Summary))
            {
                embed.AddField("¿Qué hace?", command.Summary, false);
            }
        }
        else
        {
            embed.WithDescription("Revisá que el comando esté bien escrito. Usá `aa info` para ver la lista completa.");
        }

        return embed.Build();
    }

    // Arma "aa <comando> <param1> [param2]..." a partir de los parámetros reales del comando
    // (obligatorios entre <>, opcionales entre []), para no tener que mantener a mano un texto de
    // uso por comando que se desactualice apenas cambien los parámetros.
    private static string BuildUsageLine(CommandInfo command)
    {
        var parts = command.Parameters.Select(parameter =>
        {
            string name = parameter.IsRemainder || parameter.IsMultiple ? $"{parameter.Name}..." : parameter.Name;
            return parameter.IsOptional ? $"[{name}]" : $"<{name}>";
        });

        string usage = $"{TextCommandPrefix}{command.Name}";
        return parts.Any() ? $"{usage} {string.Join(' ', parts)}" : usage;
    }

    // Devuelve true (y ya respondió) si el usuario todavía no está registrado. Compartido entre
    // slash commands y comandos de texto — cada uno le pasa su propia forma de responder.
    private static async Task<bool> RejectIfNotRegisteredAsync(ulong discordId, Func<string, Task> respondAsync)
    {
        var userRepository = _services.GetRequiredService<IUserRepository>();
        var user = await userRepository.GetByDiscordIdAsync(discordId);

        if (user is not null)
        {
            return false;
        }

        // Público a propósito (no ephemeral): la idea es que se note en el canal que hace falta /start.
        await respondAsync($"<@{discordId}> ¡Bienvenido a Asado y Acero RPG! 🥩 Para empezar tu aventura, primero tenés que elegir una clase con el comando `/start`.");
        return true;
    }
}

// Clase auxiliar para manejar servicios internos de Discord.Net y la capa de datos
public static class ServiceProviderBuilder
{
    public static ServiceProvider BuildServiceProvider(DiscordSocketClient client, InteractionService commands, IConfiguration configuration)
    {
        return new ServiceCollection()
            .AddSingleton(client)
            .AddSingleton(commands)
            .AddSingleton(configuration)
            .AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>()
            .AddSingleton<IUserRepository, UserRepository>()
            .AddSingleton<IProgressionRepository, ProgressionRepository>()
            .AddSingleton<ICooldownRepository, CooldownRepository>()
            .AddSingleton<IAdventureRepository, AdventureRepository>()
            .AddSingleton<IGatheringRepository, GatheringRepository>()
            .AddSingleton<IItemRepository, ItemRepository>()
            .AddSingleton<IInventoryRepository, InventoryRepository>()
            .AddSingleton<IShopRepository, ShopRepository>()
            .AddSingleton<ICraftingRepository, CraftingRepository>()
            .AddSingleton<IRecipeRepository, RecipeRepository>()
            .AddSingleton<ICasinoRepository, CasinoRepository>()
            .AddSingleton<ICasinoService, CasinoService>()
            .AddSingleton<ICombatSessionService, CombatSessionService>()
            .AddSingleton<IAdventureCombatStarter, AdventureCombatStarter>()
            .BuildServiceProvider();
    }
}
