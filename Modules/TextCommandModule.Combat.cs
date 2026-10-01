using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

// Segunda parte de TextCommandModule (ver el comentario en TextCommandModule.cs): comandos de
// combate. Sin [Group]/constructor propio — misma clase de C#, las dependencias inyectadas en
// TextCommandModule.cs quedan accesibles acá también.
public partial class TextCommandModule
{
    // "aa hunt" / "aa h" — misma lógica que AdventureModule.HandleHuntAsync.
    [Command("hunt")]
    [Alias("h")]
    [Summary("Salí a cazar monstruos cercanos (cooldown de 1 minuto).")]
    public Task HuntAsync() => StartCombatAsync(CooldownCatalog.Hunt, () => combatStarter.PrepareHuntAsync(Context.User.Id));

    // "aa travel" / "aa t" — misma lógica que AdventureModule.HandleTravelAsync.
    [Command("travel")]
    [Alias("t")]
    [Summary("Enfrentá al monstruo élite de tu zona: más difícil, recompensa x10 (cooldown de 10 minutos).")]
    public Task TravelAsync() => StartCombatAsync(CooldownCatalog.Travel, () => combatStarter.PrepareTravelAsync(Context.User.Id));

    // "aa boss" — misma lógica que AdventureModule.HandleBossAsync.
    [Command("boss")]
    [Summary("Enfrentá al Jefe de tu zona actual (cooldown de 30 minutos).")]
    public Task BossAsync() => StartCombatAsync(CooldownCatalog.Boss, () => combatStarter.PrepareBossAsync(Context.User.Id));

    // "aa raid" — misma lógica que RaidModule.HandleRaidAsync. Solo ARRANCA el lobby: los botones
    // (Unirse / Empezar ya / Atacar / Huir) siempre llegan como interacción de componente, sin
    // importar si el mensaje nació de un slash command o de este comando de texto.
    [Command("raid")]
    [Summary("Jefe de zona cooperativo: varios jugadores atacan al mismo jefe (mín. 2, cooldown de 30 minutos).")]
    public async Task RaidAsync()
    {
        try
        {
            var rejection = await RaidModule.ValidateStartAsync(
                userRepository, itemRepository, monsterRepository, zoneRepository, combatSessions, raidSessions, Context.User.Id);

            if (rejection is not null)
            {
                await ReplyAsync(rejection.PlainMessage, embed: rejection.Embed);
                return;
            }

            var session = await RaidModule.BuildSessionAsync(
                userRepository, itemRepository, monsterRepository, zoneRepository, Context.User.Id, GameModule.GetDisplayName(Context.User));

            // Igual que StartCombatAsync más abajo: el reply target necesita el mensaje ya enviado
            // para poder editarlo después, así que primero se manda y recién ahí se registra.
            var message = await ReplyAsync(embed: RaidModule.BuildLobbyEmbed(session), components: RaidModule.BuildLobbyButtons(session.RaidId));
            session.ReplyTarget = new MessageCombatReplyTarget(message);

            if (!raidSessions.TryAdd(session) || !raidSessions.TryRegisterParticipant(Context.User.Id, session.RaidId))
            {
                await ReplyAsync("Justo se te adelantó otra acción, probá de nuevo en un toque.");
                return;
            }

            RaidModule.ScheduleLobbyTimeout(session, raidSessions, adventureRepository);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude armar el raid ahora mismo, intentá de nuevo en un momento.");
        }
    }

    // "aa autohunt" / "aa ah" — misma lógica que AutoHuntModule.HandleAutoHuntAsync. Sin botones:
    // resuelve toda la pelea de una y comparte el cooldown de /hunt (no de "aa hunt" en particular,
    // literalmente la misma entrada de la tabla cooldowns).
    [Command("autohunt")]
    [Alias("ah")]
    [Summary("Resuelve una cacería completa de una sola vez, sin botones (comparte cooldown con /hunt).")]
    public async Task AutoHuntAsync()
    {
        try
        {
            var result = await AutoHuntModule.ExecuteAsync(adventureRepository, userRepository, itemRepository, combatStarter, Context.User.Id);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! Algo falló en la auto-cacería, intentá de nuevo en un momento.");
        }
    }

    private async Task StartCombatAsync(CooldownDefinition definition, Func<Task<CombatStartOutcome>> prepare)
    {
        try
        {
            var outcome = await prepare();

            switch (outcome.Status)
            {
                case CombatStartStatus.AlreadyInCombat:
                    await ReplyAsync(AdventureModule.BuildAlreadyInCombatMessage());
                    return;
                case CombatStartStatus.OnCooldown:
                    await ReplyAsync(embed: AdventureModule.BuildCooldownEmbed(definition, outcome.CooldownRemaining!.Value));
                    return;
                case CombatStartStatus.NoHp:
                    await ReplyAsync(embed: AdventureModule.BuildNoHpEmbed());
                    return;
                case CombatStartStatus.NoMonstersInZone:
                    await ReplyAsync(embed: AdventureModule.BuildNoMonstersInZoneEmbed());
                    return;
                case CombatStartStatus.NoBossInZone:
                    await ReplyAsync(embed: AdventureModule.BuildNoBossInZoneEmbed());
                    return;
                case CombatStartStatus.NotLeveledForBoss:
                    await ReplyAsync(embed: AdventureModule.BuildNotLeveledForBossEmbed(outcome.RequiredLevel!.Value));
                    return;
                case CombatStartStatus.RaceLost:
                    await ReplyAsync("Justo se te adelantó otra ejecución de este comando, probá de nuevo en un toque.");
                    return;
            }

            var state = outcome.State!;

            // A diferencia del slash command, acá el mensaje tiene que existir ANTES de poder
            // armar el "reply target" (necesita el IUserMessage ya enviado para poder editarlo
            // después) — los botones que dispare esta pelea funcionan igual que los de /hunt,
            // porque un click de botón siempre llega como interacción sin importar el origen.
            var message = await ReplyAsync(
                embed: AdventureModule.BuildEncounterEmbed(state),
                components: AdventureModule.BuildCombatButtons(state, await AdventureModule.LoadHealOptionsAsync(inventoryRepository, Context.User.Id, state)));

            if (!combatSessions.TryStart(Context.User.Id, state, new MessageCombatReplyTarget(message)))
            {
                await ReplyAsync(AdventureModule.BuildAlreadyInCombatMessage());
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await ReplyAsync("¡Upa! Algo falló iniciando tu aventura, intentá de nuevo en un momento.");
        }
    }

    // Nota: no hay "aa attack"/"aa flee" — una vez que la pelea arrancó (por /hunt o "aa hunt"),
    // se sigue exclusivamente con los botones "Atacar"/"Huir" del mensaje (o "aa use" para curarte
    // con un consumible sin perder el turno de otra forma), no hay forma de atacar por texto.
}
