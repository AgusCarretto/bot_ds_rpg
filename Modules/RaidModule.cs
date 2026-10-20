using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// El jefe de zona EN EQUIPO ("/boss @amigo" o "aa boss @amigo"; hasta v0.18.x era "/raid"): varios jugadores atacan al MISMO jefe compartido (HP en
// Services/IRaidSessionService.cs). Desde la v0.19.0 se pega POR TURNOS: pega uno, el jefe le responde a ESE jugador, y le toca al siguiente (en el orden en que se mencionó a
// los invitados); un clic de quien no tiene el turno se rechaza dentro del lock, y quien no juega en 60 s pierde el turno (dos seguidos lo retiran). El grupo se arma por
// INVITACIÓN: quien arranca nombra a sus compañeros, y solo ellos pueden aceptar; todos tienen que tener la MISMA zona máxima (ZoneRanking.MaxUnlockedRank) o la invitación se
// rechaza. Reusa toda la matemática de combate solitario (GameData/CombatTurnResolver.cs) y la misma recompensa que /boss (GameData/CombatRewardCalculator.cs, IAdventureRepository.
// ApplyBossVictoryAsync): cada participante que contribuyó se lleva su propia recompensa COMPLETA al ganar, no se reparte entre el grupo. Ver Modules/AdventureModule.cs para el
// equivalente solitario del que esto deriva. (El nombre interno sigue siendo "raid": las pruebas, los eventos y los ids de los botones lo usan.)
public class RaidModule(
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IZoneRepository zoneRepository,
    IAdventureRepository adventureRepository,
    ICombatSessionService combatSessions,
    IRaidSessionService raidSessions,
    IGameEvents gameEvents,
    IBuffRepository buffRepository,
    ICooldownRepository cooldownRepository,
    IPlayerBonusService bonusService,
    IZoneBoxService zoneBoxService,
    IInventoryRepository inventoryRepository) : InteractionModuleBase<SocketInteractionContext>
{
    // Ver Services/RaidSettings.cs: el mínimo es configurable (2 por defecto).
    private static int MinParticipantsToStart => RaidSettings.MinParticipants;

    // Los recordatorios de cooldown (Services/ReminderService.cs): los participantes de un raid no tienen todos una interacción propia al terminar, así que el raid les arma el aviso del jefe
    // al arrancar y al resolverse. Lo asigna Program.cs al arrancar; nulo (las pruebas) = no hace nada.
    public static IReminderService? Reminders { get; set; }

    private static Task SyncRemindersAsync(RaidSession session, IReadOnlyCollection<ulong> players) =>
        Reminders is { } reminders && session.ChannelId != 0
            ? reminders.SyncManyAsync(players, session.ChannelId, "raid")
            : Task.CompletedTask;
    private const int MaxParticipants = RaidSettings.MaxParticipants;

    // Cuánto esperan las invitaciones (Services/RaidSettings.cs).
    public static TimeSpan LobbyDuration => RaidSettings.InviteWindow;

    // Los cuatro botones de la invitación. Cada uno responde en privado (ephemeral) y delega en un método estático sin Context (los *CoreAsync de abajo), que es lo que prueban los arneses.
    // El id del botón "Aceptar" sigue llamándose "raid_join" desde que era una sala abierta. Solo puede aceptar quien fue nombrado al armar el grupo.
    [ComponentInteraction("raid_join:*")]
    public async Task HandleJoinAsync(string raidIdRaw)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            if (!Guid.TryParse(raidIdRaw, out var raidId) || raidSessions.Peek(raidId) is not { } session)
            {
                await FollowupAsync("Esa invitación ya no existe.", ephemeral: true);
                return;
            }

            string reply = await AcceptInviteCoreAsync(
                session, Context.User.Id, GameModule.GetDisplayName(Context.User),
                userRepository, itemRepository, zoneRepository, combatSessions, raidSessions, cooldownRepository, buffRepository, adventureRepository, bonusService);
            await FollowupAsync(reply, ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No te pude sumar al grupo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction("raid_decline:*")]
    public async Task HandleDeclineAsync(string raidIdRaw)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            if (!Guid.TryParse(raidIdRaw, out var raidId) || raidSessions.Peek(raidId) is not { } session)
            {
                await FollowupAsync("Esa invitación ya no existe.", ephemeral: true);
                return;
            }

            await FollowupAsync(await DeclineInviteCoreAsync(session, Context.User.Id, raidSessions, adventureRepository), ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude registrar tu respuesta, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction("raid_start:*")]
    public async Task HandleStartNowAsync(string raidIdRaw)
    {
        await DeferAsync();

        try
        {
            if (!Guid.TryParse(raidIdRaw, out var raidId) || raidSessions.Peek(raidId) is not { } session)
            {
                await FollowupAsync("Ese grupo ya no existe.", ephemeral: true);
                return;
            }

            // Si arranca, el mensaje del grupo se edita solo (session.ReplyTarget); si no, el motivo va en privado.
            if (await StartNowCoreAsync(session, Context.User.Id, raidSessions, adventureRepository) is { } reason)
            {
                await FollowupAsync(reason, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! Algo falló arrancando el combate, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction("raid_cancel:*")]
    public async Task HandleCancelAsync(string raidIdRaw)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            if (!Guid.TryParse(raidIdRaw, out var raidId) || raidSessions.Peek(raidId) is not { } session)
            {
                await FollowupAsync("Ese grupo ya no existe.", ephemeral: true);
                return;
            }

            await FollowupAsync(await CancelPartyCoreAsync(session, Context.User.Id, raidSessions), ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude cancelar el grupo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // ---- La lógica de los botones de la invitación, sin Context. Devuelven lo que se le dice en privado a quien hizo clic. ----

    // Aceptar: solo quien fue nombrado, y se vuelve a mirar TODO (pasó tiempo desde la invitación: zona máxima, cooldown, vida...). Si ahora no puede, queda afuera del grupo diciéndole por qué
    // (y el grupo sigue sin él). Cuando contesta el último invitado, el combate arranca solo.
    public static async Task<string> AcceptInviteCoreAsync(
        RaidSession session, ulong discordId, string displayName,
        IUserRepository userRepository, IItemRepository itemRepository, IZoneRepository zoneRepository, ICombatSessionService combatSessions,
        IRaidSessionService raidSessions, ICooldownRepository cooldownRepository, IBuffRepository buffRepository, IAdventureRepository adventureRepository, IPlayerBonusService? bonusService)
    {
        bool alreadyIn, invited;
        RaidPhase phase;
        lock (session.Lock)
        {
            alreadyIn = session.Participants.Any(p => p.DiscordId == discordId);
            invited = session.Invited.Any(i => i.DiscordId == discordId) && !session.Declined.Contains(discordId);
            phase = session.Phase;
        }

        // Va antes del resto: quien ya está adentro (ej. quien armó el grupo) figura como "ocupado" y recibiría el mensaje engañoso de "ya estás en medio de un combate".
        if (alreadyIn)
        {
            return "Ya estás adentro del grupo.";
        }

        if (!invited)
        {
            return "Esta invitación no es para vos. Para pelear el jefe con amigos usá **/boss** nombrándolos.";
        }

        if (phase != RaidPhase.Lobby)
        {
            return "La invitación ya venció o el combate ya arrancó.";
        }

        string? problem = await CompanionProblemAsync(userRepository, zoneRepository, combatSessions, raidSessions, cooldownRepository, discordId, session.ZoneId, session.MaxZoneRank);
        if (problem is not null)
        {
            await DeclineAsync(session, discordId, raidSessions, adventureRepository);
            return $"No podés sumarte: {problem}";
        }

        var participant = await BuildParticipantAsync(userRepository, itemRepository, buffRepository, discordId, displayName, bonusService);

        bool joined;
        lock (session.Lock)
        {
            joined = session.Phase == RaidPhase.Lobby
                && session.Participants.Count < MaxParticipants
                && session.Participants.All(p => p.DiscordId != discordId);

            if (joined)
            {
                session.Participants.Add(participant);
            }
        }

        if (!joined)
        {
            return "No te pudiste sumar (el combate ya arrancó, el grupo está lleno, o ya estabas adentro).";
        }

        if (!raidSessions.TryRegisterParticipant(discordId, session.RaidId))
        {
            lock (session.Lock)
            {
                session.Participants.Remove(participant);
            }

            return AdventureModule.BuildAlreadyInCombatMessage();
        }

        await session.ReplyTarget.UpdateAsync(BuildLobbyEmbed(session), BuildLobbyButtons(session.RaidId));

        // Si ya contestaron todos los invitados no hace falta esperar el tiempo que queda.
        await StartIfEveryoneAnsweredAsync(session, raidSessions, adventureRepository);
        return $"Aceptaste: vas a pelear contra **{session.BossName}** {session.BossEmoji}.";
    }

    public static async Task<string> DeclineInviteCoreAsync(RaidSession session, ulong discordId, IRaidSessionService raidSessions, IAdventureRepository adventureRepository)
    {
        bool invited, alreadyIn;
        lock (session.Lock)
        {
            invited = session.Invited.Any(i => i.DiscordId == discordId);
            alreadyIn = session.Participants.Any(p => p.DiscordId == discordId);
        }

        if (alreadyIn)
        {
            return "Ya aceptaste: si te querés ir, usá **Huir** cuando arranque.";
        }

        if (!invited)
        {
            return "Esta invitación no es para vos.";
        }

        await DeclineAsync(session, discordId, raidSessions, adventureRepository);
        return "Rechazaste la invitación.";
    }

    // Empezar ya: solo quien armó el grupo, y arranca con quienes ya aceptaron (hace falta el mínimo). Devuelve null si arrancó, o el motivo si no.
    public static async Task<string?> StartNowCoreAsync(RaidSession session, ulong discordId, IRaidSessionService raidSessions, IAdventureRepository adventureRepository)
    {
        if (discordId != session.StarterId)
        {
            return "Solo quien armó el grupo puede empezar antes de tiempo.";
        }

        int count;
        lock (session.Lock)
        {
            count = session.Participants.Count;
        }

        if (count < MinParticipantsToStart)
        {
            return $"Hace falta un mínimo de {MinParticipantsToStart} jugadores para arrancar — por ahora hay {count}.";
        }

        await TryActivateAsync(session, raidSessions, adventureRepository);
        return null;
    }

    // Cancelar: quien armó el grupo se arrepiente antes de que arranque. Se cierra todo sin gastar el cooldown de nadie (todavía no se reclamó ninguno).
    public static async Task<string> CancelPartyCoreAsync(RaidSession session, ulong discordId, IRaidSessionService raidSessions)
    {
        if (discordId != session.StarterId)
        {
            return "Solo quien armó el grupo puede cancelarlo.";
        }

        bool cancelled;
        lock (session.Lock)
        {
            cancelled = session.Phase == RaidPhase.Lobby;
            if (cancelled)
            {
                session.Phase = RaidPhase.Resolved;
            }
        }

        if (!cancelled)
        {
            return "El combate ya arrancó: si te querés ir, usá **Huir**.";
        }

        session.TimeoutCts?.Cancel();
        raidSessions.Remove(session.RaidId);
        await session.ReplyTarget.UpdateAsync(BuildCancelledByStarterEmbed(session), new ComponentBuilder().Build());
        return "Cancelaste el grupo.";
    }

    [ComponentInteraction("raid_attack:*")]
    public Task HandleAttackAsync(string raidIdRaw) => ResolveClickAsync(raidIdRaw, useAbility: false);

    // Botón genérico "Habilidad": el mensaje del raid es compartido por todos los participantes,
    // así que no puede llevar una etiqueta distinta por jugador — cuál habilidad se ejecuta depende
    // de la clase de quien clickea (el estado de cada uno se ve en el roster del embed).
    [ComponentInteraction("raid_ability:*")]
    public Task HandleAbilityAsync(string raidIdRaw) => ResolveClickAsync(raidIdRaw, useAbility: true);

    private async Task ResolveClickAsync(string raidIdRaw, bool useAbility)
    {
        await DeferAsync();

        var output = new InteractionRaidTurnOutput(Context.Interaction);
        if (!Guid.TryParse(raidIdRaw, out var parsedRaidId))
        {
            await output.NoteAsync("Ese combate ya no existe.");
            return;
        }

        await ResolveAttackCoreAsync(raidSessions, userRepository, itemRepository, adventureRepository, gameEvents, zoneBoxService, parsedRaidId, Context.User.Id, useAbility, output);
    }

    // El ataque o la habilidad de un participante del raid, SIN dependencia de Context: lo usan el botón (edita el mensaje compartido) y los comandos de texto "aa attack" / "aa ability"
    // (mensaje nuevo con botones nuevos, ver Services/RaidTurnOutput.cs). La resolución del turno es la misma (ResolveParticipantTurn).
    public static async Task ResolveAttackCoreAsync(
        IRaidSessionService raidSessions, IUserRepository userRepository, IItemRepository itemRepository, IAdventureRepository adventureRepository,
        IGameEvents gameEvents, IZoneBoxService zoneBoxService, Guid raidId, ulong discordId, bool useAbility, IRaidTurnOutput output)
    {
        try
        {
            if (raidSessions.Peek(raidId) is not { } session)
            {
                await output.NoteAsync("Ese combate ya no existe.");
                return;
            }

            AttackOutcome outcome;

            lock (session.Lock)
            {
                if (session.Phase != RaidPhase.Active)
                {
                    outcome = new AttackOutcome(AttackOutcomeKind.NotActive);
                }
                else
                {
                    var participant = session.Participants.FirstOrDefault(p => p.DiscordId == discordId);
                    if (participant is null)
                    {
                        outcome = new AttackOutcome(AttackOutcomeKind.NotAParticipant);
                    }
                    else if (!participant.IsActive)
                    {
                        outcome = new AttackOutcome(AttackOutcomeKind.ParticipantInactive);
                    }
                    else if (!IsTurnOf(session, participant))
                    {
                        // Fuera de turno: no se toca NADA (ni el jefe ni el reloj), solo se avisa de quién es.
                        outcome = new AttackOutcome(AttackOutcomeKind.NotYourTurn, session.Participants[session.TurnIndex].DisplayName);
                    }
                    else if (session.EatingTurnNumber == session.TurnNumber)
                    {
                        // Está comiendo (ExecuteEatAsync reservó el turno): no se juegan dos acciones en el mismo turno.
                        outcome = new AttackOutcome(AttackOutcomeKind.Busy);
                    }
                    else
                    {
                        outcome = ResolveParticipantTurn(session, participant, useAbility);

                        // Jugó: se le perdona el aviso de turno vencido, y si la pelea sigue le toca al que viene (en el mismo lock, así dos clics seguidos nunca juegan el mismo turno).
                        if (outcome.Kind != AttackOutcomeKind.AbilityUnavailable)
                        {
                            participant.MissedTurns = 0;
                        }

                        if (outcome.Kind == AttackOutcomeKind.Continues)
                        {
                            AdvanceTurn(session);
                        }
                    }
                }
            }

            switch (outcome.Kind)
            {
                case AttackOutcomeKind.NotActive:
                    await output.NoteAsync("Este combate todavía no arrancó (o ya terminó).");
                    return;
                case AttackOutcomeKind.NotAParticipant:
                    await output.NoteAsync("No sos parte de este combate: el grupo se arma con invitaciones (**/boss** nombrando a tus amigos).");
                    return;
                case AttackOutcomeKind.ParticipantInactive:
                    await output.NoteAsync("Ya no podés seguir peleando en este combate (te derribaron o te retiraste).");
                    return;
                case AttackOutcomeKind.Busy:
                    await output.NoteAsync("🍖 Estás comiendo: esperá a que termine tu turno.");
                    return;
                case AttackOutcomeKind.NotYourTurn:
                    await output.NoteAsync($"⏳ Todavía no es tu turno: le toca a **{outcome.LogLine}**.");
                    return;
                case AttackOutcomeKind.AbilityUnavailable:
                    await output.NoteAsync(outcome.LogLine!);
                    return;
                case AttackOutcomeKind.Continues:
                    StartTurnTimer(session);
                    await output.ShowAsync(session, BuildCombatEmbed(session, outcome.LogLine!), BuildCombatButtons(raidId));
                    await PingTurnAsync(session, justPlayed: discordId);
                    return;
                case AttackOutcomeKind.Victory:
                    StopTurnTimer(session);
                    await output.PrepareFinalAsync(session);
                    await ClearTurnPingAsync(session);
                    await ResolveVictoryAsync(session, outcome.LogLine!, userRepository, itemRepository, adventureRepository, raidSessions, gameEvents, zoneBoxService);
                    return;
                case AttackOutcomeKind.Wipe:
                    StopTurnTimer(session);
                    await output.PrepareFinalAsync(session);
                    await ClearTurnPingAsync(session);
                    await ResolveWipeAsync(session, outcome.LogLine!, userRepository, raidSessions, gameEvents);
                    return;
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await output.NoteAsync("¡Upa! Algo falló procesando tu ataque, intentá de nuevo en un momento.");
        }
    }

    [ComponentInteraction("raid_flee:*")]
    public async Task HandleFleeAsync(string raidIdRaw)
    {
        await DeferAsync();

        var output = new InteractionRaidTurnOutput(Context.Interaction);
        if (!Guid.TryParse(raidIdRaw, out var parsedRaidId))
        {
            await output.NoteAsync("Ese combate ya no existe.");
            return;
        }

        await ResolveFleeCoreAsync(raidSessions, userRepository, parsedRaidId, Context.User.Id, output, gameEvents);
    }

    // Retirarse del combate en equipo (el botón "Huir" o "aa flee"), SIN dependencia de Context. Se puede huir en cualquier momento, también fuera de turno. gameEvents es opcional (solo para
    // anotar la derrota de un caído si al irse este jugador termina el combate, ver SettleKnockedOutAsync).
    public static async Task ResolveFleeCoreAsync(
        IRaidSessionService raidSessions, IUserRepository userRepository, Guid raidId, ulong discordId, IRaidTurnOutput output, IGameEvents? gameEvents = null)
    {
        try
        {
            if (raidSessions.Peek(raidId) is not { } session)
            {
                await output.NoteAsync("Ese combate ya no existe.");
                return;
            }

            var retired = await RetireAsync(session, discordId, userRepository, raidSessions);
            if (retired.Participant is null)
            {
                await output.NoteAsync("No podés retirarte de este combate ahora mismo.");
                return;
            }

            if (retired.RaidEnded)
            {
                StopTurnTimer(session);
                await output.PrepareFinalAsync(session);
                await ClearTurnPingAsync(session);
                await SettleKnockedOutAsync(session, userRepository, gameEvents);
                await session.ReplyTarget.UpdateAsync(BuildAbandonedEmbed(session), new ComponentBuilder().Build());
                return;
            }

            // Si le tocaba a quien se fue, ya pasó el turno al siguiente: se arma el reloj del nuevo.
            if (retired.TurnPassed)
            {
                StartTurnTimer(session);
            }

            await output.ShowAsync(session, BuildCombatEmbed(session, $"🏃 **{retired.Participant.DisplayName}** se retiró del combate."), BuildCombatButtons(raidId));

            if (retired.TurnPassed)
            {
                await PingTurnAsync(session);
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await output.NoteAsync("¡Upa! No te pude retirar del combate, intentá de nuevo en un momento.");
        }
    }

    // ============================================================
    // Comer en el jefe en equipo (v0.19.0, a pedido del dueño). El /use suelto ya no sirve ahí (curaba la vida de la base y no la de la pelea, que vive en memoria): ahora comer es una ACCIÓN
    // del combate, como en /travel y /boss — gasta el turno (el jefe te pega igual), cura de verdad y es una vez por jugador (RaidSettings.MealsPerFight). Se hace con el botón 🍖 Comer o escribiendo
    // /use (o "aa use") con la comida, que desde acá llega a ExecuteEatAsync. Los banquetes (Míticos) también valen: además de curar dan su +% de ataque en el acto (se come igual que en el jefe solo: vale con la vida llena, ocupa la comida de la pelea y el buff queda guardado).
    // ============================================================

    // El botón "Comer": abre en privado la lista de comidas de QUIEN toca (el mensaje del combate es compartido y no puede llevar la lista de nadie). Elegir una es ExecuteEatAsync.
    [ComponentInteraction("raid_eat:*")]
    public async Task HandleEatAsync(string raidIdRaw)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            if (!Guid.TryParse(raidIdRaw, out var raidId) || raidSessions.Peek(raidId) is not { } session)
            {
                await FollowupAsync("Ese combate ya no existe.", ephemeral: true);
                return;
            }

            if (CheckEat(session, Context.User.Id, claim: false).Problem is { } problem)
            {
                await FollowupAsync(problem, ephemeral: true);
                return;
            }

            var owned = await inventoryRepository.GetOwnedByTypeAsync(Context.User.Id, "Consumable");
            var buffs = await buffRepository.GetItemBuffsAsync();
            var options = CombatHeal.BuildOptions(owned.Select(o => new HealOption(
                    o.Item.Name, o.Item.StatValue, o.Quantity, buffs.TryGetValue(o.Item.ItemId, out var buff) ? buff.AttackPercent : 0)))
                .ToList();

            // Con la vida llena solo tiene sentido un banquete (su gracia es el +% de ataque), igual que en el jefe solo.
            bool atFullHp;
            lock (session.Lock)
            {
                var me = session.Participants.FirstOrDefault(p => p.DiscordId == Context.User.Id);
                atFullHp = me is not null && me.CurrentHp >= me.MaxHp;
            }

            if (atFullHp)
            {
                options = options.Where(o => o.BuffPercent > 0).ToList();
            }

            if (options.Count == 0)
            {
                await FollowupAsync(
                    atFullHp ? "Ya estás al máximo de HP: solo un banquete tendría sentido ahora (por el +% de ataque) y no tenés." : "No tenés comida para curarte en la pelea.", ephemeral: true);
                return;
            }

            // Sin emoji en las opciones a propósito (si uno custom no le es accesible al bot, Discord rechaza el mensaje ENTERO).
            var menu = new SelectMenuBuilder().WithCustomId($"raid_eatpick:{raidId}").WithPlaceholder("🍖 Elegí qué comer (gasta tu turno)").WithMinValues(1).WithMaxValues(1);
            foreach (var option in options)
            {
                menu.AddOption(option.Name, option.Name, CombatHeal.Describe(option));
            }

            await FollowupAsync(
                $"🍖 Comer te cura pero **gasta tu turno** (el jefe te pega igual) y es **{RaidSettings.MealsPerFight} vez por pelea**.",
                components: new ComponentBuilder().WithSelectMenu(menu).Build(), ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude armar la lista de comida, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // La comida elegida en la lista privada de arriba: se juega el turno y el resultado reemplaza a la lista (el mensaje del combate lo actualiza ExecuteEatAsync).
    [ComponentInteraction("raid_eatpick:*")]
    public async Task HandleEatPickAsync(string raidIdRaw, string[] selected)
    {
        await DeferAsync();

        try
        {
            string reply;
            if (selected.Length == 0)
            {
                reply = "No elegiste ninguna comida.";
            }
            else if (!Guid.TryParse(raidIdRaw, out var raidId) || raidSessions.RaidIdOf(Context.User.Id) != raidId)
            {
                reply = "Ese combate ya no existe.";
            }
            else
            {
                reply = await ExecuteEatAsync(raidSessions, userRepository, itemRepository, inventoryRepository, buffRepository, gameEvents, Context.User.Id, selected[0]);
            }

            await Context.Interaction.ModifyOriginalResponseAsync(props =>
            {
                props.Content = reply;
                props.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude usar esa comida, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Lo que impide (o no) comer ahora: el combate tiene que estar peleándose, ser parte y seguir en pie, tener el turno, no haber comido ya y no estar justo comiendo. Con claim reserva el turno
    // (EatingTurnNumber) dentro del mismo lock: gastar la comida en la base es I/O y no puede ir adentro, y mientras tanto ni su ataque ni el reloj tocan ese turno.
    private static (string? Problem, RaidParticipant? Participant, int Turn) CheckEat(RaidSession session, ulong discordId, bool claim)
    {
        lock (session.Lock)
        {
            if (session.Phase != RaidPhase.Active)
            {
                return ("Este combate todavía no arrancó (o ya terminó).", null, 0);
            }

            var participant = session.Participants.FirstOrDefault(p => p.DiscordId == discordId);
            if (participant is null)
            {
                return ("No sos parte de este combate.", null, 0);
            }

            if (!participant.IsActive)
            {
                return ("Ya no podés seguir peleando en este combate (te derribaron o te retiraste).", null, 0);
            }

            if (!IsTurnOf(session, participant))
            {
                return ($"⏳ Todavía no es tu turno: le toca a **{session.Participants[session.TurnIndex].DisplayName}**. Comer gasta tu turno.", null, 0);
            }

            if (participant.MealsEaten >= RaidSettings.MealsPerFight)
            {
                return ("🍖 Ya comiste en esta pelea: solo se puede **una vez** por combate.", null, 0);
            }

            if (session.EatingTurnNumber == session.TurnNumber)
            {
                return ("🍖 Ya estás comiendo, esperá un toque.", null, 0);
            }

            if (claim)
            {
                session.EatingTurnNumber = session.TurnNumber;
            }

            return (null, participant, session.TurnNumber);
        }
    }

    // Si la comida no llega a gastarse, se devuelve el turno y se vuelve a armar el reloj (el que venció mientras tanto vio la reserva y no hizo nada).
    private static void ReleaseEatClaim(RaidSession session, int turn)
    {
        lock (session.Lock)
        {
            if (session.EatingTurnNumber == turn)
            {
                session.EatingTurnNumber = 0;
            }
        }

        StartTurnTimer(session);
    }

    // Comer en plena pelea en equipo, SIN dependencia de Context (lo usan el botón, /use y "aa use"). Devuelve lo que se le dice en privado a quien come; el mensaje del combate se actualiza solo.
    // El orden es el de siempre: reservar el turno, gastar la comida (guardado: si no la tiene no pasa nada) y recién ahí curar y devolver el golpe del jefe, todo en un solo lock.
    public static async Task<string> ExecuteEatAsync(
        IRaidSessionService raidSessions, IUserRepository userRepository, IItemRepository itemRepository, IInventoryRepository inventoryRepository,
        IBuffRepository buffRepository, IGameEvents gameEvents, ulong discordId, string foodName)
    {
        try
        {
            if (raidSessions.RaidIdOf(discordId) is not { } raidId || raidSessions.Peek(raidId) is not { } session)
            {
                return "No estás en ningún combate en equipo.";
            }

            var item = await itemRepository.GetByNameAsync(foodName);
            if (item is null)
            {
                return $"No encontré ningún ítem llamado **{foodName}**.";
            }

            if (item.Type != "Consumable" || item.StatValue <= 0)
            {
                return $"**{ItemDisplay.Format(item.Emoji, item.Name)}** no sirve para curarte en la pelea.";
            }

            // Un banquete (comida Mítica) además de curar da un buff de ataque (item_buffs): se come igual que en el jefe solo, vale aunque estés al máximo de HP (lo que importa es el buff) y ocupa la comida de la pelea.
            var buff = await buffRepository.GetItemBuffAsync(item.ItemId);

            var (problem, participant, turn) = CheckEat(session, discordId, claim: true);
            if (problem is not null)
            {
                return problem;
            }

            if (buff is null && participant!.CurrentHp >= participant.MaxHp)
            {
                ReleaseEatClaim(session, turn);
                return $"Ya estás al máximo de HP ({participant.CurrentHp}/{participant.MaxHp}), no hace falta comer ahora.";
            }

            bool consumed;
            try
            {
                consumed = await inventoryRepository.TryConsumeAsync(discordId, item.ItemId, 1);
            }
            catch
            {
                ReleaseEatClaim(session, turn);
                throw;
            }

            if (!consumed)
            {
                ReleaseEatClaim(session, turn);
                return $"No tenés **{ItemDisplay.Format(item.Emoji, item.Name)}** en tu inventario.";
            }

            int healed;
            bool dodged, knockedOut, wipe;
            int hit;
            string line;

            lock (session.Lock)
            {
                session.EatingTurnNumber = 0;

                // Se fue (o el combate terminó) JUSTO mientras comía: la comida ya se gastó, la misma ventana angosta que documenta /use en combate solitario (UseModule).
                if (session.Phase != RaidPhase.Active || !participant!.IsActive)
                {
                    return "El combate cambió justo mientras comías, revisá el mensaje.";
                }

                int before = participant.CurrentHp;
                participant.CurrentHp = Math.Min(participant.MaxHp, participant.CurrentHp + item.StatValue);
                healed = participant.CurrentHp - before;

                // El banquete vale YA: se recalcula el ataque desde el que tenía sin buff (así comer otro no apila +15 % sobre +15 %).
                if (buff is not null)
                {
                    participant.Damage = AttackBuff.Rescale(participant.Damage, participant.AttackBuffPercent, buff.AttackPercent);
                    participant.AttackBuffPercent = buff.AttackPercent;
                }

                // Comer cede el turno: el jefe contraataca igual, con los efectos de habilidad que haya activos, y el turno cuenta para el enfriamiento (GameData/CombatTurnResolver.ResolveCounterTurn).
                var counter = CombatTurnResolver.ResolveCounterTurn(
                    new CombatantProfile(participant.Damage, participant.Defense, participant.PlayerClass, participant.Passives), participant.Ability, session.BossDamage);
                participant.Ability = counter.Status;
                dodged = counter.Hit.Dodged;
                hit = counter.Hit.Damage;

                if (dodged)
                {
                    participant.DodgeCount++;
                }
                else
                {
                    participant.CurrentHp = Math.Max(0, participant.CurrentHp - hit);
                    participant.TotalDamageTaken += hit;
                }

                participant.MealsEaten++;
                participant.TurnsTaken++;
                participant.MissedTurns = 0;
                knockedOut = participant.IsKnockedOut;

                wipe = session.Participants.All(p => !p.IsActive);
                if (wipe)
                {
                    session.Phase = RaidPhase.Resolved; // atómico con la decisión, igual que en ResolveParticipantTurn
                }
                else
                {
                    AdvanceTurn(session);
                }

                string who = $"**{participant.DisplayName}**";
                string monsterLine = dodged
                    ? $"💨 {who} esquivó el contraataque."
                    : $"El jefe le devolvió **{hit}** a {who}." + (knockedOut ? $" 💀 {who} quedó derribado." : string.Empty);
                string buffText = buff is null ? string.Empty : $" ⚔️ +{buff.AttackPercent} % de ataque.";
                line = $"🍖 {who} comió {ItemDisplay.Format(item.Emoji, item.Name)} y recuperó **{healed}** HP.{buffText} {monsterLine}";
            }

            if (buff is not null)
            {
                // Igual que /use en el jefe solo: el buff queda activo unos minutos también fuera de la pelea. Si guardarlo falla, el de la pelea ya vale y solo se registra.
                try
                {
                    await buffRepository.ActivateAttackAsync(discordId, buff.AttackPercent, buff.Minutes, item.Name);
                }
                catch (Exception ex)
                {
                    BotLog.Warn(ex);
                }
            }

            if (wipe)
            {
                StopTurnTimer(session);
                await ClearTurnPingAsync(session);
                await ResolveWipeAsync(session, line, userRepository, raidSessions, gameEvents);
            }
            else
            {
                StartTurnTimer(session);
                await session.ReplyTarget.UpdateAsync(BuildCombatEmbed(session, line), BuildCombatButtons(session.RaidId));
                await PingTurnAsync(session, justPlayed: discordId);
            }

            return $"🍖 Comiste {ItemDisplay.Format(item.Emoji, item.Name)} y recuperaste **{healed}** HP." + (buff is null ? string.Empty : $" ⚔️ +{buff.AttackPercent} % de ataque durante {buff.Minutes} minutos.") + (knockedOut ? " Pero el jefe te derribó." : dodged ? " Esquivaste el golpe del jefe." : $" El jefe te pegó **{hit}**.");
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            return "¡Upa! No pude usar esa comida, intentá de nuevo en un momento.";
        }
    }

    // El resultado de sacar a un jugador de la pelea. Participant null = no estaba en condiciones (no es parte, ya estaba afuera, o el combate no está activo).
    private readonly record struct RetireResult(RaidParticipant? Participant, bool RaidEnded, bool TurnPassed);

    // Saca a UN jugador del combate (huyó, o se quedó dormido dos turnos): lo marca, pasa el turno si era el suyo, guarda su vida y lo libera para cazar y viajar. Si era el último en
    // pie, cierra la sesión. El cambio de estado va dentro del lock (nada de I/O ahí); lo demás, después.
    private static async Task<RetireResult> RetireAsync(RaidSession session, ulong discordId, IUserRepository userRepository, IRaidSessionService raidSessions)
    {
        RaidParticipant? participant = null;
        bool raidEnded = false;
        bool turnPassed = false;

        lock (session.Lock)
        {
            var candidate = session.Participants.FirstOrDefault(p => p.DiscordId == discordId);
            if (candidate is not null && candidate.IsActive && session.Phase == RaidPhase.Active)
            {
                bool wasHisTurn = IsTurnOf(session, candidate);
                candidate.HasFled = true;
                participant = candidate;
                raidEnded = session.Participants.All(p => !p.IsActive);
                if (raidEnded)
                {
                    session.Phase = RaidPhase.Resolved;
                }
                else if (wasHisTurn)
                {
                    AdvanceTurn(session);
                    turnPassed = true;
                }
            }
        }

        if (participant is null)
        {
            return default;
        }

        int hpDelta = participant.ToDbHpDelta(participant.CurrentHp - participant.StartingHp);
        await userRepository.ApplyCombatHpDeltaAsync(discordId, hpDelta);
        raidSessions.UnregisterParticipant(discordId);

        if (raidEnded)
        {
            raidSessions.Remove(session.RaidId);
        }

        return new RetireResult(participant, raidEnded, turnPassed);
    }

    // ============================================================
    // Turnos (v0.19.0). Todo el orden se decide bajo session.Lock, sin await adentro.
    // ============================================================

    // ¿Le toca a este jugador? (Llamar bajo el lock.) Una sesión sin invitaciones armada a mano (las pruebas) también tiene turnos: los de la lista Participants.
    private static bool IsTurnOf(RaidSession session, RaidParticipant participant) =>
        session.TurnIndex >= 0 && session.TurnIndex < session.Participants.Count && ReferenceEquals(session.Participants[session.TurnIndex], participant);

    // Pasa el turno al siguiente jugador EN PIE (se saltea a los caídos y a los que se fueron) y sube TurnNumber, que descarta el reloj del turno anterior. Si no queda nadie
    // en pie deja el índice como estaba. (Llamar bajo el lock.)
    private static void AdvanceTurn(RaidSession session)
    {
        int count = session.Participants.Count;
        for (int step = 1; step <= count; step++)
        {
            int candidate = (session.TurnIndex + step) % count;
            if (session.Participants[candidate].IsActive)
            {
                session.TurnIndex = candidate;
                break;
            }
        }

        session.TurnNumber++;
    }

    // Arma el reloj del turno que está en juego: si el jugador no actúa en RaidSettings.TurnTimeout, OnTurnTimeoutAsync lo saltea (y al segundo seguido lo retira). Sin
    // RaidSession.Turns (sesiones armadas a mano) no hace nada. El reloj del turno anterior se cancela; si por una carrera quedara uno vivo, ve otro TurnNumber y se descarta solo.
    private static void StartTurnTimer(RaidSession session)
    {
        if (session.Turns is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        int turnNumber;
        CancellationTokenSource? previous;

        lock (session.Lock)
        {
            if (session.Phase != RaidPhase.Active)
            {
                return;
            }

            turnNumber = session.TurnNumber;
            previous = session.TurnCts;
            session.TurnCts = cts;
        }

        previous?.Cancel();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(RaidSettings.TurnTimeout, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return; // el turno se jugó (o el combate terminó) antes de vencer
            }

            try
            {
                await OnTurnTimeoutAsync(session, turnNumber);
            }
            catch (Exception ex)
            {
                BotLog.Error(ex);
            }
        });
    }

    private static void StopTurnTimer(RaidSession session)
    {
        CancellationTokenSource? cts;
        lock (session.Lock)
        {
            cts = session.TurnCts;
            session.TurnCts = null;
        }

        cts?.Cancel();
    }

    // El aviso de turno (v0.19.0, a pedido del dueño): un mensaje corto con la MENCIÓN de a quién le toca, porque editar el embed no notifica a nadie. Cada aviso nuevo borra el anterior, así
    // el canal tiene uno solo a la vez. justPlayed = quien acaba de jugar: si es el único que queda en pie, no se le avisa a sí mismo. Sin RaidSession.Turns (sesiones armadas a mano) no
    // avisa. Nunca tira: un aviso que falla (canal sin permisos, token vencido) solo se registra, el combate sigue igual.
    private static async Task PingTurnAsync(RaidSession session, ulong? justPlayed = null)
    {
        if (session.Turns is null)
        {
            return;
        }

        ulong who;
        Func<Task>? previous;
        ICombatReplyTarget target;

        lock (session.Lock)
        {
            if (session.Phase != RaidPhase.Active || session.TurnIndex >= session.Participants.Count || !session.Participants[session.TurnIndex].IsActive)
            {
                return;
            }

            who = session.Participants[session.TurnIndex].DiscordId;
            previous = session.TurnPingDeleter;
            session.TurnPingDeleter = null;
            target = session.ReplyTarget;
        }

        try
        {
            if (previous is not null)
            {
                await previous();
            }
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
        }

        if (who == justPlayed)
        {
            return;
        }

        try
        {
            var deleter = await target.SendPingAsync($"🎯 <@{who}> ¡es tu turno!");
            lock (session.Lock)
            {
                session.TurnPingDeleter = deleter;
            }
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
        }
    }

    // Al terminar el combate (victoria, caída, abandono) se borra el último aviso de turno, que ya no sirve.
    private static async Task ClearTurnPingAsync(RaidSession session)
    {
        Func<Task>? previous;
        lock (session.Lock)
        {
            previous = session.TurnPingDeleter;
            session.TurnPingDeleter = null;
        }

        try
        {
            if (previous is not null)
            {
                await previous();
            }
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
        }
    }

    // Venció el turno de alguien. El primer vencimiento le hace perder el turno (pasa al siguiente y el jefe NO le pega); el segundo seguido lo retira del combate, como si huyera: sin
    // recompensa. Si el turno ya cambió (jugó justo a tiempo) no hace nada.
    private static async Task OnTurnTimeoutAsync(RaidSession session, int turnNumber)
    {
        var turns = session.Turns;
        if (turns is null)
        {
            return;
        }

        RaidParticipant slow;
        bool retire;

        lock (session.Lock)
        {
            if (session.Phase != RaidPhase.Active || session.TurnNumber != turnNumber || session.TurnIndex >= session.Participants.Count || session.EatingTurnNumber == turnNumber)
            {
                return; // otro turno, o está comiendo ahora mismo (cuando termine arma un reloj nuevo)
            }

            slow = session.Participants[session.TurnIndex];
            slow.MissedTurns++;
            retire = slow.MissedTurns >= 2;

            if (!retire)
            {
                AdvanceTurn(session);
            }
        }

        string line;
        if (retire)
        {
            var retired = await RetireAsync(session, slow.DiscordId, turns.Users, turns.Raids);
            if (retired.Participant is null)
            {
                return; // ya se había ido por su cuenta
            }

            if (retired.RaidEnded)
            {
                StopTurnTimer(session);
                await ClearTurnPingAsync(session);
                await SettleKnockedOutAsync(session, turns.Users, turns.Events);
                await session.ReplyTarget.UpdateAsync(BuildAbandonedEmbed(session), new ComponentBuilder().Build());
                return;
            }

            line = $"💤 **{slow.DisplayName}** no jugó dos turnos seguidos y se retiró del combate.";
        }
        else
        {
            line = $"⏰ **{slow.DisplayName}** tardó demasiado y pierde el turno. (Si pasa de nuevo, se retira del combate.)";
        }

        StartTurnTimer(session);
        await session.ReplyTarget.UpdateAsync(BuildCombatEmbed(session, line), BuildCombatButtons(session.RaidId));
        await PingTurnAsync(session, justPlayed: retire ? null : slow.DiscordId);
    }

    // ============================================================
    // Lógica estática (sin dependencia de Context) — reusada por Modules/TextCommandModule.Combat.cs
    // ("aa raid") y por el timeout del lobby (Task.Run en background, sin ninguna instancia de
    // módulo viva para entonces).
    // ============================================================

    public sealed record RaidRejection(string? PlainMessage, Embed? Embed);

    public static async Task<RaidRejection?> ValidateStartAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IMonsterRepository monsterRepository, IZoneRepository zoneRepository,
        ICombatSessionService combatSessions, IRaidSessionService raidSessions, ulong discordId, ICooldownRepository? cooldownRepository = null)
    {
        if (combatSessions.Peek(discordId) is not null || raidSessions.IsInAnyRaid(discordId))
        {
            return new RaidRejection(AdventureModule.BuildAlreadyInCombatMessage(), null);
        }

        // El cooldown (el mismo del jefe) se mira ANTES de armar nada: sin él no se arma el lobby ni se puede unir.
        if (await CooldownRejectionAsync(cooldownRepository, discordId) is { } onCooldown)
        {
            return onCooldown;
        }

        var player = await userRepository.GetOrCreateUserAsync(discordId);

        // Parado en El Fogón Eterno (zona 0) solo anda /boss contra el Asador: no hay raid.
        if (player.InGate)
        {
            return new RaidRejection(null, AdventureModule.BuildInGateEmbed());
        }

        if (player.CurrentHp <= 0)
        {
            return new RaidRejection(null, AdventureModule.BuildNoHpEmbed());
        }

        var boss = await monsterRepository.GetBossByZoneAsync(player.CurrentZoneId);
        if (boss is null)
        {
            return new RaidRejection(null, AdventureModule.BuildNoBossInZoneEmbed());
        }

        var orderedZones = ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync());
        int? requiredLevel = ZoneRanking.RequiredLevelForBoss(orderedZones, player.CurrentZoneId);

        if (requiredLevel is not null && player.Level < requiredLevel)
        {
            return new RaidRejection(null, AdventureModule.BuildNotLeveledForBossEmbed(requiredLevel.Value));
        }

        return null;
    }

    // Quien arranca el raid queda ANOTADO desde el primer momento (no hace falta que clickee
    // "Unirse" en su propio lobby — antes no lo estaba, pero sí figuraba como "ocupado" en el índice
    // del servicio, así que no podía sumarse y "Empezar ya" decía que no había nadie).
    public static async Task<RaidSession> BuildSessionAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IMonsterRepository monsterRepository,
        IZoneRepository zoneRepository, IBuffRepository buffRepository, ulong starterId, string starterDisplayName, IPlayerBonusService? bonusService = null)
    {
        var player = await userRepository.GetOrCreateUserAsync(starterId);
        var boss = (await monsterRepository.GetBossByZoneAsync(player.CurrentZoneId))!; // ValidateStartAsync ya confirmó que existe
        var zones = await zoneRepository.GetAllAsync();
        var zone = zones.FirstOrDefault(z => z.ZoneId == player.CurrentZoneId);
        // Posición de la zona por dificultad (no zone_id crudo): los multiplicadores del raid dependen de ella.
        int zoneRank = ZoneRanking.RankOf(ZoneRanking.OrderByDifficulty(zones), player.CurrentZoneId);

        int bossBaseHp = Random.Shared.Next(boss.MinHp, boss.MaxHp + 1);
        int bossBaseDamage = Random.Shared.Next(boss.MinDamage, boss.MaxDamage + 1);
        // El HP del jefe de raid depende de cuántos jugadores terminan entrando: acá se deja el valor
        // para 1 y se recalcula al arrancar (TryActivateAsync). Ver GameData/RaidDifficulty.cs.
        int bossMaxHp = RaidDifficulty.BossHp(bossBaseHp, 1, zoneRank);

        var session = new RaidSession
        {
            RaidId = Guid.NewGuid(),
            BossName = boss.Name,
            BossEmoji = boss.Emoji,
            BossPortrait = boss.Portrait,
            BossBaseHp = bossBaseHp,
            BossMaxHp = bossMaxHp,
            BossDamage = RaidDifficulty.BossDamage(bossBaseDamage, zoneRank),
            BossDropItemNames = boss.DropItemNames,
            BossGoldBonus = boss.GoldBonus,
            BossXpBonus = boss.XpBonus,
            ZoneId = player.CurrentZoneId,
            ZoneRank = zoneRank,
            ZoneName = zone?.Name ?? "?",
            StarterId = starterId,
            BossCurrentHp = bossMaxHp,
        };

        // La sesión todavía no la ve ningún otro hilo (recién se publica en IRaidSessionService.TryAdd),
        // así que agregar sin lock es seguro.
        session.Participants.Add(await BuildParticipantAsync(itemRepository, buffRepository, player, starterDisplayName, bonusService));

        return session;
    }

    // ============================================================
    // El grupo: "/boss @amigo" (v0.19.0). Quien arranca nombra a sus compañeros y SOLO ellos pueden aceptar. Antes de armar nada se mira que todos puedan pelear el jefe ahora, y que
    // tengan la MISMA zona máxima que quien lo arma: si no, se rechaza todo el pedido diciendo quién no puede y por qué.
    // ============================================================

    public sealed record PartyMember(ulong Id, string DisplayName);

    // Session != null: el grupo está listo para publicarse. Si no, Rejection dice por qué (siempre uno de los dos).
    public sealed record PartyPreparation(RaidSession? Session, RaidRejection? Rejection);

    public static async Task<PartyPreparation> PreparePartyAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IMonsterRepository monsterRepository, IZoneRepository zoneRepository,
        ICombatSessionService combatSessions, IRaidSessionService raidSessions, ICooldownRepository cooldownRepository, IBuffRepository buffRepository,
        IPlayerBonusService? bonusService, ulong starterId, string starterName, IReadOnlyList<PartyMember> companions)
    {
        static PartyPreparation Reject(string message) => new(null, new RaidRejection(message, null));

        // Los nombrados, sin repetidos (nombrar dos veces al mismo no es un error, se cuenta una).
        var invited = companions.GroupBy(c => c.Id).Select(group => group.First()).ToList();
        if (invited.Count == 0)
        {
            return Reject("Para pelear con amigos nombrá al menos a uno: **/boss companero:@amigo** (o \"aa boss @amigo\"). Solo, alcanza con **/boss**.");
        }

        if (invited.Any(c => c.Id == starterId))
        {
            return Reject("No podés invitarte a vos mismo 😅. Nombrá a tus compañeros.");
        }

        if (invited.Count + 1 > MaxParticipants)
        {
            return Reject($"El grupo puede ser de hasta {MaxParticipants} jugadores (vos y {MaxParticipants - 1} más).");
        }

        // En El Fogón Eterno (zona 0) el jefe es el Asador y se pelea solo.
        if ((await userRepository.GetByDiscordIdAsync(starterId))?.InGate == true)
        {
            return Reject($"En El Fogón Eterno el jefe se enfrenta solo: usá **/boss** sin nombrar a nadie, o salí del Fogón con **/zona**.");
        }

        if (await ValidateStartAsync(userRepository, itemRepository, monsterRepository, zoneRepository, combatSessions, raidSessions, starterId, cooldownRepository) is { } rejection)
        {
            return new PartyPreparation(null, rejection);
        }

        var starter = await userRepository.GetOrCreateUserAsync(starterId);
        var orderedZones = ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync());
        int starterRank = ZoneRanking.MaxUnlockedRank(orderedZones, starter.Level, starter.HighestZoneCleared);

        var problems = new List<string>();
        foreach (var companion in invited)
        {
            var problem = await FindCompanionProblemAsync(userRepository, zoneRepository, combatSessions, raidSessions, cooldownRepository, companion.Id, starter.CurrentZoneId, starterRank);
            if (problem is not null)
            {
                problems.Add($"• **{companion.DisplayName}**: {problem.Other}.");
            }
        }

        if (problems.Count > 0)
        {
            return Reject("No se puede armar el grupo para pelear el jefe:\n" + string.Join('\n', problems));
        }

        var session = await BuildSessionAsync(userRepository, itemRepository, monsterRepository, zoneRepository, buffRepository, starterId, starterName, bonusService);
        session.MaxZoneRank = starterRank;
        foreach (var companion in invited)
        {
            session.Invited.Add(new RaidInvite(companion.Id, companion.DisplayName));
        }

        return new PartyPreparation(session, null);
    }

    // Publica el grupo ya armado: lo suma al servicio, anota a quien lo armó (queda "en un combate" y no puede cazar mientras espera), deja el contexto del reloj de turnos y arma
    // el vencimiento de las invitaciones. false si justo se le adelantó otra acción (en ese caso no queda nada colgado). El llamador ya tiene que haber puesto session.ReplyTarget.
    public static bool TryOpenParty(RaidSession session, IRaidSessionService raidSessions, IAdventureRepository adventureRepository, RaidTurnContext turns, ulong channelId)
    {
        session.Turns = turns;
        session.ChannelId = channelId;

        if (!raidSessions.TryAdd(session))
        {
            return false;
        }

        if (!raidSessions.TryRegisterParticipant(session.StarterId, session.RaidId))
        {
            raidSessions.Remove(session.RaidId);
            return false;
        }

        ScheduleLobbyTimeout(session, raidSessions, adventureRepository);
        return true;
    }

    // Por qué un compañero no puede pelear ESTE jefe ahora: la misma frase dicha de afuera (para el aviso de quien arma el grupo) y dicha a él (al aceptar). Null = puede.
    private sealed record CompanionProblem(string Other, string Self);

    private static async Task<CompanionProblem?> FindCompanionProblemAsync(
        IUserRepository userRepository, IZoneRepository zoneRepository, ICombatSessionService combatSessions, IRaidSessionService raidSessions,
        ICooldownRepository cooldownRepository, ulong discordId, int bossZoneId, int expectedMaxRank)
    {
        // Nunca se le abre una cuenta a otro: quien no hizo /start no puede ser parte.
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new CompanionProblem("todavía no creó su personaje (que use **/start**)", "todavía no creaste tu personaje: usá **/start**");
        }

        var orderedZones = ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync());

        // La regla del grupo: la MISMA zona máxima desbloqueada (la que muestra el perfil como "máx. Zona N"). Si no, uno pasearía al otro por una zona que no le toca.
        if (expectedMaxRank > 0)
        {
            int rank = ZoneRanking.MaxUnlockedRank(orderedZones, player.Level, player.HighestZoneCleared);
            if (rank != expectedMaxRank)
            {
                string theirs = ZoneLabel(orderedZones, rank);
                string group = ZoneLabel(orderedZones, expectedMaxRank);
                return new CompanionProblem(
                    $"su zona máxima es la {theirs} y la de quien armó el grupo es la {group}: para ir juntos tienen que tener la misma zona máxima",
                    $"tu zona máxima es la {theirs} y la del grupo es la {group}: para ir juntos tienen que tener la misma zona máxima");
            }
        }

        if (combatSessions.Peek(discordId) is not null || raidSessions.IsInAnyRaid(discordId))
        {
            return new CompanionProblem("ya está en medio de otro combate", "ya estás en medio de otro combate");
        }

        var remaining = await cooldownRepository.GetRemainingAsync(discordId, CooldownCatalog.Boss.CommandName, CooldownCatalog.Boss.Duration);
        if (remaining is not null)
        {
            string left = TimeFormat.Remaining(remaining.Value);
            return new CompanionProblem($"todavía tiene el cooldown del jefe (le faltan {left})", $"todavía tenés el cooldown del jefe (te faltan {left})");
        }

        if (player.InGate)
        {
            return new CompanionProblem("está en El Fogón Eterno, donde el jefe se pelea solo", "estás en El Fogón Eterno, donde el jefe se pelea solo");
        }

        if (player.CurrentHp <= 0)
        {
            return new CompanionProblem("no tiene vida (que se cure con **/heal**)", "no tenés vida: curate con **/heal**");
        }

        int? requiredLevel = ZoneRanking.RequiredLevelForBoss(orderedZones, bossZoneId);
        if (requiredLevel is not null && player.Level < requiredLevel)
        {
            return new CompanionProblem($"no llegó al nivel {requiredLevel} que pide este jefe", $"no llegaste al nivel {requiredLevel} que pide este jefe");
        }

        return null;
    }

    // La versión dicha a quien acepta (segunda persona), o null si puede. La usa el botón Aceptar.
    private static async Task<string?> CompanionProblemAsync(
        IUserRepository userRepository, IZoneRepository zoneRepository, ICombatSessionService combatSessions, IRaidSessionService raidSessions,
        ICooldownRepository cooldownRepository, ulong discordId, int bossZoneId, int expectedMaxRank) =>
        (await FindCompanionProblemAsync(userRepository, zoneRepository, combatSessions, raidSessions, cooldownRepository, discordId, bossZoneId, expectedMaxRank))?.Self;

    // "Zona 3" para una posición por dificultad (las zonas se numeran 1..N en orden de dificultad, ver CLAUDE.md "Zones"); si no se encuentra, el número crudo.
    private static string ZoneLabel(IReadOnlyList<Zone> orderedZones, int rank) =>
        rank >= 1 && rank <= orderedZones.Count ? $"Zona {orderedZones[rank - 1].ZoneId}" : $"Zona {rank}";

    // Un invitado dijo que no (o no pudo). Si con eso ya contestaron todos, el grupo arranca con quienes aceptaron — o se cancela si no llegan al mínimo.
    private static async Task DeclineAsync(RaidSession session, ulong discordId, IRaidSessionService raidSessions, IAdventureRepository adventureRepository)
    {
        lock (session.Lock)
        {
            if (session.Phase != RaidPhase.Lobby || session.Participants.Any(p => p.DiscordId == discordId))
            {
                return;
            }

            session.Declined.Add(discordId);
        }

        await session.ReplyTarget.UpdateAsync(BuildLobbyEmbed(session), BuildLobbyButtons(session.RaidId));
        await StartIfEveryoneAnsweredAsync(session, raidSessions, adventureRepository);
    }

    private static async Task StartIfEveryoneAnsweredAsync(RaidSession session, IRaidSessionService raidSessions, IAdventureRepository adventureRepository)
    {
        bool everyoneAnswered;
        lock (session.Lock)
        {
            everyoneAnswered = session.Phase == RaidPhase.Lobby
                && session.Invited.Count > 0
                && session.Invited.All(i => session.Declined.Contains(i.DiscordId) || session.Participants.Any(p => p.DiscordId == i.DiscordId));
        }

        if (everyoneAnswered)
        {
            await TryActivateAsync(session, raidSessions, adventureRepository);
        }
    }

    // Si el jugador tiene ocupado el cooldown del raid (que es el del jefe), el rechazo con cuánto le falta; si no, null. Sin repositorio no chequea
    // (los llamadores viejos siguen funcionando: igual el cooldown se reclama de forma atómica al arrancar el raid, ver TryActivateAsync).
    private static async Task<RaidRejection?> CooldownRejectionAsync(ICooldownRepository? cooldownRepository, ulong discordId)
    {
        if (cooldownRepository is null)
        {
            return null;
        }

        var remaining = await cooldownRepository.GetRemainingAsync(discordId, CooldownCatalog.Raid.CommandName, CooldownCatalog.Raid.Duration);
        return remaining is null ? null : new RaidRejection(null, BuildRaidCooldownEmbed(remaining.Value));
    }

    public static Embed BuildRaidCooldownEmbed(TimeSpan remaining) =>
        new EmbedBuilder()
            .WithTitle($"⏳ {CooldownCatalog.Boss.Emoji} Jefe: todavía no podés")
            .WithDescription($"Te falta **{TimeFormat.Remaining(remaining)}** para volver a pelear al jefe, solo o en equipo (es el mismo cooldown de **/boss**).")
            .WithColor(Color.DarkGrey)
            .Build();

    private static async Task<RaidParticipant> BuildParticipantAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IBuffRepository buffRepository, ulong discordId, string displayName, IPlayerBonusService? bonusService) =>
        await BuildParticipantAsync(itemRepository, buffRepository, await userRepository.GetOrCreateUserAsync(discordId), displayName, bonusService);

    private static async Task<RaidParticipant> BuildParticipantAsync(
        IItemRepository itemRepository, IBuffRepository buffRepository, User player, string displayName, IPlayerBonusService? bonusService)
    {
        var weapon = player.WeaponId is int weaponId ? await itemRepository.GetByIdAsync(weaponId) : null;
        var amulet = player.AmuletId is int amuletId ? await itemRepository.GetByIdAsync(amuletId) : null;
        // El +% de ataque de un banquete activo vale en el raid desde el primer golpe. El ataque se fija al unirse: un banquete
        // comido después no cuenta para ese raid, y si el buff vence a mitad de raid igual dura hasta que termina.
        var buff = await buffRepository.GetActiveAttackAsync((ulong)player.DiscordId);
        // Lo permanente (mascotas v0.10.0, Fuego Nuevo y bendiciones v0.12.0) también queda fijo al unirse: ataque, defensa y vida entran en el perfil; el oro y la EXP, al cobrar la victoria (ResolveVictoryAsync).
        var bonuses = bonusService is null ? null : await bonusService.GetAsync((ulong)player.DiscordId, player.FuegoNuevo);
        var profile = PlayerCombatProfileCalculator.Resolve(player, weapon, amulet, buff?.AttackPercent ?? 0, bonuses);

        return new RaidParticipant
        {
            Bonuses = bonuses,
            DiscordId = (ulong)player.DiscordId,
            DisplayName = displayName,
            Damage = profile.Damage,
            AttackBuffPercent = buff?.AttackPercent ?? 0,
            Defense = profile.Defense,
            Level = player.Level,
            PlayerClass = player.Class,
            Passives = profile.Passives,
            MaxHp = profile.CombatMaxHp,
            StartingHp = profile.CombatCurrentHp,
            CurrentHp = profile.CombatCurrentHp,
        };
    }

    // Reclama el cooldown de /boss para cada participante (fuera de cualquier lock — es I/O) y
    // pasa el raid a Active si queda gente suficiente. Público para el timeout del lobby y el botón
    // "Empezar ya".
    public static async Task TryActivateAsync(RaidSession session, IRaidSessionService raidSessions, IAdventureRepository adventureRepository)
    {
        // Tomar la activación es atómico: quien encuentre la fase en Lobby la pasa a Activating y
        // sigue; cualquier otra llamada concurrente (ej. "Empezar ya" justo cuando vence el timer)
        // ve Activating y sale sin hacer nada. También congela la lista: los joins exigen Lobby.
        List<RaidParticipant> snapshot;
        lock (session.Lock)
        {
            if (session.Phase != RaidPhase.Lobby)
            {
                return;
            }

            session.Phase = RaidPhase.Activating;
            snapshot = session.Participants.ToList();
        }

        // Ya no hace falta el vencimiento de las invitaciones (arrancó antes: respondieron todos, o "Empezar ya").
        session.TimeoutCts?.Cancel();

        // Los cooldowns que ESTA activación alcanzó a reclamar (v0.18.4): si el raid no llega a pelearse (se cancela por falta de gente o la activación explota a mitad de camino) hay que soltarlos,
        // porque antes quedaban corriendo la espera corta de 30 minutos y quien armó el raid no podía hacer /boss ni /raid por un jefe que nunca peleó (lo reportó Adiofh en #sugerencias).
        var claimedIds = new List<ulong>();

        try
        {
            var confirmed = new List<RaidParticipant>();
            var dropped = new List<RaidParticipant>();

            foreach (var participant in snapshot)
            {
                bool claimed = await adventureRepository.TryClaimCooldownAsync(
                    participant.DiscordId, CooldownCatalog.Raid.CommandName, CooldownCatalog.Raid.Duration, CooldownCatalog.Raid.RetryAfterFailure);
                if (claimed)
                {
                    confirmed.Add(participant);
                    claimedIds.Add(participant.DiscordId);
                }
                else
                {
                    dropped.Add(participant);
                    raidSessions.UnregisterParticipant(participant.DiscordId);
                }
            }

            if (confirmed.Count < MinParticipantsToStart)
            {
                lock (session.Lock)
                {
                    session.Phase = RaidPhase.Resolved;
                }

                raidSessions.Remove(session.RaidId);

                // Se sueltan ANTES de editar el mensaje: si la edición fallara, igual nadie queda con el cooldown gastado.
                await ReleaseClaimsAsync(claimedIds, adventureRepository);
                await session.ReplyTarget.UpdateAsync(BuildCancelledEmbed(session, confirmed.Count), new ComponentBuilder().Build());
                return;
            }

            lock (session.Lock)
            {
                session.Participants.RemoveAll(dropped.Contains);

                // El orden de los turnos: primero quien armó el grupo y después los invitados en el orden en que se los nombró (no en el que aceptaron). Sin invitaciones (sesiones
                // armadas a mano) queda el orden de la lista.
                if (session.Invited.Count > 0)
                {
                    var order = new List<ulong> { session.StarterId };
                    order.AddRange(session.Invited.Select(i => i.DiscordId));
                    session.Participants.Sort((a, b) => OrderOf(order, a.DiscordId).CompareTo(OrderOf(order, b.DiscordId)));
                }

                session.TurnIndex = 0;
                session.TurnNumber = 1;

                // Recién acá se sabe cuántos jugadores pelean de verdad (los que no pudieron reclamar
                // el cooldown ya salieron): el HP del jefe crece con cada uno. Va antes de pasar a
                // Active, dentro del mismo lock, así ningún click ve un jefe a medio escalar.
                session.BossMaxHp = RaidDifficulty.BossHp(session.BossBaseHp, session.Participants.Count, session.ZoneRank);
                session.BossCurrentHp = session.BossMaxHp;
                session.Phase = RaidPhase.Active;
            }

            string startLine = dropped.Count > 0
                ? $"¡El jefe entra en combate! ({dropped.Count} jugador(es) no pudo(pudieron) sumarse por tener el cooldown de Jefe ocupado.)"
                : "¡El jefe entra en combate!";

            await session.ReplyTarget.UpdateAsync(BuildCombatEmbed(session, startLine), BuildCombatButtons(session.RaidId));
            StartTurnTimer(session);

            // Todos los que pelean ya tienen el cooldown del jefe corriendo (la espera corta de una derrota; la victoria la alarga después).
            await SyncRemindersAsync(session, session.Participants.Select(p => p.DiscordId).ToList());

            // El primer turno también se avisa (quien armó el grupo puede estar mirando otra cosa mientras contestan los demás).
            await PingTurnAsync(session);
        }
        catch
        {
            // Si la activación explota a la mitad (la base no responde, el mensaje ya no se puede
            // editar) la sesión quedaría en Activating para siempre con todos los anotados
            // registrados como "en un raid": sin poder cazar ni viajar hasta reiniciar el bot. Se
            // cierra el raid completo y se vuelve a lanzar la excepción para que el llamador avise.
            lock (session.Lock)
            {
                session.Phase = RaidPhase.Resolved;
            }

            raidSessions.Remove(session.RaidId);
            await ReleaseClaimsAsync(claimedIds, adventureRepository);

            try
            {
                await session.ReplyTarget.UpdateAsync(BuildFailedEmbed(session), new ComponentBuilder().Build());
            }
            catch
            {
                // Si tampoco se puede editar el mensaje no hay nada más que hacer; lo importante (soltar
                // a los jugadores) ya quedó hecho arriba.
            }

            throw;
        }
    }

    private static int OrderOf(List<ulong> order, ulong discordId)
    {
        int index = order.IndexOf(discordId);
        return index < 0 ? int.MaxValue : index;
    }

    // Suelta los cooldowns del jefe que una activación reclamó y no se usaron. Nunca tira: si soltar uno falla, queda en el log y se sigue con el resto (peor caso: la espera corta de siempre).
    private static async Task ReleaseClaimsAsync(IReadOnlyList<ulong> discordIds, IAdventureRepository adventureRepository)
    {
        foreach (ulong id in discordIds)
        {
            try
            {
                await adventureRepository.ReleaseCooldownAsync(id, CooldownCatalog.Raid.CommandName, CooldownCatalog.Raid.Duration);
            }
            catch (Exception ex)
            {
                BotLog.Warn(ex);
            }
        }
    }

    public static void ScheduleLobbyTimeout(RaidSession session, IRaidSessionService raidSessions, IAdventureRepository adventureRepository)
    {
        var cts = new CancellationTokenSource();
        session.TimeoutCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(LobbyDuration, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return; // El combate ya arrancó (respondieron todos, o "Empezar ya") o se resolvió por otra vía.
            }

            try
            {
                await TryActivateAsync(session, raidSessions, adventureRepository);
            }
            catch (Exception ex)
            {
                // Si ya no se puede editar el mensaje (token vencido, mensaje borrado) no hay nada
                // más que hacer — el raid igual queda marcado Resolved/sacado del índice arriba. Se registra:
                // acá también puede haber fallado la base al activar el raid.
                BotLog.Error(ex);
            }
        }, cts.Token);
    }

    private enum AttackOutcomeKind { NotActive, NotAParticipant, ParticipantInactive, NotYourTurn, Busy, AbilityUnavailable, Continues, Victory, Wipe }

    private sealed record AttackOutcome(AttackOutcomeKind Kind, string? LogLine = null);

    // Se llama SIEMPRE bajo session.Lock — pura matemática en memoria, nada de I/O acá adentro.
    private static AttackOutcome ResolveParticipantTurn(RaidSession session, RaidParticipant participant, bool useAbility)
    {
        // Habilidad pedida pero no disponible (enfriamiento, o clase sin habilidad): se rechaza sin
        // gastar el turno ni tocar nada.
        if (useAbility && CombatTurnResolver.CheckAbility(participant.PlayerClass, participant.Ability) != AbilityAvailability.Ready)
        {
            int wait = participant.Ability.CooldownRemaining;
            return new AttackOutcome(
                AttackOutcomeKind.AbilityUnavailable,
                wait > 0 ? $"Tu habilidad está en enfriamiento ({wait} turno(s))." : "Tu clase no tiene habilidad.");
        }

        // Golpe, Sifón de Almas y contraataque los resuelve el mismo código que el combate solitario
        // y el autohunt (GameData/CombatTurnResolver.cs) — acá solo se aplica el resultado al estado
        // compartido, bajo el lock del llamador.
        var turn = CombatTurnResolver.ResolveTurn(
            new CombatantProfile(participant.Damage, participant.Defense, participant.PlayerClass, participant.Passives),
            participant.Ability,
            useAbility ? PlayerAction.Ability : PlayerAction.Attack,
            participant.CurrentHp, participant.MaxHp, session.BossCurrentHp, session.BossDamage);

        session.BossCurrentHp = turn.MonsterHpAfter;
        participant.CurrentHp = turn.PlayerHpAfter;
        participant.Ability = turn.StatusAfter;
        participant.TurnsTaken++;
        participant.TotalDamageDealt += turn.DamageDealt;
        participant.CritCount += turn.CritCount;

        // Solo cuenta como "contribuyó" quien efectivamente pegó — desaparecer con Sombra no pega.
        if (turn.DamageDealt > 0)
        {
            participant.Contributed = true;
        }

        string who = $"**{participant.DisplayName}**";
        string actionText = turn.AbilityUsed is { } ability
            ? $"{ability.Emoji} {who} usó **{ability.Name}**. "
            : turn.Ambush ? $"🥷 {who} emboscó desde las sombras. " : string.Empty;
        string critText = turn.CritCount switch
        {
            <= 0 => string.Empty,
            1 => "💥 ¡GOLPE CRÍTICO! ",
            _ => $"💥 ¡{turn.CritCount} GOLPES CRÍTICOS! ",
        };
        string healText = turn.LifestealHeal > 0 ? $" 🔮 Sifón de Almas curó {turn.LifestealHeal} HP." : string.Empty;

        if (turn.MonsterDefeated)
        {
            // Resolved DENTRO del mismo lock que decide el resultado (no después, en
            // ResolveVictoryAsync): si no, entre soltar el lock y marcarlo, otro click
            // simultáneo vería la fase todavía Active y dispararía una segunda victoria
            // (recompensas duplicadas).
            session.Phase = RaidPhase.Resolved;
            return new AttackOutcome(AttackOutcomeKind.Victory, $"{actionText}{critText}{who} le dio el golpe final a **{session.BossName}**.{healText}");
        }

        var monsterHit = turn.MonsterHit!; // el jefe sigue vivo => contraatacó
        string monsterLine;

        if (monsterHit.Dodged)
        {
            participant.DodgeCount++;
            monsterLine = $"💨 {who} esquivó el contraataque.";
        }
        else
        {
            participant.TotalDamageTaken += monsterHit.Damage;
            monsterLine = $"El jefe le devolvió **{monsterHit.Damage}** a {who}.";
            if (participant.IsKnockedOut)
            {
                monsterLine += $" 💀 {who} quedó derribado.";
            }
        }

        string strikeText = turn.DamageDealt > 0
            ? $"{critText}{who} le hizo **{turn.DamageDealt}** de daño al jefe.{healText} "
            : string.Empty;
        string logLine = $"{actionText}{strikeText}{monsterLine}";

        bool allInactive = session.Participants.All(p => !p.IsActive);
        if (allInactive)
        {
            session.Phase = RaidPhase.Resolved; // mismo motivo que arriba: atómico con la decisión
            return new AttackOutcome(AttackOutcomeKind.Wipe, logLine);
        }

        return new AttackOutcome(AttackOutcomeKind.Continues, logLine);
    }

    private static async Task ResolveVictoryAsync(
        RaidSession session, string logLine, IUserRepository userRepository, IItemRepository itemRepository,
        IAdventureRepository adventureRepository, IRaidSessionService raidSessions, IGameEvents gameEvents, IZoneBoxService zoneBoxService)
    {
        // session.Phase ya quedó en Resolved dentro del lock de ResolveParticipantTurn.
        raidSessions.Remove(session.RaidId);

        // La caja de las repeticiones es la misma para todos los participantes: se lee una vez (Services/BossChest.cs).
        var repeat = await BossChest.RepeatAsync(zoneBoxService, session.ZoneId);

        var results =new List<(RaidParticipant Participant, CombatReward Reward, Item? Drop, LevelUpOutcome Outcome)>();

        // Solo cobra quien contribuyó (pegó al menos un golpe) y no se retiró — huir siempre
        // renuncia a la recompensa, igual que en combate solitario. Cada uno se lleva TODO, no se
        // reparte entre el grupo (ver Context del plan) — cada llamada persiste, en la MISMA
        // transacción, tanto su HP final como el oro/XP/drop y el highest_zone_cleared.
        foreach (var participant in session.Participants.Where(p => p.Contributed && !p.HasFled))
        {
            // "Primera vez" por participante: el cofre de la zona es 100% solo para quien nunca había derrotado a este jefe en esta vuelta (se mira ANTES de aplicar la victoria);
            // las repeticiones dan la caja más chica de zone_boxes con su chance.
            bool firstClear = BossChest.IsFirstClear(await userRepository.GetByDiscordIdAsync(participant.DiscordId), session.ZoneId);
            var reward = CombatRewardCalculator.RollBossReward(
                participant.Level, session.BossGoldBonus, session.BossXpBonus, firstClear, participant.Bonuses, firstClear ? 0 : repeat.ChancePercent);

            Item? drop = null;
            if (reward.DroppedSomething)
            {
                if (firstClear && session.BossDropItemNames.Count > 0)
                {
                    string dropName = session.BossDropItemNames[Random.Shared.Next(session.BossDropItemNames.Count)];
                    drop = await itemRepository.GetByNameAsync(dropName);
                }
                else if (!firstClear && repeat.BoxName is not null)
                {
                    drop = await itemRepository.GetByNameAsync(repeat.BoxName);
                }
            }

            int hpDelta = participant.ToDbHpDelta(participant.CurrentHp - participant.StartingHp);
            var outcome = await adventureRepository.ApplyBossVictoryAsync(
                participant.DiscordId, reward.Gold, reward.Xp, hpDelta, drop?.ItemId, droppedItemQuantity: 1, session.ZoneId);

            await gameEvents.RecordVictoryAsync(participant.DiscordId, GameEventKinds.RaidWin, outcome);

            // La historia (GameData/Lore.cs): si es la primera vez que ESTE jugador vence al jefe, se le abre su capítulo (el aviso le llega por el evento, a su próximo comando).
            await gameEvents.RecordStoryChapterAsync(participant.DiscordId, session.BossName, firstClear, outcome);

            results.Add((participant, reward, drop, outcome));
        }

        await session.ReplyTarget.UpdateAsync(BuildVictoryEmbed(session, logLine, results), new ComponentBuilder().Build());

        // La victoria dejó el cooldown del jefe en su espera larga: se vuelve a armar el aviso de cada participante (el que pegó el último golpe lo arma también su interacción).
        await SyncRemindersAsync(session, session.Participants.Select(p => p.DiscordId).ToList());
    }

    private static async Task ResolveWipeAsync(
        RaidSession session, string logLine, IUserRepository userRepository, IRaidSessionService raidSessions, IGameEvents gameEvents)
    {
        // session.Phase ya quedó en Resolved dentro del lock de ResolveParticipantTurn.
        raidSessions.Remove(session.RaidId);

        await SettleKnockedOutAsync(session, userRepository, gameEvents);

        await session.ReplyTarget.UpdateAsync(BuildWipeEmbed(session, logLine), new ComponentBuilder().Build());
        await SyncRemindersAsync(session, session.Participants.Select(p => p.DiscordId).ToList());
    }

    // Los caídos pagan la misma penalidad que en un combate solitario (EXP del nivel a 0, 5 % del oro de la billetera) y guardan su vida. Los que ya se habían retirado (HasFled) ya
    // persistieron su HP al huir — no tocarlos de nuevo o se les aplicaría el delta dos veces. Lo usa la caída de todo el grupo y también el combate que termina porque el último en pie
    // se retira o se queda dormido (antes, en ese caso, el caído no pagaba nada). gameEvents puede ser null (quien llama no lo tiene): solo se omite el evento.
    private static async Task SettleKnockedOutAsync(RaidSession session, IUserRepository userRepository, IGameEvents? gameEvents)
    {
        List<RaidParticipant> knockedOut;
        lock (session.Lock)
        {
            knockedOut = session.Participants.Where(p => p.IsKnockedOut && !p.HasFled).ToList();
        }

        foreach (var participant in knockedOut)
        {
            int hpDelta = participant.ToDbHpDelta(participant.CurrentHp - participant.StartingHp);
            await userRepository.ApplyCombatHpDeltaAsync(participant.DiscordId, hpDelta);
            await AdventureModule.ApplyDeathPenaltyAsync(userRepository, participant.DiscordId);
            if (gameEvents is not null)
            {
                await gameEvents.RecordAsync(participant.DiscordId, GameEventKinds.FightLost, session.ZoneId, detail: "raid");
            }
        }
    }

    // ============================================================
    // Embeds y botones — de solo presentación, públicos por el mismo motivo que el resto del bot:
    // Modules/TextCommandModule.Combat.cs los reusa tal cual.
    // ============================================================

    // La invitación: quién armó el grupo, a quién invitó y cómo va cada uno (✅ aceptó, ⏳ falta, ❌ no). Sin invitaciones (sesiones armadas a mano) muestra solo a los anotados.
    public static Embed BuildLobbyEmbed(RaidSession session)
    {
        List<RaidParticipant> participants;
        List<RaidInvite> invited;
        HashSet<ulong> declined;
        lock (session.Lock)
        {
            participants = session.Participants.ToList();
            invited = session.Invited.ToList();
            declined = session.Declined.ToHashSet();
        }

        var lines = participants
            .Select(p => $"✅ **{p.DisplayName}**{(p.DiscordId == session.StarterId ? " · armó el grupo" : string.Empty)}")
            .ToList();

        foreach (var invite in invited.Where(i => participants.All(p => p.DiscordId != i.DiscordId)))
        {
            lines.Add(declined.Contains(invite.DiscordId)
                ? $"❌ ~~{invite.DisplayName}~~ · no se suma"
                : $"⏳ <@{invite.DiscordId}> · falta que acepte");
        }

        string starterName = participants.FirstOrDefault(p => p.DiscordId == session.StarterId)?.DisplayName ?? "Alguien";

        return new EmbedBuilder()
            .WithTitle($"👥 Jefe en equipo: {session.BossName} {session.BossEmoji}")
            .WithColor(Color.Purple)
            .WithMonsterPortrait(session.BossPortrait) // la cara del jefe en todos los mensajes del grupo
            .WithDescription(
                $"**{starterName}** arma un grupo para enfrentar al jefe de **{session.ZoneName}**.\n\n" +
                "Pegan **por turnos**: pega uno, el jefe le responde a ese jugador y después le toca al siguiente. Cada uno que le pegue cobra su recompensa completa. " +
                "La vida del jefe crece con cada jugador.\n\n" +
                $"Los invitados aceptan con los botones. Arranca cuando contestan todos, a los {(int)LobbyDuration.TotalSeconds} s, " +
                $"o cuando {starterName} toque **Empezar ya** (mínimo {MinParticipantsToStart} jugadores).")
            .AddField($"👥 Grupo ({participants.Count}/{MaxParticipants})", string.Join('\n', lines), false)
            .Build();
    }

    public static MessageComponent BuildLobbyButtons(Guid raidId)
    {
        return new ComponentBuilder()
            .WithButton("Aceptar", $"raid_join:{raidId}", ButtonStyle.Success, new Emoji("✅"))
            .WithButton("Rechazar", $"raid_decline:{raidId}", ButtonStyle.Secondary, new Emoji("✖️"))
            .WithButton("Empezar ya", $"raid_start:{raidId}", ButtonStyle.Primary, new Emoji("▶️"))
            .WithButton("Cancelar", $"raid_cancel:{raidId}", ButtonStyle.Danger, new Emoji("🚫"))
            .Build();
    }

    // Lo que se manda JUNTO con la invitación (fuera del embed, que no avisa a nadie): la mención de los invitados.
    public static string BuildInviteMentions(RaidSession session)
    {
        lock (session.Lock)
        {
            return string.Join(' ', session.Invited.Select(i => $"<@{i.DiscordId}>")) + " ¡los invitaron a pelear un jefe en equipo!";
        }
    }

    public static Embed BuildCombatEmbed(RaidSession session, string logLine)
    {
        List<RaidParticipant> participants;
        int bossHp;
        ulong? turnOf = null;
        lock (session.Lock)
        {
            participants = session.Participants.ToList();
            bossHp = session.BossCurrentHp;

            if (session.Phase == RaidPhase.Active && session.TurnIndex >= 0 && session.TurnIndex < session.Participants.Count && session.Participants[session.TurnIndex].IsActive)
            {
                turnOf = session.Participants[session.TurnIndex].DiscordId;
            }
        }

        string roster = string.Join('\n', participants.Select(p =>
        {
            // ▶️ marca a quien le toca; los demás, su estado.
            string status = p.DiscordId == turnOf ? "▶️" : p.HasFled ? "🏃" : p.IsKnockedOut ? "💀" : "✅";
            // Estado de la habilidad de cada uno (el botón "Habilidad" es compartido y genérico).
            string abilityLine = p.IsActive
                ? "\n" + string.Join(" · ", new[] { AdventureModule.BuildAbilityStatusLine(p.PlayerClass, p.Ability), p.MealsEaten >= RaidSettings.MealsPerFight ? "🍖 comida usada" : "🍖 comida lista" }.Where(x => x.Length > 0))
                : string.Empty;
            return $"{status} **{p.DisplayName}** — {HpLine(p.CurrentHp, p.MaxHp)}{abilityLine}";
        }));

        string turnLine = turnOf is { } id ? $"\n\n🎯 Le toca a <@{id}>" : string.Empty;

        return new EmbedBuilder()
            .WithTitle($"⚔️ {session.BossName} {session.BossEmoji} · jefe en equipo")
            .WithColor(Color.Gold)
            .WithMonsterPortrait(session.BossPortrait)
            .WithDescription(logLine + turnLine)
            .AddField($"{session.BossEmoji} HP de {session.BossName}", HpLine(bossHp, session.BossMaxHp), false)
            .AddField("👥 Participantes", roster, false)
            .WithFooter($"Pegan por turnos · {(int)RaidSettings.TurnTimeout.TotalSeconds} s para jugar el tuyo · {RaidSettings.MealsPerFight} comida por jugador (gasta el turno)")
            .Build();
    }

    public static MessageComponent BuildCombatButtons(Guid raidId)
    {
        return new ComponentBuilder()
            .WithButton("Atacar", $"raid_attack:{raidId}", ButtonStyle.Primary, new Emoji("⚔️"))
            .WithButton("Habilidad", $"raid_ability:{raidId}", ButtonStyle.Success, new Emoji("✨"))
            .WithButton("Comer", $"raid_eat:{raidId}", ButtonStyle.Secondary, new Emoji("🍖"))
            .WithButton("Huir", $"raid_flee:{raidId}", ButtonStyle.Danger, new Emoji("🏃"))
            .Build();
    }

    private static Embed BuildVictoryEmbed(
        RaidSession session, string logLine, IReadOnlyList<(RaidParticipant Participant, CombatReward Reward, Item? Drop, LevelUpOutcome Outcome)> results)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"🏆 ¡{session.BossName} {session.BossEmoji} derrotado!")
            .WithColor(Color.Green)
            .WithMonsterPortrait(session.BossPortrait)
            .WithDescription($"{logLine}\n\n{NpcDialogue.Boss(session.BossName, session.BossEmoji, NpcDialogue.BossLine.Defeated)}");

        if (results.Count == 0)
        {
            embed.AddField("Botín", "_Nadie llegó a pegarle un golpe — sin recompensa esta vez._", false);
        }

        foreach (var (participant, reward, drop, outcome) in results)
        {
            string dropLine = drop is not null ? $"\n🎁 {ItemDisplay.Format(drop.Emoji, drop.Name)}{(drop.Type == "Caja" ? " — abrilo con /open" : string.Empty)}" : string.Empty;
            string levelLine = outcome.LevelsGained > 0 ? $"\n🎉 ¡Subió a nivel {outcome.Player.Level}!" : string.Empty;
            // La primera vez que ESE jugador vence al jefe de la zona, además del cofre recibe el huevo de su mascota (en la misma transacción que el premio).
            string eggLine = outcome.EggGranted is { } egg ? $"\n🥚 {egg.EggName} — abrilo con /open" : string.Empty;

            embed.AddField(
                participant.DisplayName,
                $"💰 {reward.Gold} oro · 📊 {reward.Xp} XP{dropLine}{eggLine}{levelLine}",
                true);
        }

        return embed.Build();
    }

    private static Embed BuildWipeEmbed(RaidSession session, string logLine)
    {
        return new EmbedBuilder()
            .WithTitle($"💀 El grupo cayó ante {session.BossName} {session.BossEmoji}")
            .WithColor(Color.DarkRed)
            .WithMonsterPortrait(session.BossPortrait)
            .WithDescription($"{logLine}\n\n{NpcDialogue.Boss(session.BossName, session.BossEmoji, NpcDialogue.BossLine.Victory)}\n\nSin recompensa esta vez. Usá **/heal** para recuperarte.")
            .AddField("☠️ Penalidad", $"Quien cayó perdió la EXP de su nivel y el {DeathPenalty.GoldPercent} % del oro de su billetera. Lo que hay en el banco no se toca.", false)
            .Build();
    }

    private static Embed BuildAbandonedEmbed(RaidSession session)
    {
        return new EmbedBuilder()
            .WithTitle($"🏃 {session.BossName} {session.BossEmoji}: combate en equipo abandonado")
            .WithColor(Color.DarkGrey)
            .WithDescription("Todos los participantes se retiraron antes de derrotarlo.")
            .Build();
    }

    private static Embed BuildCancelledEmbed(RaidSession session, int confirmedCount)
    {
        return new EmbedBuilder()
            .WithTitle($"👥 {session.BossName} {session.BossEmoji}: grupo cancelado")
            .WithColor(Color.DarkGrey)
            .WithDescription($"No se juntaron los {MinParticipantsToStart} jugadores mínimos ({confirmedCount} confirmado(s)).\n\nNo se gastó el cooldown de Jefe de nadie.")
            .Build();
    }

    private static Embed BuildCancelledByStarterEmbed(RaidSession session)
    {
        return new EmbedBuilder()
            .WithTitle($"👥 {session.BossName} {session.BossEmoji}: grupo cancelado")
            .WithColor(Color.DarkGrey)
            .WithDescription("Quien lo armó lo canceló antes de empezar.\n\nNo se gastó el cooldown de Jefe de nadie.")
            .Build();
    }

    private static Embed BuildFailedEmbed(RaidSession session)
    {
        return new EmbedBuilder()
            .WithTitle($"👥 {session.BossName} {session.BossEmoji}: grupo cancelado")
            .WithColor(Color.DarkGrey)
            .WithDescription("Algo falló al arrancar el combate. Probá de nuevo con **/boss** (nombrando a tus compañeros) en un momento.")
            .Build();
    }

    private static string HpLine(int current, int max) => $"{ProgressBar.Render(current, max)}\n{current}/{max}";
}
