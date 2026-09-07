using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;

// Sistema de Zonas: /zona cambia la zona actual del jugador (valida min_level), /zonas lista todas
// las zonas disponibles con su nivel requerido. A partir de acá, /hunt caza exclusivamente
// monstruos de la zona actual (ver Services/AdventureCombatStarter.cs) — /travel no se ve afectado
// a propósito, sigue con su propio pool fijo (ver GameData/MonsterCatalog.TravelMonsters).
public class ZoneModule(IUserRepository userRepository, IZoneRepository zoneRepository)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("zona", "Viajá a otra zona del mundo (usá /zonas para ver los IDs disponibles).")]
    public async Task HandleZonaAsync([Summary("id", "ID de la zona a la que querés viajar (ver /zonas).")] int zoneId)
    {
        await DeferAsync();

        try
        {
            var (message, embed) = await ExecuteTravelAsync(userRepository, zoneRepository, Context.User.Id, zoneId);
            await FollowupAsync(message, embed: embed);
        }
        catch (Exception)
        {
            await FollowupAsync("¡Upa! No pude procesar el viaje ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [SlashCommand("zonas", "Mostrá todas las zonas del mundo y sus niveles requeridos.")]
    public async Task HandleZonasAsync()
    {
        await DeferAsync();

        try
        {
            var embed = await BuildZoneListEmbedAsync(userRepository, zoneRepository, Context.User.Id);
            await FollowupAsync(embed: embed);
        }
        catch (Exception)
        {
            await FollowupAsync("No pude consultar las zonas ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Estático (sin dependencia de Context) para que Modules/TextCommandModule.Zones.cs comparta
    // exactamente la misma lógica en "aa zona <id>". Devuelve texto plano O embed, nunca los dos:
    // los errores de validación (zona inexistente, nivel insuficiente) son solo texto.
    public static async Task<(string? Message, Embed? Embed)> ExecuteTravelAsync(
        IUserRepository userRepository, IZoneRepository zoneRepository, ulong discordId, int zoneId)
    {
        var zone = await zoneRepository.GetByIdAsync(zoneId);
        if (zone is null)
        {
            return ($"No existe ninguna zona con ID **{zoneId}**. Usá `/zonas` (o `aa zonas`) para ver las disponibles.", null);
        }

        var player = await userRepository.GetOrCreateUserAsync(discordId);

        if (player.Level < zone.MinLevel)
        {
            return ($"🚫 **{zone.Name}** requiere nivel **{zone.MinLevel}** — todavía sos nivel {player.Level}. Seguí subiendo antes de cruzar.", null);
        }

        if (player.CurrentZoneId == zone.ZoneId)
        {
            return ($"Ya estás en **{zone.Name}** {zone.Emoji}.", null);
        }

        await userRepository.ChangeZoneAsync(discordId, zone.ZoneId);

        return (null, BuildTravelEmbed(zone));
    }

    private static Embed BuildTravelEmbed(Zone zone)
    {
        return new EmbedBuilder()
            .WithTitle("🗺️ ¡Viaje completado!")
            .WithColor(Color.Teal)
            .WithDescription($"Llegaste a **{zone.Name}** {zone.Emoji}.\n\n_{zone.Description}_")
            .AddField("📊 Nivel requerido", zone.MinLevel.ToString(), true)
            .WithFooter("A partir de ahora, /hunt caza monstruos de esta zona.")
            .Build();
    }

    // Estático por el mismo motivo que ExecuteTravelAsync — reusado por "aa zonas".
    public static async Task<Embed> BuildZoneListEmbedAsync(IUserRepository userRepository, IZoneRepository zoneRepository, ulong discordId)
    {
        var player = await userRepository.GetOrCreateUserAsync(discordId);
        var zones = await zoneRepository.GetAllAsync();

        var embed = new EmbedBuilder()
            .WithTitle("🗺️ Zonas de Asado y Acero")
            .WithColor(Color.Teal)
            .WithDescription("Usá `/zona <id>` (o `aa zona <id>`) para viajar. `/hunt` siempre caza en tu zona actual.");

        foreach (var zone in zones)
        {
            string here = zone.ZoneId == player.CurrentZoneId ? " 📍 _(acá estás)_" : string.Empty;
            string locked = player.Level < zone.MinLevel ? " 🔒" : string.Empty;
            embed.AddField(
                $"{zone.Emoji} {zone.ZoneId}. {zone.Name}{here}{locked}",
                $"Nivel mínimo: **{zone.MinLevel}**\n_{zone.Description}_",
                false);
        }

        return embed.Build();
    }
}
