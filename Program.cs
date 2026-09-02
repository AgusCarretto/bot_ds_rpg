using BotDsRpg.Data;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

class Program
{
    private static ServiceProvider _services = null!;
    private static DiscordSocketClient _client = null!;
    private static InteractionService _commands = null!;

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
        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
        };

        _client = new DiscordSocketClient(config);
        _commands = new InteractionService(_client.Rest);

        // 3. Inyectamos dependencias
        _services = ServiceProviderBuilder.BuildServiceProvider(_client, _commands, configuration);

        // 4. Registramos los eventos del sistema
        _client.Log += LogAsync;
        _commands.Log += LogAsync;
        _client.Ready += () => ReadyAsync(configuration);
        _client.InteractionCreated += HandleInteractionAsync;

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
            .AddSingleton<ICooldownRepository, CooldownRepository>()
            .AddSingleton<IAdventureRepository, AdventureRepository>()
            .AddSingleton<IGatheringRepository, GatheringRepository>()
            .AddSingleton<IItemRepository, ItemRepository>()
            .AddSingleton<IInventoryRepository, InventoryRepository>()
            .AddSingleton<IShopRepository, ShopRepository>()
            .AddSingleton<ICraftingRepository, CraftingRepository>()
            .AddSingleton<ICasinoRepository, CasinoRepository>()
            .AddSingleton<ICombatService, CombatService>()
            .AddSingleton<ICasinoService, CasinoService>()
            .BuildServiceProvider();
    }
}