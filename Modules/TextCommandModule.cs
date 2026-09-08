using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

// Puente de comandos de texto tradicionales (prefijo "aa ", ver Program.cs) para quienes no
// puedan usar cómodamente el selector de slash commands (ej. desde el celular). Ninguno de estos
// métodos reimplementa lógica de negocio: todos llaman a los mismos repositorios/servicios que
// sus equivalentes en slash command, y reutilizan los mismos builders de Embed (públicos en sus
// módulos de origen) para que la respuesta se vea idéntica sea cual sea el camino usado.
//
// partial: dividido por dominio para que no siga creciendo como un único archivo de 450+ líneas
// cada vez que se suma un comando nuevo — ver TextCommandModule.Combat.cs, .Gathering.cs y
// .Economy.cs. El constructor primario (con todas las dependencias inyectadas) vive acá, y queda
// accesible desde las otras partes porque son la misma clase de C#.
public partial class TextCommandModule(
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IInventoryRepository inventoryRepository,
    ICooldownRepository cooldownRepository,
    IGatheringRepository gatheringRepository,
    IShopRepository shopRepository,
    ICraftingRepository craftingRepository,
    IRecipeRepository recipeRepository,
    ICasinoRepository casinoRepository,
    ICasinoService casinoService,
    ICombatSessionService combatSessions,
    IAdventureCombatStarter combatStarter,
    IAdventureRepository adventureRepository,
    IProgressionRepository progressionRepository,
    IZoneRepository zoneRepository) : ModuleBase<SocketCommandContext>
{
    // ---- Onboarding / clase ----

    // "aa start" — misma lógica que OnboardingModule.HandleStartAsync. Único comando de texto
    // que no exige estar registrado (ver Program.cs).
    [Command("start")]
    [Summary("Empezá tu aventura: elegí tu clase inicial.")]
    public async Task StartAsync()
    {
        try
        {
            var existing = await userRepository.GetByDiscordIdAsync(Context.User.Id);
            if (existing is not null)
            {
                await ReplyAsync(OnboardingModule.BuildAlreadyRegisteredMessage(existing));
                return;
            }

            await ReplyAsync(embed: OnboardingModule.BuildWelcomeEmbed(), components: OnboardingModule.BuildClassButtons());
        }
        catch (Exception)
        {
            await ReplyAsync("¡Upa! No pude iniciar tu registro ahora mismo, intentá de nuevo en un momento.");
        }
    }

    // "aa class" / "aa c" — misma lógica que ClassModule.HandleClassCommandAsync.
    [Command("class")]
    [Alias("c")]
    [Summary("Elegí tu clase en Asado y Acero RPG.")]
    public Task ClassAsync() =>
        ReplyAsync(embed: ClassModule.BuildPromptEmbed(), components: ClassModule.BuildPromptButtons());

    // ---- Ayuda ----

    // "aa tutorial" / "aa tu" — misma lógica que HelpModule.HandleTutorialAsync.
    [Command("tutorial")]
    [Alias("tu")]
    [Summary("Aprendé el loop básico del juego: recolección, herrería, combate y supervivencia.")]
    public Task TutorialAsync() => ReplyAsync(embed: HelpModule.BuildTutorialEmbed());

    // "aa info" / "aa in" — misma lógica que HelpModule.HandleInfoAsync.
    [Command("info")]
    [Alias("in")]
    [Summary("Mostrá la lista completa de comandos, agrupados por categoría.")]
    public Task InfoAsync() => ReplyAsync(embed: HelpModule.BuildInfoEmbed());

    // ---- Perfil ----

    // "aa profile" / "aa p" — misma lógica que GameModule.HandleProfileAsync, incluyendo poder
    // consultar a otro jugador mencionándolo: "aa p @alguien".
    [Command("profile")]
    [Alias("p")]
    [Summary("Mostrá tu estado actual, nivel y estadísticas (o los de otro jugador: \"aa p @alguien\").")]
    public async Task ProfileAsync([Remainder] IUser? targetUser = null)
    {
        IUser target = targetUser ?? Context.User;

        try
        {
            if (target.Id != Context.User.Id && await userRepository.GetByDiscordIdAsync(target.Id) is null)
            {
                await ReplyAsync(GameModule.BuildNotRegisteredMessage(GameModule.GetDisplayName(target)));
                return;
            }

            string avatarUrl = target.GetAvatarUrl() ?? target.GetDefaultAvatarUrl();
            var embed = await GameModule.BuildProfileEmbedAsync(userRepository, itemRepository, target.Id, GameModule.GetDisplayName(target), avatarUrl);
            await ReplyAsync(embed: embed);
        }
        catch (Exception)
        {
            await ReplyAsync("¡Upa! No pude acceder a ese perfil ahora mismo, intentá de nuevo en un momento.");
        }
    }

    // "aa inventory" / "aa i" — misma lógica que GameModule.HandleInventoryAsync, incluyendo poder
    // consultar a otro jugador mencionándolo: "aa i @alguien".
    [Command("inventory")]
    [Alias("i")]
    [Summary("Mostrá los materiales que tenés guardados (o los de otro jugador: \"aa i @alguien\").")]
    public async Task InventoryAsync([Remainder] IUser? targetUser = null)
    {
        IUser target = targetUser ?? Context.User;

        try
        {
            if (target.Id != Context.User.Id && await userRepository.GetByDiscordIdAsync(target.Id) is null)
            {
                await ReplyAsync(GameModule.BuildNotRegisteredMessage(GameModule.GetDisplayName(target)));
                return;
            }

            var embed = await GameModule.BuildInventoryEmbedAsync(inventoryRepository, target.Id, GameModule.GetDisplayName(target));
            await ReplyAsync(embed: embed);
        }
        catch (Exception)
        {
            await ReplyAsync("No pude consultar ese inventario ahora mismo, intentá de nuevo en un momento.");
        }
    }

    // "aa cd" — misma lógica que CooldownModule.HandleCooldownsAsync.
    [Command("cd")]
    [Summary("Mostrá el estado de tus cooldowns.")]
    public async Task CooldownsAsync()
    {
        try
        {
            var embed = await CooldownModule.BuildStatusEmbedAsync(cooldownRepository, userRepository, Context.User.Id);
            await ReplyAsync(embed: embed);
        }
        catch (Exception)
        {
            await ReplyAsync("No pude consultar tus cooldowns ahora mismo, intentá de nuevo en un momento.");
        }
    }

    // "aa leaderboard" / "aa lb" — misma lógica que LeaderboardModule.HandleLeaderboardAsync.
    [Command("leaderboard")]
    [Alias("lb")]
    [Summary("Mostrá a los jugadores con más nivel/experiencia del server.")]
    public async Task LeaderboardAsync()
    {
        try
        {
            var embed = await LeaderboardModule.BuildLeaderboardEmbedAsync(userRepository);
            await ReplyAsync(embed: embed);
        }
        catch (Exception)
        {
            await ReplyAsync("No pude cargar el ranking ahora mismo, intentá de nuevo en un momento.");
        }
    }
}
