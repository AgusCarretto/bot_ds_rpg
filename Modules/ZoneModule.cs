using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using Discord;
using Discord.Interactions;
using BotDsRpg.Services;

// Sistema de Zonas: /zona cambia la zona actual del jugador (valida min_level y, desde el Sistema
// de Jefes de Zona, que ya hayas derrotado al jefe de la zona anterior), /zonas lista todas las
// zonas disponibles con su nivel requerido. A partir de acá, /hunt, /travel y /boss enfrentan
// exclusivamente monstruos de la zona actual (ver Services/AdventureCombatStarter.cs): un pool para
// /hunt, un monstruo dedicado para /travel (Database/seed_travel_monsters.sql) y el jefe para /boss.
public class ZoneModule(IUserRepository userRepository, IZoneRepository zoneRepository, IMonsterRepository monsterRepository, IItemRepository itemRepository)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("zona", "Viajá a otra zona del mundo (usá /zonas para ver los IDs disponibles).")]
    public async Task HandleZonaAsync(
        [Summary("id", "Elegí de la lista la zona a la que querés viajar (ver /zonas).")]
        [Autocomplete(typeof(ZoneAutocompleteHandler))] int zoneId)
    {
        await DeferAsync();

        try
        {
            var (message, embed) = await ExecuteTravelAsync(userRepository, zoneRepository, monsterRepository, Context.User.Id, zoneId, itemRepository);
            await FollowupAsync(message, embed: embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude procesar el viaje ahora mismo, intentá de nuevo en un momento."), ephemeral: true);
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
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("No pude consultar las zonas ahora mismo, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // Estático (sin dependencia de Context) para que Modules/TextCommandModule.Zones.cs comparta
    // exactamente la misma lógica en "aa zona <id>". Devuelve texto plano O embed, nunca los dos:
    // los errores de validación (zona inexistente, nivel insuficiente, jefe no derrotado) son solo texto.
    public static async Task<(string? Message, Embed? Embed)> ExecuteTravelAsync(
        IUserRepository userRepository, IZoneRepository zoneRepository, IMonsterRepository monsterRepository, ulong discordId, int zoneId,
        IItemRepository? itemRepository = null)
    {
        // La zona 0 es El Fogón Eterno (GameData/FogonRules.cs): otras reglas de entrada y no es una zona de la escalera.
        if (zoneId == FogonRules.GateZoneId)
        {
            return await ExecuteEnterGateAsync(userRepository, zoneRepository, itemRepository, discordId);
        }

        var zone = await zoneRepository.GetByIdAsync(zoneId);
        if (zone is null || zone.Kind != "normal")
        {
            return (Loc.F($"No existe ninguna zona con ID **{zoneId}**. Usá `/zonas` (o `aa zonas`) para ver las disponibles."), null);
        }

        var player = await userRepository.GetOrCreateUserAsync(discordId);

        // Las zonas 6 a 10 (v0.47.0) solo existen con su Fuego Nuevo.
        if (zone.MinFuegoNuevo > player.FuegoNuevo)
        {
            return (Loc.F($"🔒 **{zone.Name}** {zone.Emoji} se abre con el **Fuego Nuevo {zone.MinFuegoNuevo}** (tenés {player.FuegoNuevo}). Mirá cómo funciona con `/info tema:fuego`."), null);
        }

        if (player.Level < zone.MinLevel)
        {
            return (Loc.F($"🚫 **{zone.Name}** requiere nivel **{zone.MinLevel}** — todavía sos nivel {player.Level}. Seguí subiendo antes de cruzar."), null);
        }

        if (player.CurrentZoneId == zone.ZoneId && !player.InGate)
        {
            return (Loc.F($"Ya estás en **{zone.Name}** {zone.Emoji}."), null);
        }

        // Bloqueo por Jefe de Zona: no se puede saltar más de una zona más allá de la última cuyo
        // jefe ya derrotaste. Se compara por POSICIÓN en la lista ordenada por min_level (no por
        // zone_id crudo — ver users.highest_zone_cleared en Database/schema.sql). Si la zona
        // inmediatamente anterior todavía no tiene un jefe cargado, no bloqueamos: no seria justo
        // trabar el avance por contenido que todavía no existe.
        var orderedZones = ZoneRanking.ForPlayer(await zoneRepository.GetAllAsync(), player.FuegoNuevo);
        var gatekeeperZone = ZoneRanking.PendingGatekeeperZone(orderedZones, zone.ZoneId, player.HighestZoneCleared);

        // Las zonas 6 a 10 no tienen jefe: se sale de ellas pasando su mazmorra (GameData/Dungeons).
        if (ZoneRanking.IsDungeonZone(gatekeeperZone))
        {
            return (Loc.F($"🔒 Para viajar a **{zone.Name}** primero tenés que pasar la **mazmorra** de **{gatekeeperZone!.Name}**: forjá su **Reliquia** y usá `/boss` estando ahí."), null);
        }

        if (gatekeeperZone is not null)
        {
            var gatekeeperBoss = await monsterRepository.GetBossByZoneAsync(gatekeeperZone.ZoneId);

            if (gatekeeperBoss is not null)
            {
                return (
                    Loc.F($"🔒 Para viajar a **{zone.Name}** primero tenés que derrotar a **{gatekeeperBoss.Name}** {gatekeeperBoss.Emoji}, ") +
                    Loc.F($"el Jefe de **{gatekeeperZone.Name}** — probá `/boss` estando ahí."),
                    null);
            }
        }

        await userRepository.ChangeZoneAsync(discordId, zone.ZoneId);

        return (null, BuildTravelEmbed(zone));
    }

    // /zona 0: entrar a El Fogón Eterno. Hace falta haber vencido al jefe de la última zona, el nivel de la puerta y llevar PUESTOS el arma y el amuleto del Fogón. Entrar solo
    // marca users.in_gate (la zona actual no cambia); salir es viajar a cualquier zona normal.
    private static async Task<(string? Message, Embed? Embed)> ExecuteEnterGateAsync(
        IUserRepository userRepository, IZoneRepository zoneRepository, IItemRepository? itemRepository, ulong discordId)
    {
        var gate = await zoneRepository.GetGateAsync();
        if (gate is null)
        {
            return (Loc.F($"No existe ninguna zona con ID **{FogonRules.GateZoneId}**. Usá `/zonas` (o `aa zonas`) para ver las disponibles."), null);
        }

        var player = await userRepository.GetOrCreateUserAsync(discordId);
        var orderedZones = ZoneRanking.ForPlayer(await zoneRepository.GetAllAsync(), player.FuegoNuevo);

        // Con la zona tope en las 6 a 10 (v0.47.0) a la puerta no se entra caminando: se llega pasando la mazmorra de esa zona, que te deja frente al Asador.
        if (ZoneRanking.IsDungeonZone(FogonRules.LastZone(orderedZones)))
        {
            var top = FogonRules.LastZone(orderedZones)!;
            return (Loc.F($"🔥 Con tu Fuego Nuevo, a **{gate.Name}** se llega pasando la **mazmorra** de **{top.Name}**: forjá su **Reliquia** y usá `/boss` estando ahí. Al salir de la mazmorra peleás contra **{FogonRules.BossName}** sin curarte."), null);
        }

        var weapon = player.WeaponId is int weaponId && itemRepository is not null ? await itemRepository.GetByIdAsync(weaponId) : null;
        var amulet = player.AmuletId is int amuletId && itemRepository is not null ? await itemRepository.GetByIdAsync(amuletId) : null;
        var check = FogonRules.CheckEntry(orderedZones, gate, player.Level, player.HighestZoneCleared, weapon, amulet);

        switch (check.Status)
        {
            case FogonRules.EntryStatus.NotOpen:
                var last = FogonRules.LastZone(orderedZones);
                return (Loc.F($"🔒 **{gate.Name}** {gate.Emoji} se abre cuando vencés al jefe de **{last?.Name ?? Loc.T("la última zona")}** — probá `/boss` estando ahí."), null);
            case FogonRules.EntryStatus.LevelTooLow:
                return (Loc.F($"🚫 **{gate.Name}** requiere nivel **{gate.MinLevel}** — todavía sos nivel {player.Level}. Seguí subiendo antes de entrar."), null);
            case FogonRules.EntryStatus.MissingGear:
                return (Loc.F($"🔥 Para entrar a **{gate.Name}** tenés que llevar PUESTOS {FogonRules.MissingText(check)}. Se forjan en la herrería (`/forge`) y quedan equipados solos; ") +
                    Loc.T("los de la zona anterior hay que venderlos primero. Mirá cómo funciona con `/info tema:fogon`."), null);
        }

        if (player.InGate)
        {
            return (Loc.F($"Ya estás en **{gate.Name}** {gate.Emoji}: enfrentá a **{FogonRules.BossName}** con `/boss`."), null);
        }

        await userRepository.SetInGateAsync(discordId, true);

        return (null, new EmbedBuilder()
            .WithTitle(Loc.F($"🔥 ¡Entraste a {gate.Name}!"))
            .WithColor(Color.DarkOrange)
            .WithDescription(Loc.F($"{gate.Description}\n\nAcá no hay cacería, viajes ni jefe en equipo. Enfrentá a **{FogonRules.BossName}** con **/boss**.\nPara salir, viajá a una zona con **/zona**."))
            .Build());
    }

    private static Embed BuildTravelEmbed(Zone zone)
    {
        return new EmbedBuilder()
            .WithTitle(Loc.T("🗺️ ¡Viaje completado!"))
            .WithColor(Color.Teal)
            .WithDescription(Loc.F($"Llegaste a **{zone.Name}** {zone.Emoji}.\n\n_{zone.Description}_"))
            .AddField(Loc.T("📊 Nivel requerido"), zone.MinLevel.ToString(), true)
            .WithFooter(Loc.T("A partir de ahora, /hunt caza monstruos de esta zona."))
            .Build();
    }

    // Estático por el mismo motivo que ExecuteTravelAsync — reusado por "aa zonas".
    public static async Task<Embed> BuildZoneListEmbedAsync(IUserRepository userRepository, IZoneRepository zoneRepository, ulong discordId)
    {
        var player = await userRepository.GetOrCreateUserAsync(discordId);
        var everyZone = await zoneRepository.GetAllAsync();
        var zones = ZoneRanking.ForPlayer(everyZone, player.FuegoNuevo);

        var embed = new EmbedBuilder()
            .WithTitle(Loc.T("🗺️ Zonas de Asado y Acero"))
            .WithColor(Color.Teal)
            .WithDescription(Loc.T("Usá `/zona <id>` (o `aa zona <id>`) para viajar. `/hunt` siempre caza en tu zona actual."));

        foreach (var zone in zones)
        {
            string here = zone.ZoneId == player.CurrentZoneId && !player.InGate ? Loc.T(" 📍 _(acá estás)_") : string.Empty;
            string locked = player.Level < zone.MinLevel ? " 🔒" : string.Empty;
            embed.AddField(
                $"{zone.Emoji} {zone.ZoneId}. {zone.Name}{here}{locked}",
                ZoneRanking.IsDungeonZone(zone) ? Loc.F($"Nivel mínimo: **{zone.MinLevel}** · sin jefe: se sale por su **mazmorra**\n_{zone.Description}_") : Loc.F($"Nivel mínimo: **{zone.MinLevel}**\n_{zone.Description}_"),
                false);
        }

        // La próxima zona que abre el Fuego Nuevo (v0.47.0): se anuncia, sin contar qué tiene.
        if (player.FuegoNuevo > 0 && ZoneRanking.NextLockedByFuegoNuevo(everyZone, player.FuegoNuevo) is { } next)
        {
            embed.AddField($"🔒 {next.ZoneId}. {next.Name}", Loc.F($"Se abre con el **Fuego Nuevo {next.MinFuegoNuevo}**."), false);
        }

        // El Fogón Eterno (zona 0): aparece recién cuando vencés al jefe de la última zona (hasta entonces es una sorpresa).
        var gate = await zoneRepository.GetGateAsync();
        if (gate is not null && FogonRules.IsGateOpen(ZoneRanking.OrderByDifficulty(zones), player.HighestZoneCleared))
        {
            string here = player.InGate ? Loc.T(" 📍 _(acá estás)_") : string.Empty;
            string locked = player.Level < gate.MinLevel ? " 🔒" : string.Empty;
            embed.AddField(
                $"{gate.Emoji} {gate.ZoneId}. {gate.Name}{here}{locked}",
                Loc.F($"Nivel mínimo: **{gate.MinLevel}** · solo con el **{FogonRules.WeaponName}** y la **{FogonRules.AmuletName}** puestos\n_{gate.Description}_"),
                false);
        }

        return embed.Build();
    }
}
