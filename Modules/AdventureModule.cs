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
    IBuffRepository buffRepository) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("hunt", "Salí a cazar monstruos cercanos (cooldown de 1 minuto).")]
    public Task HandleHuntAsync() =>
        StartCombatAsync(CooldownCatalog.Hunt, () => combatStarter.PrepareHuntAsync(Context.User.Id));

    [SlashCommand("travel", "Enfrentá al monstruo élite de tu zona: más difícil, recompensa x10 (cooldown de 10 minutos).")]
    public Task HandleTravelAsync() =>
        StartCombatAsync(CooldownCatalog.Travel, () => combatStarter.PrepareTravelAsync(Context.User.Id));

    [SlashCommand("boss", "Enfrentá al Jefe de tu zona actual: derrotarlo te deja avanzar de zona (cooldown de 30 minutos).")]
    public Task HandleBossAsync() =>
        StartCombatAsync(CooldownCatalog.Boss, () => combatStarter.PrepareBossAsync(Context.User.Id));

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
                case CombatStartStatus.RaceLost:
                    await FollowupAsync("Justo se te adelantó otra ejecución de este comando, probá de nuevo en un toque.", ephemeral: true);
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
            await FollowupAsync("¡Upa! Algo falló iniciando tu aventura, intentá de nuevo en un momento.", ephemeral: true);
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
                await FollowupAsync("No tenés ningún combate activo.", ephemeral: true);
                return;
            }

            if (!CombatHeal.IsLimited(session.State.CommandName))
            {
                await FollowupAsync("Curarte con el desplegable solo se puede en /travel y /boss.", ephemeral: true);
                return;
            }

            if (selected.Length == 0 || selected[0] == "none")
            {
                await FollowupAsync("No elegiste ninguna comida.", ephemeral: true);
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
            await FollowupAsync("¡Upa! No pude usar esa comida, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction("btn_attack")]
    public Task HandleAttackAsync() => ResolveTurnAsync(fled: false, useAbility: false);

    // Habilidad de clase (ver GameData/ClassAbilities.cs): un botón por combate, el mismo custom id
    // para todas las clases — cuál se ejecuta depende de state.PlayerClass, no del botón.
    [ComponentInteraction("btn_ability")]
    public Task HandleAbilityAsync() => ResolveTurnAsync(fled: false, useAbility: true);

    [ComponentInteraction("btn_flee")]
    public Task HandleFleeAsync() => ResolveTurnAsync(fled: true, useAbility: false);

    private async Task ResolveTurnAsync(bool fled, bool useAbility)
    {
        // DeferAsync en una interacción de componente edita el mensaje original una vez resuelto,
        // en vez de crear uno nuevo. Los clicks de botón SIEMPRE llegan como interacción de
        // componente, sin importar si el combate arrancó por slash command o por "aa hunt".
        await DeferAsync();

        try
        {
            var session = combatSessions.Peek(Context.User.Id);
            if (session is null)
            {
                await FollowupAsync("No tenés ningún combate activo. Empezá uno con /hunt, /travel, o \"aa hunt\".", ephemeral: true);
                return;
            }

            var state = session.State;

            if (fled)
            {
                if (!combatSessions.TryAdvance(Context.User.Id, session, null, null))
                {
                    await FollowupAsync("Justo se resolvió tu combate por otra vía, revisá el mensaje.", ephemeral: true);
                    return;
                }

                // Delta (no snapshot): así una curación con /use a mitad de combate no se pierde
                // si el HP en base ya reflejaba otra cosa (ver IUserRepository.ApplyCombatHpDeltaAsync).
                await userRepository.ApplyCombatHpDeltaAsync(
                    Context.User.Id, state.ToDbHpDelta(state.PlayerCurrentHp - state.PlayerStartingHp));

                await ModifyOriginalResponseAsync(props =>
                {
                    // No se acumula nada nuevo acá: huir no dispara golpe del monstruo, así que el
                    // resumen es tal cual venía de los turnos anteriores.
                    props.Embed = BuildFleeEmbed(state);
                    props.Components = new ComponentBuilder().Build();
                });
                return;
            }

            // Click sobre una habilidad que ya no está disponible (mensaje viejo, doble click, clase
            // sin habilidad): se rechaza sin gastar el turno.
            if (useAbility && CombatTurnResolver.CheckAbility(state.PlayerClass, state.Ability) != AbilityAvailability.Ready)
            {
                await FollowupAsync("Tu habilidad todavía está en enfriamiento.", ephemeral: true);
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
                var reward = state.CommandName switch
                {
                    "travel" => CombatRewardCalculator.RollTravelReward(state.PlayerLevel, state.MonsterGoldBonus, state.MonsterXpBonus),
                    "boss" => CombatRewardCalculator.RollBossReward(state.PlayerLevel, state.MonsterGoldBonus, state.MonsterXpBonus),
                    _ => CombatRewardCalculator.RollHuntReward(state.PlayerLevel, state.MonsterGoldBonus, state.MonsterXpBonus),
                };

                Item? droppedItem = await ResolveDroppedItemAsync(itemRepository, state, reward);

                if (!combatSessions.TryAdvance(Context.User.Id, session, null, null))
                {
                    await FollowupAsync("Justo se resolvió tu combate por otra vía, revisá el mensaje.", ephemeral: true);
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
                // Primera vez que cae el jefe de esta zona (la que abre la siguiente) o repetición: el mensaje es distinto.
                // Se mira ANTES de aplicar la victoria, que es lo que sube highest_zone_cleared.
                bool firstBossClear = finalState.CommandName == "boss" && finalState.BossZoneId is int clearedZone
                    && ((await userRepository.GetByDiscordIdAsync(Context.User.Id))?.HighestZoneCleared ?? 0) < clearedZone;
                var outcome = finalState.CommandName == "boss"
                    ? await adventureRepository.ApplyBossVictoryAsync(
                        Context.User.Id, reward.Gold, reward.Xp, hpDelta, droppedItem?.ItemId, droppedItemQuantity: 1, finalState.BossZoneId!.Value)
                    : await adventureRepository.ApplyVictoryAsync(
                        Context.User.Id, reward.Gold, reward.Xp, hpDelta, droppedItem?.ItemId, droppedItemQuantity: 1);

                await gameEvents.RecordVictoryAsync(
                    Context.User.Id,
                    finalState.CommandName switch { "travel" => GameEventKinds.TravelWin, "boss" => GameEventKinds.BossWin, _ => GameEventKinds.HuntWin },
                    outcome);

                await ModifyOriginalResponseAsync(props =>
                {
                    props.Embed = BuildVictoryEmbed(finalState, turn, reward, droppedItem, outcome, firstBossClear);
                    props.Components = new ComponentBuilder().Build();
                });
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
                if (!combatSessions.TryAdvance(Context.User.Id, session, null, null))
                {
                    await FollowupAsync("Justo se resolvió tu combate por otra vía, revisá el mensaje.", ephemeral: true);
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
                    Context.User.Id, finalState.ToDbHpDelta(playerHpAfter - state.PlayerStartingHp));

                await gameEvents.RecordAsync(Context.User.Id, GameEventKinds.FightLost, detail: finalState.CommandName);

                await ModifyOriginalResponseAsync(props =>
                {
                    props.Embed = BuildDefeatEmbed(finalState, turn);
                    props.Components = new ComponentBuilder().Build();
                });
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

            if (!combatSessions.TryAdvance(Context.User.Id, session, nextState, new InteractionCombatReplyTarget(Context.Interaction)))
            {
                await FollowupAsync("Justo se resolvió tu combate por otra vía, revisá el mensaje.", ephemeral: true);
                return;
            }

            // La comida se vuelve a leer en cada turno: lo que comprás o vendés a mitad de pelea se refleja.
            var healOptions = await LoadHealOptionsAsync(inventoryRepository, buffRepository, Context.User.Id, nextState);
            await ModifyOriginalResponseAsync(props =>
            {
                props.Embed = BuildOngoingEmbed(nextState, turn);
                props.Components = BuildCombatButtons(nextState, healOptions);
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! Algo falló procesando el combate, intentá de nuevo en un momento.", ephemeral: true);
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
        "Ya estás en medio de un combate. Terminalo (atacando o huyendo) antes de iniciar otro.";

    // Atacar / [habilidad de clase] / Huir. El botón de habilidad lleva el nombre de la habilidad de
    // la clase del jugador y, mientras está en enfriamiento, los turnos que faltan y queda
    // deshabilitado. Una clase sin habilidad (no debería pasar) simplemente no lo muestra.
    // En /travel y /boss suma abajo el desplegable "Curar" con la comida que tenés (healOptions, ver
    // LoadHealOptionsAsync): una curación por pelea, y una vez usada queda deshabilitado con el motivo.
    public static MessageComponent BuildCombatButtons(CombatState state, IReadOnlyList<HealOption>? healOptions = null)
    {
        var builder = new ComponentBuilder()
            .WithButton("Atacar", "btn_attack", ButtonStyle.Primary, new Emoji("⚔️"));

        var ability = ClassAbilities.For(state.PlayerClass);
        if (ability is not null)
        {
            int cooldown = state.Ability.CooldownRemaining;
            string label = cooldown > 0 ? $"{ability.Name} ({cooldown})" : ability.Name;
            builder.WithButton(label, "btn_ability", ButtonStyle.Success, new Emoji(ability.Emoji), disabled: cooldown > 0);
        }

        builder.WithButton("Huir", "btn_flee", ButtonStyle.Danger, new Emoji("🏃"));

        if (CombatHeal.IsLimited(state.CommandName))
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
        if (!CombatHeal.IsLimited(state.CommandName) || state.HealUsed)
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
            return menu.WithPlaceholder("🍖 Ya te curaste en esta pelea").WithDisabled(true).AddOption("Curación usada", "none");
        }

        if (options is null || options.Count == 0)
        {
            return menu.WithPlaceholder("🍖 No tenés comida para curarte").WithDisabled(true).AddOption("Sin comida", "none");
        }

        menu.WithPlaceholder("🍖 Curarte con comida (1 vez por pelea)");
        foreach (var option in options)
        {
            // Sin emoji en las opciones a propósito: si uno custom no le es accesible al bot, Discord rechaza el mensaje
            // ENTERO y se caería el inicio de cada /travel y /boss. El nombre y los HP alcanzan.
            menu.AddOption(option.Name, option.Name, CombatHeal.Describe(option));
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

        string readiness = status.CooldownRemaining > 0 ? $"⏳ en {status.CooldownRemaining} turno(s)" : "lista";
        string effect = status.EffectTurnsRemaining > 0 && ability.EffectLabel is not null
            ? $" · {ability.EffectLabel} ({status.EffectTurnsRemaining} turno(s))"
            : string.Empty;

        return $"{ability.Emoji} {ability.Name}: {readiness}{effect}";
    }

    private static EmbedBuilder WithAbilityField(EmbedBuilder embed, CombatState state)
    {
        string line = BuildAbilityStatusLine(state.PlayerClass, state.Ability);
        return line.Length > 0 ? embed.AddField("✨ Habilidad", line, false) : embed;
    }

    public static Embed BuildCooldownEmbed(CooldownDefinition definition, TimeSpan remaining)
    {
        return new EmbedBuilder()
            .WithTitle($"⏳ {definition.Emoji} {definition.DisplayName}: todavía no podés")
            .WithDescription($"Te falta **{TimeFormat.Remaining(remaining)}** para volver a intentarlo.")
            .WithColor(Color.DarkGrey)
            .Build();
    }

    public static Embed BuildNoHpEmbed()
    {
        return new EmbedBuilder()
            .WithTitle("💀 Estás sin fuerzas")
            .WithDescription("Te quedaste sin HP. Usá **/heal** para recuperarte antes de volver a intentarlo.")
            .WithColor(Color.DarkRed)
            .Build();
    }

    public static Embed BuildNoMonstersInZoneEmbed()
    {
        return new EmbedBuilder()
            .WithTitle("🗺️ Zona sin monstruos")
            .WithDescription("Todavía no hay monstruos cargados en tu zona actual. Probá **/zona** para viajar a otra, o avisale al staff.")
            .WithColor(Color.DarkGrey)
            .Build();
    }

    public static Embed BuildNoBossInZoneEmbed()
    {
        return new EmbedBuilder()
            .WithTitle("👑 Esta zona no tiene Jefe")
            .WithDescription("Tu zona actual todavía no tiene un Jefe de Zona cargado — probá `/hunt` mientras tanto.")
            .WithColor(Color.DarkGrey)
            .Build();
    }

    public static Embed BuildNotLeveledForBossEmbed(int requiredLevel)
    {
        return new EmbedBuilder()
            .WithTitle("👑 Todavía no estás listo para este Jefe")
            .WithDescription($"Necesitás ser **nivel {requiredLevel}** (el nivel de la próxima zona) para desafiarlo. Seguí subiendo con `/hunt`.")
            .WithColor(Color.DarkGrey)
            .Build();
    }

    public static Embed BuildEncounterEmbed(CombatState state)
    {
        string title = state.CommandName switch
        {
            "hunt" => "🏹 ¡Encuentro en la caza!",
            "boss" => "👑 ¡Encuentro con el Jefe de Zona!",
            _ => "🗺️ ¡Encuentro en el viaje!",
        };

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(Color.Orange)
            .WithDescription(state.CommandName == "boss"
                ? $"¡Un **{state.MonsterName}** {state.MonsterEmoji} salvaje aparece!\n\n{NpcDialogue.Boss(state.MonsterName, state.MonsterEmoji, NpcDialogue.BossLine.Intro)}"
                : $"¡Un **{state.MonsterName}** {state.MonsterEmoji} salvaje aparece!")
            .AddField("❤️ Tu HP", HpLine(state.PlayerCurrentHp, state.PlayerMaxHp), true)
            .AddField($"{state.MonsterEmoji} HP de {state.MonsterName}", HpLine(state.MonsterCurrentHp, state.MonsterMaxHp), true);

        if (CombatHeal.IsLimited(state.CommandName))
        {
            embed.WithFooter("🍖 Podés curarte UNA vez en esta pelea con el desplegable de abajo.");
        }

        return WithAbilityField(embed, state).Build();
    }

    private static Embed BuildOngoingEmbed(CombatState state, TurnResult turn)
    {
        var monsterHit = turn.MonsterHit!; // el combate sigue => el monstruo contraatacó
        string monsterLine = monsterHit.Dodged
            ? $"💨 ¡Esquivaste el ataque del **{state.MonsterName}**!"
            : $"El **{state.MonsterName}** te hizo **{monsterHit.Damage}** de daño.";
        string playerLine = turn.DamageDealt > 0 ? $"Le hiciste **{turn.DamageDealt}** de daño." : "No atacaste este turno.";

        var embed = new EmbedBuilder()
            .WithTitle($"⚔️ Combate contra {state.MonsterName} {state.MonsterEmoji}")
            .WithColor(Color.Gold)
            .WithDescription($"{turn.ActionFlavor}{CritPrefix(turn.CritCount)}{playerLine} {monsterLine}{LifestealSuffix(turn.LifestealHeal)}")
            .AddField("❤️ Tu HP", HpLine(state.PlayerCurrentHp, state.PlayerMaxHp), true)
            .AddField($"{state.MonsterEmoji} HP de {state.MonsterName}", HpLine(state.MonsterCurrentHp, state.MonsterMaxHp), true);

        return WithAbilityField(embed, state).Build();
    }

    private static Embed BuildVictoryEmbed(
        CombatState state, TurnResult turn, CombatReward reward, Item? droppedItem, LevelUpOutcome outcome, bool firstBossClear = true)
    {
        var player = outcome.Player;

        var embed = new EmbedBuilder()
            .WithTitle($"🏆 ¡Victoria contra {state.MonsterName} {state.MonsterEmoji}!")
            .WithColor(Color.Green)
            .WithDescription($"{turn.ActionFlavor}{CritPrefix(turn.CritCount)}Le hiciste **{turn.DamageDealt}** de daño y lo derrotaste.{LifestealSuffix(turn.LifestealHeal)}")
            .AddField("💰 Oro ganado", reward.Gold.ToString(), true)
            .AddField("📊 EXP ganada", reward.Xp.ToString(), true)
            .AddField("❤️ Tu HP", HpLine(player.CurrentHp, player.MaxHp), true)
            .AddField("📋 Resumen del combate", BuildCombatSummaryLine(state), false);

        if (droppedItem is not null)
        {
            embed.AddField("🎁 Material obtenido", $"{ItemDisplay.Format(droppedItem.Emoji, droppedItem.Name)} ({droppedItem.Rarity})", false);
        }

        if (outcome.LevelsGained > 0)
        {
            embed.AddField("🎉 ¡Subiste de nivel!", $"Ahora sos nivel **{player.Level}** (vida máxima: {player.MaxHp}).", false);
        }

        if (state.CommandName == "boss")
        {
            // El jefe se despide; y solo la PRIMERA vez que cae se anuncia que se abre la próxima zona. Las siguientes es "volviste a ganar":
            // decir de nuevo "ya podés avanzar" sería mentira (esa zona ya estaba abierta).
            string zoneLine = firstBossClear
                ? "¡Se abrió el camino! Ya podés avanzar a la próxima zona con `/zona`."
                : "¡Volviste a ganarle! El camino a la próxima zona ya lo tenías abierto.";
            embed.AddField("👑 ¡Jefe de Zona derrotado!", $"{NpcDialogue.Boss(state.MonsterName, state.MonsterEmoji, NpcDialogue.BossLine.Defeated)}\n\n{zoneLine}", false);
        }

        return embed.Build();
    }

    // state.PlayerCurrentHp ya viene con el resultado final aplicado (ver ResolveTurnAsync) — no
    // hace falta un HP "real" aparte de la base: al mostrar en unidades de combate (posiblemente
    // escaladas por un Guerrero) evitamos mezclar una cifra real de la base con un Máximo escalado.
    private static Embed BuildDefeatEmbed(CombatState state, TurnResult turn)
    {
        string playerLine = turn.DamageDealt > 0 ? $"Le hiciste **{turn.DamageDealt}** de daño, pero el" : "Pero el";

        return new EmbedBuilder()
            .WithTitle($"💀 Derrota contra {state.MonsterName} {state.MonsterEmoji}")
            .WithColor(Color.DarkRed)
            .WithDescription(
                $"{turn.ActionFlavor}{CritPrefix(turn.CritCount)}{playerLine} **{state.MonsterName}** te devolvió **{turn.MonsterHit!.Damage}** " +
                $"y te dejó fuera de combate. Usá **/heal** para recuperarte.{LifestealSuffix(turn.LifestealHeal)}{BossTaunt(state)}")
            .AddField("❤️ Tu HP", HpLine(state.PlayerCurrentHp, state.PlayerMaxHp), true)
            .AddField($"{state.MonsterEmoji} HP de {state.MonsterName}", HpLine(turn.MonsterHpAfter, state.MonsterMaxHp), true)
            .AddField("📋 Resumen del combate", BuildCombatSummaryLine(state), false)
            .Build();
    }

    // "💥 ¡GOLPE CRÍTICO! " antepuesto a la línea de daño del jugador (o "¡N GOLPES CRÍTICOS!" si la
    // Lluvia de Flechas acertó varios), o nada si no hubo crítico.
    private static string CritPrefix(int critCount) => critCount switch
    {
        <= 0 => string.Empty,
        1 => "💥 ¡GOLPE CRÍTICO! ",
        _ => $"💥 ¡{critCount} GOLPES CRÍTICOS! ",
    };

    // "🔮 Sifón de Almas te curó X HP." agregado al log si el Hechicero curó algo este turno.
    private static string LifestealSuffix(int healed) => healed > 0 ? $"\n🔮 Sifón de Almas te curó **{healed}** HP." : string.Empty;

    private static Embed BuildFleeEmbed(CombatState state)
    {
        return new EmbedBuilder()
            .WithTitle($"🏃 Huiste del combate contra {state.MonsterName} {state.MonsterEmoji}")
            .WithColor(Color.DarkGrey)
            .WithDescription("Escapaste cobardemente... sin oro, sin experiencia, pero de una sola pieza.")
            .AddField("❤️ Tu HP", HpLine(state.PlayerCurrentHp, state.PlayerMaxHp), true)
            .AddField("📋 Resumen del combate", BuildCombatSummaryLine(state), false)
            .Build();
    }

    // Público para que AutoHuntModule y UseModule usen exactamente el mismo formato de resumen al
    // cerrar un combate (victoria, derrota o huida) — los contadores viajan en el propio CombatState.
    public static string BuildCombatSummaryLine(CombatState state)
    {
        string critText = state.CritCount > 0 ? $" · 💥 {state.CritCount} crítico(s)" : string.Empty;
        string dodgeText = state.DodgeCount > 0 ? $" · 💨 Esquivaste {state.DodgeCount} golpe(s)" : string.Empty;
        string healText = state.TotalHealed > 0 ? $" · 🔮 {state.TotalHealed} curados" : string.Empty;
        return $"⏱️ {state.TurnsElapsed} turno(s) · 🗡️ {state.TotalDamageDealt} de daño hecho · 🩸 {state.TotalDamageTaken} de daño recibido{critText}{dodgeText}{healText}";
    }

    // Lo que dice el jefe cuando te vence (nada si no era un jefe).
    private static string BossTaunt(CombatState state) =>
        state.CommandName == "boss" ? "\n\n" + NpcDialogue.Boss(state.MonsterName, state.MonsterEmoji, NpcDialogue.BossLine.Victory) : string.Empty;

    private static string HpLine(int current, int max) => $"{ProgressBar.Render(current, max)}\n{current}/{max}";
}
