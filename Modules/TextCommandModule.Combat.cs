using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord;
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
    [Summary("Enfrentá al monstruo élite de tu zona: más difícil, recompensa x30 (cooldown de 30 minutos).")]
    public Task TravelAsync() => StartCombatAsync(CooldownCatalog.Travel, () => combatStarter.PrepareTravelAsync(Context.User.Id));

    // "aa boss" / "aa boss @amigo @otro" — misma lógica que AdventureModule.HandleBossAsync. Sin menciones es la pelea de siempre contra el jefe; con una o más (hasta 5) arma un grupo que
    // pega por turnos (v0.19.0, reemplaza al viejo "aa raid"). Solo ARRANCA la invitación: los botones (Aceptar / Rechazar / Empezar ya / Atacar / Huir) siempre llegan como interacción
    // de componente, sin importar si el mensaje nació de un slash command o de este comando de texto.
    [Command("boss")]
    [Summary("Enfrentá al Jefe de tu zona, solo o con amigos: \"aa boss @amigo\" (hasta 5, misma zona máxima, pegan por turnos). Cooldown de 5 horas; si perdés o huís, 30 minutos.")]
    public async Task BossAsync(params IUser[] companions)
    {
        if (companions.Length == 0)
        {
            await StartCombatAsync(CooldownCatalog.Boss, () => combatStarter.PrepareBossAsync(Context.User.Id));
            return;
        }

        try
        {
            if (companions.Any(c => c.IsBot))
            {
                await ReplyAsync("Los bots no pelean jefes 🤖. Mencioná a otros jugadores.");
                return;
            }

            var preparation = await partyBoss.PrepareAsync(
                Context.User.Id, GameModule.GetDisplayName(Context.User),
                companions.Select(c => new RaidModule.PartyMember(c.Id, GameModule.GetDisplayName(c))).ToList());

            if (preparation.Session is not { } session)
            {
                await ReplyAsync(preparation.Rejection!.PlainMessage, embed: preparation.Rejection.Embed);
                return;
            }

            // Igual que StartCombatAsync más abajo: el reply target necesita el mensaje ya enviado
            // para poder editarlo después, así que primero se manda y recién ahí se registra.
            var message = await ReplyAsync(RaidModule.BuildInviteMentions(session), embed: RaidModule.BuildLobbyEmbed(session), components: RaidModule.BuildLobbyButtons(session.RaidId));
            session.ReplyTarget = new MessageCombatReplyTarget(message);

            if (!partyBoss.TryOpen(session, Context.Channel.Id))
            {
                await session.ReplyTarget.ClearComponentsAsync();
                await ReplyAsync("Justo se te adelantó otra acción, probá de nuevo en un toque.");
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude armar el grupo ahora mismo, intentá de nuevo en un momento.");
        }
    }

    // "aa raid" — el viejo raid abierto ya no existe (v0.19.0): el jefe en equipo es "aa boss @amigo". Queda el comando para que quien lo escribía por costumbre sepa qué pasó.
    [Command("raid")]
    [Summary("Ya no existe: para pelear el jefe con amigos usá \"aa boss @amigo\".")]
    public Task RaidAsync() =>
        ReplyAsync("El raid ahora es el jefe en equipo: escribí **aa boss @amigo** (o **/boss** nombrando a tus compañeros). Pegan por turnos y tienen que tener la misma zona máxima.");

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
            var result = await AutoHuntModule.ExecuteAsync(adventureRepository, userRepository, itemRepository, combatStarter, gameEvents, Context.User.Id, bonusService);
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
                case CombatStartStatus.InGate:
                    await ReplyAsync(embed: AdventureModule.BuildInGateEmbed());
                    return;
                case CombatStartStatus.GateGearMissing:
                    await ReplyAsync(embed: AdventureModule.BuildGateGearMissingEmbed());
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
                components: AdventureModule.BuildCombatButtons(state, await AdventureModule.LoadHealOptionsAsync(inventoryRepository, buffRepository, Context.User.Id, state)));

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
