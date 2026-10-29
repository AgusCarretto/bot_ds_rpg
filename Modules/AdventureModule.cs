using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

public class AdventureModule(
    IAdventureRepository adventureRepository,
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IInventoryRepository inventoryRepository,
    ICombatSessionService combatSessions,
    IAdventureCombatStarter combatStarter,
    IGameEvents gameEvents,
    IBuffRepository buffRepository,
    IZoneBoxService zoneBoxService,
    IPartyBossService partyBoss) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("hunt", "Salí a cazar monstruos cercanos (cooldown de 1 minuto).")]
    public Task HandleHuntAsync() =>
        StartCombatAsync(CooldownCatalog.Hunt, () => combatStarter.PrepareHuntAsync(Context.User.Id));

    [SlashCommand("travel", "Enfrentá al monstruo élite de tu zona: más difícil, recompensa x30 (cooldown de 30 minutos).")]
    public Task HandleTravelAsync() =>
        StartCombatAsync(CooldownCatalog.Travel, () => combatStarter.PrepareTravelAsync(Context.User.Id));

    // El jefe de la zona, SOLO o EN EQUIPO (v0.19.0; reemplaza al viejo /raid): sin compañeros es la pelea de siempre; nombrando a uno o más (hasta 5) arma un grupo que pega por turnos
    // y exige la misma zona máxima (ver Modules/RaidModule.cs). Slash no tiene parámetros variables, de ahí los cinco opcionales; "aa boss @a @b" los toma todos juntos.
    [SlashCommand("boss", "Enfrentá al Jefe de tu zona, solo o con amigos (cooldown de 5 h, 30 min si perdés).")]
    public Task HandleBossAsync(
        [Summary("companero", "Un amigo para pelear juntos por turnos (misma zona máxima que vos).")] IUser? companion = null,
        [Summary("companero2", "Otro amigo más (opcional).")] IUser? companion2 = null,
        [Summary("companero3", "Otro amigo más (opcional).")] IUser? companion3 = null,
        [Summary("companero4", "Otro amigo más (opcional).")] IUser? companion4 = null,
        [Summary("companero5", "Otro amigo más (opcional).")] IUser? companion5 = null)
    {
        var named = new[] { companion, companion2, companion3, companion4, companion5 }.OfType<IUser>().ToList();
        return named.Count == 0
            ? StartCombatAsync(CooldownCatalog.Boss, () => combatStarter.PrepareBossAsync(Context.User.Id))
            : StartPartyAsync(named);
    }

    // Arma el grupo: valida a todos, publica la invitación (con la mención de los invitados fuera del embed, que es lo único que avisa) y deja que RaidModule siga con los botones.
    private async Task StartPartyAsync(IReadOnlyList<IUser> companions)
    {
        await DeferAsync();

        try
        {
            if (companions.Any(c => c.IsBot))
            {
                await FollowupAsync(Loc.T("Los bots no pelean jefes 🤖. Invitá a otros jugadores."), ephemeral: true);
                return;
            }

            var preparation = await partyBoss.PrepareAsync(
                Context.User.Id, GameModule.GetDisplayName(Context.User),
                companions.Select(c => new RaidModule.PartyMember(c.Id, GameModule.GetDisplayName(c))).ToList());

            if (preparation.Session is not { } session)
            {
                await FollowupAsync(preparation.Rejection!.PlainMessage, embed: preparation.Rejection.Embed, ephemeral: true);
                return;
            }

            session.ReplyTarget = new InteractionCombatReplyTarget(Context.Interaction);
            if (!partyBoss.TryOpen(session, Context.Channel?.Id ?? 0))
            {
                await FollowupAsync(Loc.T("Justo se te adelantó otra acción, probá de nuevo en un toque."), ephemeral: true);
                return;
            }

            await FollowupAsync(RaidModule.BuildInviteMentions(session), embed: RaidModule.BuildLobbyEmbed(session), components: RaidModule.BuildLobbyButtons(session.RaidId));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude armar el grupo ahora mismo, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    private async Task StartCombatAsync(CooldownDefinition definition, Func<Task<CombatStartOutcome>> prepare)
    {
        // El chequeo de cooldown + la preparación pueden superar los 3s que da Discord antes de
        // que la interacción expire.
        await DeferAsync();

        try
        {
            // Toda la lógica de negocio vive en IAdventureCombatStarter: la comparte "aa hunt"
            // (Modules/TextCommandModule.cs) sin duplicar nada. Cada comando resuelve su monstruo según
            // la zona actual del jugador: /hunt un pool (PrepareHuntAsync), /travel su monstruo
            // dedicado (PrepareTravelAsync), /boss el jefe (PrepareBossAsync).
            var outcome = await prepare();

            switch (outcome.Status)
            {
                case CombatStartStatus.AlreadyInCombat:
                    await FollowupAsync(BuildAlreadyInCombatMessage(), ephemeral: true);
                    return;
                case CombatStartStatus.OnCooldown:
                    await FollowupAsync(embed: BuildCooldownEmbed(definition, outcome.CooldownRemaining!.Value), ephemeral: true);
                    return;
                case CombatStartStatus.NoHp:
                    await FollowupAsync(embed: BuildNoHpEmbed(), ephemeral: true);
                    return;
                case CombatStartStatus.NoMonstersInZone:
                    await FollowupAsync(embed: BuildNoMonstersInZoneEmbed(), ephemeral: true);
                    return;
                case CombatStartStatus.NoBossInZone:
                    await FollowupAsync(embed: BuildNoBossInZoneEmbed(), ephemeral: true);
                    return;
                case CombatStartStatus.NotLeveledForBoss:
                    await FollowupAsync(embed: BuildNotLeveledForBossEmbed(outcome.RequiredLevel!.Value), ephemeral: true);
                    return;
                case CombatStartStatus.InGate:
                    await FollowupAsync(embed: BuildInGateEmbed(), ephemeral: true);
                    return;
                case CombatStartStatus.GateGearMissing:
                    await FollowupAsync(embed: BuildGateGearMissingEmbed(), ephemeral: true);
                    return;
                case CombatStartStatus.RaceLost:
                    await FollowupAsync(Loc.T("Justo se te adelantó otra ejecución de este comando, probá de nuevo en un toque."), ephemeral: true);
                    return;
            }

            var state = outcome.State!;

            if (!combatSessions.TryStart(Context.User.Id, state, new InteractionCombatReplyTarget(Context.Interaction)))
            {
                await FollowupAsync(BuildAlreadyInCombatMessage(), ephemeral: true);
                return;
            }

            await FollowupAsync(
                embed: BuildEncounterEmbed(state),
                components: BuildCombatButtons(state, await LoadHealOptionsAsync(inventoryRepository, buffRepository, Context.User.Id, state)));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync(Loc.T("¡Upa! Algo falló iniciando tu aventura, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // Desplegable "Curar" (solo /travel y /boss, una vez por pelea — ver GameData/CombatHeal.cs). Elegir una comida
    // pasa por EXACTAMENTE la misma lógica que /use en combate (UseModule.ExecuteUseAsync: gasta la comida, cura y le
    // cede el turno al monstruo), que además ya actualiza el mensaje del combate; acá solo se avisan los rechazos.
    [ComponentInteraction(CombatHeal.MenuCustomId)]
    public async Task HandleHealAsync(string[] selected)
    {
        await DeferAsync();

        try
        {
            var session = combatSessions.Peek(Context.User.Id);
            if (session is null)
            {
                // Sin esta guarda, UseModule trataría el click como "curarse fuera de combate" y gastaría la comida
                // con un mensaje viejo.
                await FollowupAsync(NoActiveFightMessage(combatSessions, Context.User.Id), ephemeral: true);
                return;
            }

            if (!CombatHeal.HasMenu(session.State.CommandName))
            {
                await FollowupAsync(Loc.T("Curarte con el desplegable solo se puede en /travel y /boss."), ephemeral: true);
                return;
            }

            if (selected.Length == 0 || selected[0] == "none")
            {
                await FollowupAsync(Loc.T("No elegiste ninguna comida."), ephemeral: true);
                return;
            }

            var result = await UseModule.ExecuteUseAsync(
                userRepository, itemRepository, inventoryRepository, combatSessions, buffRepository, Context.User.Id, selected[0]);

            if (result.PlainMessage is not null)
            {
                await FollowupAsync(result.PlainMessage, ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude usar esa comida, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    [ComponentInteraction("btn_attack")]
    public Task HandleAttackAsync() => ResolveTurnAsync(fled: false, useAbility: false);

    // Habilidad de clase (ver GameData/ClassAbilities.cs): un botón por combate, el mismo custom id
    // para todas las clases — cuál se ejecuta depende de state.PlayerClass, no del botón.
    [ComponentInteraction("btn_ability")]
    public Task HandleAbilityAsync() => ResolveTurnAsync(fled: false, useAbility: true);

    // «Huir» PREGUNTA antes de huir (v0.37.1, el dueño: «cada vez que se huya de una batalla necesito que se pregunte: ¿estás seguro?, cobarde o algo así»): en el celu, al editarse el mensaje de la pelea,
    // el botón de Huir cae donde estaba el de Atacar y, atacando rápido, se toca sin querer. La pregunta va en un mensaje PRIVADO aparte (el de la pelea no cambia, así los botones no se mueven) y el
    // «Sí, huir» lleva el id de ESTA pelea: si esa ya terminó y empezó otra, no huye de la nueva. Escribir «aa flee» NO pregunta: escribirlo ya es a propósito.
    [ComponentInteraction("btn_flee")]
    public async Task HandleFleeAsync()
    {
        try
        {
            var session = combatSessions.Peek(Context.User.Id);
            if (session is null)
            {
                await RespondAsync(NoActiveFightMessage(combatSessions, Context.User.Id), ephemeral: true);
                return;
            }

            await RespondAsync(Loc.T("🐔 ¿Estás seguro? Huir es de cobardes: si te vas no ganás nada de esta pelea."), components: BuildFleeConfirmButtons(session.State.FightId), ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
        }
    }

    [ComponentInteraction("btn_flee_yes:*")]
    public async Task HandleFleeYesAsync(string fightIdRaw)
    {
        await DeferAsync();

        try
        {
            var check = ResolveFleeConfirm(combatSessions, Context.User.Id, fightIdRaw);
            if (check != FleeConfirmStatus.Ok)
            {
                string why = check == FleeConfirmStatus.NoFight
                    ? NoActiveFightMessage(combatSessions, Context.User.Id)
                    : Loc.T("Esa pelea ya terminó; la que está en curso es otra y no huiste de nada.");
                await ModifyOriginalResponseAsync(p =>
                {
                    p.Content = why;
                    p.Components = new ComponentBuilder().Build();
                });
                return;
            }

            // La pelea se edita por el destino que ya tiene la sesión (SessionTargetCombatTurnOutput); este mensaje privado solo se cierra.
            var output = new SessionTargetCombatTurnOutput(Context.Interaction);
            await ResolveTurnCoreAsync(
                combatSessions, userRepository, itemRepository, adventureRepository, inventoryRepository, buffRepository, gameEvents, zoneBoxService,
                Context.User.Id, fled: true, useAbility: false, output);

            await ModifyOriginalResponseAsync(p =>
            {
                p.Content = output.Completed ? Loc.T("🏃 Huiste de la pelea.") : Loc.T("No se pudo huir: mirá el aviso.");
                p.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
        }
    }

    [ComponentInteraction("btn_flee_no:*")]
    public async Task HandleFleeNoAsync(string fightIdRaw)
    {
        await DeferAsync();
        await ModifyOriginalResponseAsync(p =>
        {
            p.Content = Loc.T("💪 ¡Seguís en la pelea!");
            p.Components = new ComponentBuilder().Build();
        });
    }

    public enum FleeConfirmStatus { Ok, NoFight, OtherFight }

    // ¿El «Sí, huir» sigue valiendo? Ok si el jugador tiene una pelea y es LA misma que se quiso dejar; NoFight si ya no tiene ninguna; OtherFight si la que tiene es otra (la anterior terminó).
    public static FleeConfirmStatus ResolveFleeConfirm(ICombatSessionService combatSessions, ulong discordId, string fightIdRaw)
    {
        var session = combatSessions.Peek(discordId);
        if (session is null)
        {
            return FleeConfirmStatus.NoFight;
        }

        return Guid.TryParse(fightIdRaw, out var fightId) && fightId == session.State.FightId ? FleeConfirmStatus.Ok : FleeConfirmStatus.OtherFight;
    }

    // Los dos botones de la pregunta: «No, a pelear» primero y «Sí, huir» (rojo) segundo. Los ids llevan el id de la pelea.
    public static MessageComponent BuildFleeConfirmButtons(Guid fightId) =>
        new ComponentBuilder()
            .WithButton(Loc.T("No, a pelear"), $"btn_flee_no:{fightId:N}", ButtonStyle.Success, new Emoji("💪"))
            .WithButton(Loc.T("Sí, huir"), $"btn_flee_yes:{fightId:N}", ButtonStyle.Danger, new Emoji("🏃"))
            .Build();

    private async Task ResolveTurnAsync(bool fled, bool useAbility)
    {
        // DeferAsync en una interacción de componente edita el mensaje original una vez resuelto,
        // en vez de crear uno nuevo. Los clicks de botón SIEMPRE llegan como interacción de
        // componente, sin importar si el combate arrancó por slash command o por "aa hunt".
        await DeferAsync();

        await ResolveTurnCoreAsync(
            combatSessions, userRepository, itemRepository, adventureRepository, inventoryRepository, buffRepository, gameEvents, zoneBoxService,
            Context.User.Id, fled, useAbility, new InteractionCombatTurnOutput(Context.Interaction));
    }

    // Lo que se le dice a quien aprieta un botón (o escribe «aa attack») sin tener un combate (v0.25.4). Dice POR QUÉ cuando el bot lo sabe: el combate venció por inactividad (más de un minuto sin jugar el
    // turno) o ya se resolvió (un mensaje viejo); y si no hay registro, las dos causas que quedan: el bot se reinició (cada actualización corta las peleas en curso, viven en memoria) o respondió otra copia del bot.
    // También lo deja en el registro, para poder ver con qué frecuencia pasa y por cuál causa.
    public static string NoActiveFightMessage(ICombatSessionService combatSessions, ulong discordId)
    {
        string? reason = combatSessions.RecentEnd(discordId);
        Console.WriteLine($"[COMBATE] {discordId} intentó pelear sin combate activo ({reason ?? "sin registro: reinicio del bot u otra copia del bot"}).");

        return reason switch
        {
            "timeout" => Loc.T("No tenés ningún combate activo: pasó más de **1 minuto** sin jugar el turno y se cerró como abandonado. Empezá otro con /hunt, /travel o /boss."),
            "resolved" => Loc.T("No tenés ningún combate activo: ese ya terminó (ganaste, perdiste o huiste) y este mensaje es viejo. Empezá otro con /hunt, /travel o /boss."),
            _ => Loc.T("No tenés ningún combate activo. Si estabas peleando, se cortó porque el bot se reinició (cada actualización corta las peleas en curso). Empezá otro con /hunt, /travel, /boss o \"aa hunt\"."),
        };
    }

    // El turno de un combate solitario (hunt, travel y boss), SIN dependencia de Context: lo usan el botón (Atacar / Habilidad / Huir, con la pantalla editada en el mismo mensaje) y los
    // comandos de texto "aa attack", "aa ability" y "aa flee" (mensaje nuevo con botones nuevos, ver Services/CombatTurnOutput.cs). Es UN solo lugar: no se vuelve a copiar la lógica.
    public static async Task ResolveTurnCoreAsync(
        ICombatSessionService combatSessions, IUserRepository userRepository, IItemRepository itemRepository, IAdventureRepository adventureRepository,
        IInventoryRepository inventoryRepository, IBuffRepository buffRepository, IGameEvents gameEvents, IZoneBoxService zoneBoxService,
        ulong discordId, bool fled, bool useAbility, ICombatTurnOutput output)
    {
        try
        {
            var session = combatSessions.Peek(discordId);
            if (session is null)
            {
                await output.NoteAsync(NoActiveFightMessage(combatSessions, discordId));
                return;
            }

            var state = session.State;

            if (fled)
            {
                if (!combatSessions.TryAdvance(discordId, session, null, null))
                {
                    await output.NoteAsync(Loc.T("Justo se resolvió tu combate por otra vía, revisá el mensaje."));
                    return;
                }

                // Delta (no snapshot): así una curación con /use a mitad de combate no se pierde
                // si el HP en base ya reflejaba otra cosa (ver IUserRepository.ApplyCombatHpDeltaAsync).
                await userRepository.ApplyCombatHpDeltaAsync(
                    discordId, state.ToDbHpDelta(state.PlayerCurrentHp - state.PlayerStartingHp));

                // No se acumula nada nuevo acá: huir no dispara golpe del monstruo, así que el
                // resumen es tal cual venía de los turnos anteriores.
                await output.ShowAsync(BuildFleeEmbed(state), new ComponentBuilder().Build(), null, session.ReplyTarget);
                return;
            }

            // Click sobre una habilidad que ya no está disponible (mensaje viejo, doble click, clase
            // sin habilidad): se rechaza sin gastar el turno.
            if (useAbility && CombatTurnResolver.CheckAbility(state.PlayerClass, state.Ability) != AbilityAvailability.Ready)
            {
                await output.NoteAsync(Loc.T("Tu habilidad todavía está en enfriamiento."));
                return;
            }

            // --- Turno del jugador: golpe, Sifón de Almas y contraataque, todo en un único lugar
            // compartido con el raid y el autohunt (ver GameData/CombatTurnResolver.cs). ---
            var turn = CombatTurnResolver.ResolveTurn(
                new CombatantProfile(state.PlayerDamage, state.PlayerDefense, state.PlayerClass, state.Passives),
                state.Ability,
                useAbility ? PlayerAction.Ability : PlayerAction.Attack,
                state.PlayerCurrentHp, state.PlayerMaxHp, state.MonsterCurrentHp, state.MonsterDamage);

            int playerHit = turn.DamageDealt;
            int monsterHpAfter = turn.MonsterHpAfter;
            int turnsElapsed = state.TurnsElapsed + 1;
            int critCount = state.CritCount + turn.CritCount;
            int totalDamageDealt = state.TotalDamageDealt + playerHit;
            int lifestealHeal = turn.LifestealHeal;
            int playerHpAfterLifesteal = turn.PlayerHpAfterStrike;
            int totalHealed = state.TotalHealed + lifestealHeal;

            if (turn.MonsterDefeated)
            {
                // Las tres recompensas salen de la MISMA fórmula (nivel + bonus del monstruo); /travel la
                // multiplica y cada tipo de pelea tiene su chance de drop (ver GameData/CombatRewardCalculator.cs),
                // así sube junto con la zona.
                // Primera vez (de esta vuelta) que cae el jefe de esta zona o repetición: cambia el cofre y el mensaje (Services/BossChest.cs). La primera vez cae el cofre de la zona
                // siempre; las repeticiones dan la caja de zone_boxes con su chance. Se mira ANTES de aplicar la victoria, que es lo que sube highest_zone_cleared.
                bool firstBossClear = state.CommandName == "boss" && state.BossZoneId is int clearedZone
                    && BossChest.IsFirstClear(await userRepository.GetByDiscordIdAsync(discordId), clearedZone);
                var repeat = state.CommandName == "boss" && !firstBossClear && state.BossZoneId is int repeatZone
                    ? await BossChest.RepeatAsync(zoneBoxService, repeatZone)
                    : (ChancePercent: 0, BoxName: (string?)null);
                // El viaje (v0.36.0) ya no suelta un material: puede caer la caja de la zona (zone_boxes, rol "travel") con su chance baja por los bonus de drop.
                var travelBox = state.CommandName == "travel" && state.TravelZoneId is int travelZone
                    ? await TravelBox.ChanceAsync(zoneBoxService, travelZone)
                    : (ChancePercent: 0, BoxName: (string?)null);
                var reward = state.CommandName switch
                {
                    "travel" => CombatRewardCalculator.RollTravelReward(state.PlayerLevel, state.MonsterGoldBonus, state.MonsterXpBonus, state.Bonuses, travelBox.ChancePercent),
                    "boss" => CombatRewardCalculator.RollBossReward(
                        state.PlayerLevel, state.MonsterGoldBonus, state.MonsterXpBonus, firstBossClear, state.Bonuses, repeat.ChancePercent),
                    _ => CombatRewardCalculator.RollHuntReward(state.PlayerLevel, state.MonsterGoldBonus, state.MonsterXpBonus, state.Bonuses),
                };

                Item? droppedItem = state.CommandName == "boss" && !firstBossClear
                    ? (reward.DroppedSomething && repeat.BoxName is not null ? await itemRepository.GetByNameAsync(repeat.BoxName) : null)
                    : state.CommandName == "travel"
                        ? (reward.DroppedSomething && travelBox.BoxName is not null ? await itemRepository.GetByNameAsync(travelBox.BoxName) : null)
                        : await ResolveDroppedItemAsync(itemRepository, state, reward);

                if (!combatSessions.TryAdvance(discordId, session, null, null))
                {
                    await output.NoteAsync(Loc.T("Justo se resolvió tu combate por otra vía, revisá el mensaje."));
                    return;
                }

                var finalState = state with
                {
                    PlayerCurrentHp = playerHpAfterLifesteal,
                    TurnsElapsed = turnsElapsed,
                    CritCount = critCount,
                    TotalDamageDealt = totalDamageDealt,
                    TotalHealed = totalHealed,
                };

                // Delta desde PlayerStartingHp (no un snapshot absoluto), por la misma razón que en
                // la huida: compone bien con cualquier curación aplicada en memoria durante la pelea.
                // Si es un jefe, ApplyBossVictoryAsync además sube highest_zone_cleared a
                // BossZoneId (capturado al arrancar el combate, no la zona actual "de nuevo").
                int hpDelta = finalState.ToDbHpDelta(finalState.PlayerCurrentHp - finalState.PlayerStartingHp);
                var outcome = finalState.CommandName == "boss"
                    ? await adventureRepository.ApplyBossVictoryAsync(
                        discordId, reward.Gold, reward.Xp, hpDelta, droppedItem?.ItemId, droppedItemQuantity: 1, finalState.BossZoneId!.Value)
                    : await adventureRepository.ApplyVictoryAsync(
                        discordId, reward.Gold, reward.Xp, hpDelta, droppedItem?.ItemId, droppedItemQuantity: 1);

                await gameEvents.RecordVictoryAsync(
                    discordId,
                    finalState.CommandName switch { "travel" => GameEventKinds.TravelWin, "boss" => GameEventKinds.BossWin, _ => GameEventKinds.HuntWin },
                    outcome);

                // La historia (GameData/Lore.cs): la primera vez que vence a este jefe se le abre su capítulo (el aviso sale solo, por el evento) y la pantalla cuenta la escena.
                var openedChapter = await gameEvents.RecordStoryChapterAsync(
                    discordId, finalState.CommandName == "boss" ? finalState.MonsterName : null, firstBossClear, outcome);

                await output.ShowAsync(
                    BuildVictoryEmbedWithStory(finalState, turn, reward, droppedItem, outcome, firstBossClear, openedChapter), new ComponentBuilder().Build(), null, session.ReplyTarget);
                return;
            }

            // --- Contraataque del monstruo (el jugador no lo derribó): ya resuelto arriba por el
            // resolver, con los efectos de habilidad activos (Aguante / Sombra) aplicados. ---
            var monsterHitOutcome = turn.MonsterHit!;
            int monsterHit = monsterHitOutcome.Damage;
            int playerHpAfter = turn.PlayerHpAfter;
            int dodgeCount = state.DodgeCount + (monsterHitOutcome.Dodged ? 1 : 0);
            int totalDamageTaken = state.TotalDamageTaken + monsterHit;

            if (playerHpAfter <= 0)
            {
                if (!combatSessions.TryAdvance(discordId, session, null, null))
                {
                    await output.NoteAsync(Loc.T("Justo se resolvió tu combate por otra vía, revisá el mensaje."));
                    return;
                }

                var finalState = state with
                {
                    PlayerCurrentHp = playerHpAfter,
                    TurnsElapsed = turnsElapsed,
                    DodgeCount = dodgeCount,
                    CritCount = critCount,
                    TotalDamageDealt = totalDamageDealt,
                    TotalDamageTaken = totalDamageTaken,
                    TotalHealed = totalHealed,
                };

                await userRepository.ApplyCombatHpDeltaAsync(
                    discordId, finalState.ToDbHpDelta(playerHpAfter - state.PlayerStartingHp));

                var penalty = await ApplyDeathPenaltyAsync(userRepository, discordId);
                await gameEvents.RecordAsync(discordId, GameEventKinds.FightLost, detail: finalState.CommandName);

                await output.ShowAsync(BuildDefeatEmbed(finalState, turn, penalty), new ComponentBuilder().Build(), null, session.ReplyTarget);
                return;
            }

            // --- Ambos sobreviven: el combate sigue ---
            var nextState = state with
            {
                MonsterCurrentHp = monsterHpAfter,
                PlayerCurrentHp = playerHpAfter,
                TurnsElapsed = turnsElapsed,
                DodgeCount = dodgeCount,
                CritCount = critCount,
                TotalDamageDealt = totalDamageDealt,
                TotalDamageTaken = totalDamageTaken,
                TotalHealed = totalHealed,
                Ability = turn.StatusAfter,
            };

            var nextTarget = output.NextReplyTarget(session.ReplyTarget);
            if (!combatSessions.TryAdvance(discordId, session, nextState, nextTarget))
            {
                await output.NoteAsync(Loc.T("Justo se resolvió tu combate por otra vía, revisá el mensaje."));
                return;
            }

            // La comida se vuelve a leer en cada turno: lo que comprás o vendés a mitad de pelea se refleja.
            var healOptions = await LoadHealOptionsAsync(inventoryRepository, buffRepository, discordId, nextState);
            await output.ShowAsync(BuildOngoingEmbed(nextState, turn), BuildCombatButtons(nextState, healOptions), nextTarget, session.ReplyTarget);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await output.NoteAsync(Loc.T("¡Upa! Algo falló procesando el combate, intentá de nuevo en un momento."));
        }
    }

    // Público para que AutoHuntModule resuelva el drop exactamente igual. Hunt, travel y jefe por
    // igual: un ítem al azar de la lista de ESTE monstruo (nunca madera/piedra/otro tipo, ver
    // GameData/MonsterCatalog.cs) — lo que cambia entre comandos es la chance (RollXReward), no de dónde sale.
    public static async Task<Item?> ResolveDroppedItemAsync(IItemRepository itemRepository, CombatState state, CombatReward reward)
    {
        if (!reward.DroppedSomething || state.MonsterDropItemNames.Count == 0)
        {
            return null;
        }

        string dropName = state.MonsterDropItemNames[Random.Shared.Next(state.MonsterDropItemNames.Count)];
        return await itemRepository.GetByNameAsync(dropName);
    }

    // Todo lo que sigue es de solo presentación (sin dependencias de Context): público para que
    // Modules/TextCommandModule.cs pueda armar exactamente los mismos mensajes para "aa hunt".
    public static string BuildAlreadyInCombatMessage() =>
        Loc.T("Ya estás en medio de un combate. Terminalo (atacando o huyendo) antes de iniciar otro.");

    // Atacar / [habilidad de clase] / Huir. El botón de habilidad lleva el nombre de la habilidad de
    // la clase del jugador y, mientras está en enfriamiento, los turnos que faltan y queda
    // deshabilitado. Una clase sin habilidad (no debería pasar) simplemente no lo muestra.
    // En /travel y /boss suma abajo el desplegable "Curar" con la comida que tenés (healOptions, ver
    // LoadHealOptionsAsync): una curación por pelea, y una vez usada queda deshabilitado con el motivo.
    public static MessageComponent BuildCombatButtons(CombatState state, IReadOnlyList<HealOption>? healOptions = null)
    {
        var builder = new ComponentBuilder()
            .WithButton(Loc.T("Atacar"), "btn_attack", ButtonStyle.Primary, new Emoji("⚔️"));

        var ability = ClassAbilities.For(state.PlayerClass);
        if (ability is not null)
        {
            int cooldown = state.Ability.CooldownRemaining;
            string label = cooldown > 0 ? $"{ability.Name} ({cooldown})" : ability.Name;
            builder.WithButton(label, "btn_ability", ButtonStyle.Success, new Emoji(ability.Emoji), disabled: cooldown > 0);
        }

        builder.WithButton(Loc.T("Huir"), "btn_flee", ButtonStyle.Danger, new Emoji("🏃"));

        if (CombatHeal.HasMenu(state.CommandName))
        {
            builder.WithSelectMenu(BuildHealMenu(state, healOptions), row: 1);
        }

        return builder.Build();
    }

    // La comida del jugador para el desplegable, o null si esta pelea no lo tiene o ya se curó (no hace falta leer
    // el inventario). Público y estático para que "aa travel" / "aa boss" arme los mismos componentes.
    public static async Task<IReadOnlyList<HealOption>?> LoadHealOptionsAsync(
        IInventoryRepository inventoryRepository, IBuffRepository buffRepository, ulong discordId, CombatState state)
    {
        if (!CombatHeal.HasMenu(state.CommandName) || state.HealUsed)
        {
            return null;
        }

        var owned = await inventoryRepository.GetOwnedByTypeAsync(discordId, "Consumable");
        var buffs = await buffRepository.GetItemBuffsAsync();
        return CombatHeal.BuildOptions(owned.Select(o => new HealOption(
            o.Item.Name, o.Item.StatValue, o.Quantity, buffs.TryGetValue(o.Item.ItemId, out var buff) ? buff.AttackPercent : 0)));
    }

    // Un desplegable de Discord no puede quedar sin opciones, así que los estados "sin comida" / "ya te curaste"
    // llevan una opción de relleno y van deshabilitados: se ve el motivo en vez de que el control desaparezca.
    private static SelectMenuBuilder BuildHealMenu(CombatState state, IReadOnlyList<HealOption>? options)
    {
        var menu = new SelectMenuBuilder().WithCustomId(CombatHeal.MenuCustomId).WithMinValues(1).WithMaxValues(1);

        if (state.HealUsed)
        {
            return menu.WithPlaceholder(Loc.T("🍖 Ya te curaste en esta pelea")).WithDisabled(true).AddOption(Loc.T("Curación usada"), "none");
        }

        if (options is null || options.Count == 0)
        {
            return menu.WithPlaceholder(Loc.T("🍖 No tenés comida para curarte")).WithDisabled(true).AddOption(Loc.T("Sin comida"), "none");
        }

        menu.WithPlaceholder(Loc.T("🍖 Curarte con comida (1 vez por pelea)"));
        foreach (var option in options)
        {
            // El emoji del ítem va en la opción SOLO si la aplicación lo tiene de verdad (ItemMenuExtensions): con uno que Discord no conoce rechaza el mensaje ENTERO
            // y se caería el inicio de cada /travel y /boss, por eso sin la lista de emojis cargada la opción sale como siempre.
            menu.AddItemOption(option.Name, option.Name, option.Name, CombatHeal.Describe(option));
        }

        return menu;
    }

    // "🔥 Bola de Fuego: lista" / "⏳ en 2 turno(s)" + el efecto activo si lo hay ("🛡️ Aguante activo
    // (1 turno)"). Vacío si la clase no tiene habilidad.
    public static string BuildAbilityStatusLine(string playerClass, AbilityState status)
    {
        var ability = ClassAbilities.For(playerClass);
        if (ability is null)
        {
            return string.Empty;
        }

        string readiness = status.CooldownRemaining > 0 ? Loc.F($"⏳ en {status.CooldownRemaining} turno(s)") : Loc.T("lista");
        string effect = status.EffectTurnsRemaining > 0 && ability.EffectLabel is not null
            ? Loc.F($" · {Loc.Tr(ability.EffectLabel)} ({status.EffectTurnsRemaining} turno(s))")
            : string.Empty;

        return $"{ability.Emoji} {ability.Name}: {readiness}{effect}";
    }

    private static EmbedBuilder WithAbilityField(EmbedBuilder embed, CombatState state)
    {
        string line = BuildAbilityStatusLine(state.PlayerClass, state.Ability);
        return line.Length > 0 ? embed.AddField(Loc.T("✨ Habilidad"), line, false) : embed;
    }

    public static Embed BuildCooldownEmbed(CooldownDefinition definition, TimeSpan remaining)
    {
        return new EmbedBuilder()
            .WithTitle(Loc.F($"⏳ {definition.Emoji} {Loc.Tr(definition.DisplayName)}: todavía no podés"))
            .WithDescription(Loc.F($"Te falta **{TimeFormat.Remaining(remaining)}** para volver a intentarlo."))
            .WithColor(Color.DarkGrey)
            .Build();
    }

    public static Embed BuildNoHpEmbed()
    {
        return new EmbedBuilder()
            .WithTitle(Loc.T("💀 Estás sin fuerzas"))
            .WithDescription(Loc.T("Te quedaste sin HP. Usá **/heal** para recuperarte antes de volver a intentarlo."))
            .WithColor(Color.DarkRed)
            .Build();
    }

    public static Embed BuildNoMonstersInZoneEmbed()
    {
        return new EmbedBuilder()
            .WithTitle(Loc.T("🗺️ Zona sin monstruos"))
            .WithDescription(Loc.T("Todavía no hay monstruos cargados en tu zona actual. Probá **/zona** para viajar a otra, o avisale al staff."))
            .WithColor(Color.DarkGrey)
            .Build();
    }

    public static Embed BuildNoBossInZoneEmbed()
    {
        return new EmbedBuilder()
            .WithTitle(Loc.T("👑 Esta zona no tiene Jefe"))
            .WithDescription(Loc.T("Tu zona actual todavía no tiene un Jefe de Zona cargado — probá `/hunt` mientras tanto."))
            .WithColor(Color.DarkGrey)
            .Build();
    }

    // /hunt, /travel, /autohunt y /raid estando parado en El Fogón Eterno (zona 0, GameData/FogonRules.cs): ahí solo hay un enemigo.
    public static Embed BuildInGateEmbed() =>
        new EmbedBuilder()
            .WithTitle(Loc.T("🔥 Estás en El Fogón Eterno"))
            .WithDescription(Loc.F($"Acá no hay cacería, viajes ni jefe en equipo: solo te espera **{FogonRules.BossName}**. Enfrentalo con **/boss**, o salí del Fogón viajando a una zona con **/zona**."))
            .WithColor(Color.DarkOrange)
            .Build();

    // /boss en el Fogón sin el equipo puesto (lo vendió después de entrar).
    public static Embed BuildGateGearMissingEmbed() =>
        new EmbedBuilder()
            .WithTitle(Loc.T("🔥 Te falta el equipo del Fogón"))
            .WithDescription(
                Loc.F($"Para enfrentar a **{FogonRules.BossName}** tenés que llevar puestos el **{FogonRules.WeaponName}** y la **{FogonRules.AmuletName}**. ") +
                Loc.T("Forjalos en la herrería (**/forge**). No se gastó el cooldown."))
            .WithColor(Color.DarkOrange)
            .Build();

    public static Embed BuildNotLeveledForBossEmbed(int requiredLevel)
    {
        return new EmbedBuilder()
            .WithTitle(Loc.T("👑 Todavía no estás listo para este Jefe"))
            .WithDescription(Loc.F($"Necesitás ser **nivel {requiredLevel}** (el nivel de la próxima zona) para desafiarlo. Seguí subiendo con `/hunt`."))
            .WithColor(Color.DarkGrey)
            .Build();
    }

    public static Embed BuildEncounterEmbed(CombatState state)
    {
        string title = state.CommandName switch
        {
            "hunt" => Loc.T("🏹 ¡Encuentro en la caza!"),
            "boss" => Loc.T("👑 ¡Encuentro con el Jefe de Zona!"),
            _ => Loc.T("🗺️ ¡Encuentro en el viaje!"),
        };

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(Color.Orange)
            .WithMonsterPortrait(state.MonsterPortrait) // la cara del monstruo, miniatura de TODOS los mensajes de esta pelea (si no, desaparecería a mitad)
            .WithDescription(Loc.F($"¡Un **{state.MonsterName}** {state.MonsterEmoji} salvaje aparece!\n\n{MonsterSays(state, NpcDialogue.BossLine.Intro)}"))
            .AddField(Loc.T("❤️ Tu HP"), HpLine(state.PlayerCurrentHp, state.PlayerMaxHp), true)
            .AddField(Loc.F($"{state.MonsterEmoji} HP de {state.MonsterName}"), HpLine(state.MonsterCurrentHp, state.MonsterMaxHp), true);

        if (CombatHeal.HasMenu(state.CommandName))
        {
            embed.WithFooter(Loc.T("🍖 Podés curarte UNA vez en esta pelea con el desplegable de abajo."));
        }
        else if (CombatHeal.IsLimited(state.CommandName))
        {
            embed.WithFooter(Loc.T("🍖 Podés curarte UNA vez en esta pelea con /use."));
        }

        return WithAbilityField(embed, state).Build();
    }

    private static Embed BuildOngoingEmbed(CombatState state, TurnResult turn)
    {
        var monsterHit = turn.MonsterHit!; // el combate sigue => el monstruo contraatacó
        string monsterLine = monsterHit.Dodged
            ? Loc.F($"💨 ¡Esquivaste el ataque del **{state.MonsterName}**!")
            : Loc.F($"El **{state.MonsterName}** te hizo **{monsterHit.Damage}** de daño.");
        string playerLine = turn.DamageDealt > 0 ? Loc.F($"Le hiciste **{turn.DamageDealt}** de daño.") : Loc.T("No atacaste este turno.");

        var embed = new EmbedBuilder()
            .WithTitle(Loc.F($"⚔️ Combate contra {state.MonsterName} {state.MonsterEmoji}"))
            .WithColor(Color.Gold)
            .WithMonsterPortrait(state.MonsterPortrait)
            .WithDescription($"{turn.ActionFlavor}{CritPrefix(turn.CritCount)}{playerLine} {monsterLine}{LifestealSuffix(turn.LifestealHeal, turn.LeechHeal)}{LastStandSuffix(turn)}")
            .AddField(Loc.T("❤️ Tu HP"), HpLine(state.PlayerCurrentHp, state.PlayerMaxHp), true)
            .AddField(Loc.F($"{state.MonsterEmoji} HP de {state.MonsterName}"), HpLine(state.MonsterCurrentHp, state.MonsterMaxHp), true);

        return WithAbilityField(embed, state).Build();
    }

    // «Ganaste: 💰 N Oro, 📊 N XP.» con los íconos de la interfaz (GameData/UiEmojis.cs, con su respaldo en emoji común): la comparten la victoria a mano y la del /autohunt para que se lean igual.
    public static string EarnedLine(CombatReward reward) =>
        Loc.F($"Ganaste: {UiEmojis.Gold} **{reward.Gold}** Oro, {UiEmojis.Xp} **{reward.Xp}** XP.");

    // La vida del jugador tal como se ve en la pelea: la de la base (que es lo que devuelve la victoria, ya con la subida de nivel) con el factor de combate de ESA pelea. Antes la victoria mostraba la vida
    // real y la pelea la escalada, y el máximo «cambiaba» de una pantalla a la otra (v0.25.3, ver GameData/HpDisplay.cs).
    // Va en un campo de ANCHO COMPLETO (la victoria), así que lleva la barra de ÍCONOS como el perfil (8 piezas); sin las piezas cargadas sale la de texto de siempre.
    public static string ShownHpLine(User player, double hpMultiplier)
    {
        int current = HpDisplay.Scale(player.CurrentHp, hpMultiplier), max = HpDisplay.Scale(player.MaxHp, hpMultiplier);
        int segments = UiEmojis.Bar(UiEmojis.BarHp) is null ? 10 : GameModule.ProfileBarSegments;
        return $"{ProgressBar.Render(current, max, segments, UiEmojis.BarHp)}\n{current}/{max}";
    }

    // La victoria sin capítulo nuevo (la firma de siempre: los arneses de prueba la invocan por reflexión con estos 6 argumentos).
    private static Embed BuildVictoryEmbed(
        CombatState state, TurnResult turn, CombatReward reward, Item? droppedItem, LevelUpOutcome outcome, bool firstBossClear = true) =>
        BuildVictoryEmbedWithStory(state, turn, reward, droppedItem, outcome, firstBossClear, null);

    // openedChapter: el capítulo de la historia que esta victoria abrió (GameData/Lore.cs), o null; su escena va dentro del campo del jefe.
    private static Embed BuildVictoryEmbedWithStory(
        CombatState state, TurnResult turn, CombatReward reward, Item? droppedItem, LevelUpOutcome outcome, bool firstBossClear, LoreChapter? openedChapter)
    {
        var player = outcome.Player;

        var embed = new EmbedBuilder()
            .WithTitle(Loc.F($"🏆 ¡Victoria contra {state.MonsterName} {state.MonsterEmoji}!"))
            .WithColor(Color.Green)
            .WithMonsterPortrait(state.MonsterPortrait) // SIEMPRE la cara del monstruo, haya drop o no (el drop va en su campo, abajo)
            // Todos los monstruos dicen algo al caer. El jefe lo dice en su campo propio (más abajo, junto con lo de la próxima zona).
            // Lo ganado va en la descripción, en una línea con sus íconos, igual que en el /autohunt (v0.26.2); la vida sigue siendo su campo, con la barra.
            .WithDescription($"{turn.ActionFlavor}{CritPrefix(turn.CritCount)}" + Loc.F($"Le hiciste **{turn.DamageDealt}** de daño y lo derrotaste.") + LifestealSuffix(turn.LifestealHeal, turn.LeechHeal) +
                $"\n{EarnedLine(reward)}" +
                (state.CommandName == "boss" ? string.Empty : $"\n\n{MonsterSays(state, NpcDialogue.BossLine.Defeated)}"))
            .AddField(Loc.T("❤️ Tu HP"), ShownHpLine(player, state.Passives.MaxHpMultiplier), false);

        // La tablita del resumen (se apaga en /config): arranca en una fila nueva, justo debajo de la vida (que ocupa todo el ancho).
        if (state.ShowFightSummary)
        {
            AddCombatSummary(embed, state);
        }

        if (droppedItem is not null)
        {
            // El jefe suelta un cofre (ver GameData/CombatRewardCalculator.BossChestFirstClearPercent); lo demás, un material.
            bool isChest = droppedItem.Type == "Caja";
            string droppedText = ItemDisplay.Format(droppedItem.Emoji, droppedItem.Name);
            if (isChest && state.CommandName == "travel")
            {
                // v0.36.0: el viaje ya no suelta material; lo único que puede caer es la caja de la zona.
                embed.AddField(Loc.T("🎁 ¡Cayó una caja!"), Loc.F($"{droppedText} — abrila con `/open`"), false);
            }
            else
            {
                embed.AddField(isChest ? Loc.T("🎁 ¡Cofre del jefe!") : Loc.T("🎁 Material obtenido"), isChest ? Loc.F($"{droppedText} — abrilo con `/open`") : droppedText, false);
            }
        }

        // La subida de nivel NO va acá: sale como mensaje propio, apenas termina el combate (GameData/LevelUpCard.cs).

        // La primera vez que se vence al jefe de la zona llega, junto con el cofre, el HUEVO de la mascota de esa zona (en la misma transacción que el premio).
        if (outcome.EggGranted is { } egg)
        {
            embed.AddField(Loc.T("🥚 ¡Un huevo para vos!"), Loc.F($"{ItemEmojiRegistry.Label(egg.EggName, "🥚")} — abrilo con `/open` y nace tu mascota."), false);
        }

        if (state.CommandName == "boss")
        {
            // El jefe se despide; y solo la PRIMERA vez que cae se anuncia que se abre la próxima zona. Las siguientes es "volviste a ganar":
            // decir de nuevo "ya podés avanzar" sería mentira (esa zona ya estaba abierta).
            if (outcome.Gate == GateEvent.Cleared)
            {
                // El Asador Eterno (zona 0): ganarle devuelve a la última zona y habilita el Fuego Nuevo (GameData/FogonRules.cs).
                embed.AddField(Loc.T("🔥 ¡El Asador Eterno cayó!"), $"{MonsterSays(state, NpcDialogue.BossLine.Defeated)}{StoryScene(openedChapter)}\n\n{FogonRules.ClearedText}", false);
            }
            else
            {
                string zoneLine = outcome.Gate == GateEvent.Opened
                    ? FogonRules.OpenedText
                    : firstBossClear
                        ? Loc.T("¡Se abrió el camino! Ya podés avanzar a la próxima zona con `/zona`.")
                        : Loc.T("¡Volviste a ganarle! El camino a la próxima zona ya lo tenías abierto.");
                embed.AddField(Loc.T("👑 ¡Jefe de Zona derrotado!"), $"{MonsterSays(state, NpcDialogue.BossLine.Defeated)}{StoryScene(openedChapter)}\n\n{zoneLine}", false);
            }
        }

        return embed.Build();
    }

    // state.PlayerCurrentHp ya viene con el resultado final aplicado (ver ResolveTurnAsync) — no
    // hace falta un HP "real" aparte de la base: al mostrar en unidades de combate (posiblemente
    // escaladas por un Guerrero) evitamos mezclar una cifra real de la base con un Máximo escalado.
    private static Embed BuildDefeatEmbed(CombatState state, TurnResult turn, DeathPenaltyOutcome? penalty = null)
    {
        // Una sola frase por caso (no se arma pegando pedazos): el inglés ordena distinto que el español.
        string hitLine = turn.DamageDealt > 0
            ? Loc.F($"Le hiciste **{turn.DamageDealt}** de daño, pero el **{state.MonsterName}** te devolvió **{turn.MonsterHit!.Damage}** y te dejó fuera de combate. Usá **/heal** para recuperarte.")
            : Loc.F($"Pero el **{state.MonsterName}** te devolvió **{turn.MonsterHit!.Damage}** y te dejó fuera de combate. Usá **/heal** para recuperarte.");

        var embed = new EmbedBuilder()
            .WithTitle(Loc.F($"💀 Derrota contra {state.MonsterName} {state.MonsterEmoji}"))
            .WithColor(Color.DarkRed)
            .WithMonsterPortrait(state.MonsterPortrait)
            .WithDescription($"{turn.ActionFlavor}{CritPrefix(turn.CritCount)}{hitLine}{LifestealSuffix(turn.LifestealHeal, turn.LeechHeal)}\n\n{MonsterSays(state, NpcDialogue.BossLine.Victory)}")
            .AddField(Loc.T("❤️ Tu HP"), HpLine(state.PlayerCurrentHp, state.PlayerMaxHp), true)
            .AddField(Loc.F($"{state.MonsterEmoji} HP de {state.MonsterName}"), HpLine(turn.MonsterHpAfter, state.MonsterMaxHp), true);

        if (state.ShowFightSummary)
        {
            AddCombatSummary(embed, state);
        }

        return WithDeathPenalty(embed, penalty).Build();
    }

    // Penalidad por perder un combate (GameData/DeathPenalty.cs): la EXP del nivel vuelve a 0 y se pierde el 5 % del oro de la billetera. Es UN solo
    // UPDATE atómico en IUserRepository; los cuatro lugares donde se pierde (solitario, /use, /autohunt y el raid caído) pasan por acá. Nunca tira: si
    // la base falla se loguea y el jugador simplemente no pierde nada (peor sería dejarlo sin respuesta justo cuando el combate ya se resolvió).
    public static async Task<DeathPenaltyOutcome?> ApplyDeathPenaltyAsync(IUserRepository userRepository, ulong discordId)
    {
        try
        {
            return await userRepository.ApplyDeathPenaltyAsync(discordId);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            return null;
        }
    }

    // La línea que cuenta la penalidad, o null si no hubo nada que contar (sin oro y sin EXP no se agrega ni un campo vacío).
    public static string? DeathPenaltyLine(DeathPenaltyOutcome? penalty)
    {
        if (penalty is null || (penalty.GoldLost <= 0 && penalty.XpLost <= 0))
        {
            return null;
        }

        if (penalty.GoldLost <= 0)
        {
            return Loc.T("Tu EXP de este nivel volvió a **0**.");
        }

        string gold = Loc.F($"Perdiste **{GameHistory.Number(penalty.GoldLost)}** de oro (el {DeathPenalty.GoldPercent} % de tu billetera)");
        return penalty.XpLost > 0
            ? Loc.F($"{gold} y tu EXP de este nivel volvió a **0**. Lo del banco no se toca.")
            : Loc.F($"{gold}. Lo del banco no se toca.");
    }

    // Agrega el campo "☠️ Penalidad" si hubo algo que perder; sin penalidad deja el embed como está.
    public static EmbedBuilder WithDeathPenalty(EmbedBuilder embed, DeathPenaltyOutcome? penalty)
    {
        string? line = DeathPenaltyLine(penalty);
        return line is null ? embed : embed.AddField(Loc.T("☠️ Penalidad"), line, false);
    }

    // "💥 ¡GOLPE CRÍTICO! " antepuesto a la línea de daño del jugador (o "¡N GOLPES CRÍTICOS!" si la
    // Lluvia de Flechas acertó varios), o nada si no hubo crítico.
    private static string CritPrefix(int critCount) => critCount switch
    {
        <= 0 => string.Empty,
        1 => Loc.T("💥 ¡GOLPE CRÍTICO! "),
        _ => Loc.F($"💥 ¡{critCount} GOLPES CRÍTICOS! "),
    };

    // El «Aliento final» del dragón (tier 4+): el golpe te iba a matar y quedaste con 1 de vida.
    private static string LastStandSuffix(TurnResult turn) => turn.LastStand ? Loc.T("\n🐉 ¡Aliento final! Tu dragón te mantuvo en pie con 1 de vida.") : string.Empty;

    // "🔮 Sifón de Almas te curó X HP." si el Hechicero curó algo este turno y "🩸 Sangre te curó Y HP." si fue el encantamiento (healed = la cura total; leech = la parte de Sangre).
    private static string LifestealSuffix(int healed, int leech = 0)
    {
        int siphon = Math.Max(0, healed - leech);
        return (siphon > 0 ? Loc.F($"\n🔮 Sifón de Almas te curó **{siphon}** HP.") : string.Empty) + (leech > 0 ? Loc.F($"\n🩸 Sangre te curó **{leech}** HP.") : string.Empty);
    }

    private static Embed BuildFleeEmbed(CombatState state)
    {
        // La vida ocupa su propia fila (no en línea): así la tablita del resumen arranca en una fila nueva y las tres columnas quedan parejas.
        var embed = new EmbedBuilder()
            .WithTitle(Loc.F($"🏃 Huiste del combate contra {state.MonsterName} {state.MonsterEmoji}"))
            .WithColor(Color.DarkGrey)
            .WithMonsterPortrait(state.MonsterPortrait)
            .WithDescription(Loc.T("Escapaste cobardemente... sin oro, sin experiencia, pero de una sola pieza."))
            .AddField(Loc.T("❤️ Tu HP"), HpLine(state.PlayerCurrentHp, state.PlayerMaxHp), !state.ShowFightSummary);

        if (state.ShowFightSummary)
        {
            AddCombatSummary(embed, state);
        }

        return embed.Build();
    }

    // El resumen del combate como una TABLITA (v0.26.1, el dueño: «un montón de cosas… ponelo en tablita»): un campo en línea por dato (el nombre dice qué es y el valor es el número), de a tres por
    // fila, en vez de una sola línea con todo pegado. Siempre van turnos, daño hecho y daño recibido; críticos, esquives y curados solo si hubo (nunca un campo en cero). Público para que AutoHuntModule
    // y UseModule usen exactamente la misma tabla al cerrar un combate (victoria, derrota o huida); los contadores viajan en el propio CombatState. El que la llama decide si va (state.ShowFightSummary).
    public static EmbedBuilder AddCombatSummary(EmbedBuilder embed, CombatState state)
    {
        embed.AddField(Loc.T("⏱️ Turnos"), state.TurnsElapsed.ToString(), true)
            .AddField(Loc.T("🗡️ Daño hecho"), GameHistory.Number(state.TotalDamageDealt), true)
            .AddField(Loc.T("🩸 Daño recibido"), GameHistory.Number(state.TotalDamageTaken), true);

        if (state.CritCount > 0)
        {
            embed.AddField(Loc.T("💥 Críticos"), GameHistory.Number(state.CritCount), true);
        }

        if (state.DodgeCount > 0)
        {
            embed.AddField(Loc.T("💨 Esquives"), GameHistory.Number(state.DodgeCount), true);
        }

        if (state.TotalHealed > 0)
        {
            embed.AddField(Loc.T("🔮 Curados"), GameHistory.Number(state.TotalHealed), true);
        }

        return embed;
    }

    // La escena de la primera victoria sobre un jefe (GameData/Lore.cs, FirstClearLine), en cursiva y con su aire; vacía si esta victoria no abrió ningún capítulo. El aviso
    // "📖 Capítulo desbloqueado" no va acá: sale como mensaje propio al terminar el comando (Services/ProgressNotifier.cs).
    private static string StoryScene(LoreChapter? chapter) => chapter is null ? string.Empty : $"\n\n*{chapter.FirstClearLine}*";

    // Lo que dice el monstruo de ESTA pelea (cacería, viaje o jefe) al aparecer, al caer o cuando te vence: todos hablan. Público para que /use y /autohunt
    // usen exactamente las mismas frases. El Asador Eterno cambia lo que dice según cuántos Fuegos Nuevos hizo el jugador (el estado lleva sus bonificaciones).
    public static string MonsterSays(CombatState state, NpcDialogue.BossLine line) =>
        NpcDialogue.Monster(state.MonsterName, state.MonsterEmoji, line, isBoss: state.CommandName == "boss", fuegoNuevo: state.Bonuses?.FuegoNuevo ?? 0);

    private static string HpLine(int current, int max) => $"{ProgressBar.Render(current, max)}\n{current}/{max}";
}
