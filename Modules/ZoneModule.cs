using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;

// Sistema de Zonas: /zona cambia la zona actual del jugador (valida min_level y, desde el Sistema
// de Jefes de Zona, que ya hayas derrotado al jefe de la zona anterior), /zonas lista todas las
// zonas disponibles con su nivel requerido. A partir de acá, /hunt caza exclusivamente monstruos de
// la zona actual (ver Services/AdventureCombatStarter.cs) — /travel no se ve afectado a propósito,
// sigue con su propio pool fijo (ver GameData/MonsterCatalog.TravelMonsters).
public class ZoneModule(IUserRepository userRepository, IZoneRepository zoneRepository, IMonsterRepository monsterRepository)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("zona", "Viajá a otra zona del mundo (usá /zonas para ver los IDs disponibles).")]
    public async Task HandleZonaAsync([Summary("id", "ID de la zona a la que querés viajar (ver /zonas).")] int zoneId)
    {
        await DeferAsync();

        try
        {
            var (message, embed) = await ExecuteTravelAsync(userRepository, zoneRepository, monsterRepository, Context.User.Id, zoneId);
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
    // los errores de validación (zona inexistente, nivel insuficiente, jefe no derrotado) son solo texto.
    public static async Task<(string? Message, Embed? Embed)> ExecuteTravelAsync(
        IUserRepository userRepository, IZoneRepository zoneRepository, IMonsterRepository monsterRepository, ulong discordId, int zoneId)
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

        // Bloqueo por Jefe de Zona: no se puede saltar más de una zona más allá de la última cuyo
        // jefe ya derrotaste. Se compara por POSICIÓN en la lista ordenada por min_level (no por
        // zone_id crudo — ver users.highest_zone_cleared en Database/schema.sql). Si la zona
        // inmediatamente anterior todavía no tiene un jefe cargado, no bloqueamos: no seria justo
        // trabar el avance por contenido que todavía no existe.
        var orderedZones = (await zoneRepository.GetAllAsync()).OrderBy(z => z.MinLevel).ToList();
        int targetRank = orderedZones.FindIndex(z => z.ZoneId == zone.ZoneId) + 1;

        if (targetRank > 1)
        {
            var gatekeeperZone = orderedZones[targetRank - 2];
            var gatekeeperBoss = await monsterRepository.GetBossByZoneAsync(gatekeeperZone.ZoneId);

            if (gatekeeperBoss is not null)
            {
                int clearedRank = player.HighestZoneCleared == 0
                    ? 0
                    : orderedZones.FindIndex(z => z.ZoneId == player.HighestZoneCleared) + 1;

                if (targetRank > clearedRank + 1)
                {
                    return (
                        $"🔒 Para viajar a **{zone.Name}** primero tenés que derrotar a **{gatekeeperBoss.Name}** {gatekeeperBoss.Emoji}, " +
                        $"el Jefe de **{gatekeeperZone.Name}** — probá `/boss` estando ahí.",
                        null);
                }
            }
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
