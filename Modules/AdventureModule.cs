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
    ICombatSessionService combatSessions,
    IAdventureCombatStarter combatStarter) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("hunt", "Salí a cazar monstruos cercanos (cooldown de 1 minuto).")]
    public Task HandleHuntAsync() =>
        StartCombatAsync(CooldownCatalog.Hunt, () => combatStarter.PrepareHuntAsync(Context.User.Id));

    [SlashCommand("travel", "Emprendé un viaje de exploración: más difícil, mejores recompensas (cooldown de 10 minutos).")]
    public Task HandleTravelAsync() =>
        StartCombatAsync(CooldownCatalog.Travel, () => combatStarter.PrepareAsync(Context.User.Id, CooldownCatalog.Travel, MonsterCatalog.TravelMonsters));

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
            // (Modules/TextCommandModule.cs) sin duplicar nada. /hunt resuelve su propio pool según
            // la zona actual del jugador (PrepareHuntAsync); /travel sigue con un pool fijo.
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

            await FollowupAsync(embed: BuildEncounterEmbed(state), components: BuildCombatButtons());
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! Algo falló iniciando tu aventura, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction("btn_attack")]
    public Task HandleAttackAsync() => ResolveTurnAsync(fled: false);

    [ComponentInteraction("btn_flee")]
    public Task HandleFleeAsync() => ResolveTurnAsync(fled: true);

    private async Task ResolveTurnAsync(bool fled)
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

            // --- Golpe del jugador ---
            var playerHitOutcome = CombatMath.ResolvePlayerHit(state.PlayerDamage, state.Passives.CritChanceBonus);
            int playerHit = playerHitOutcome.Damage;
            int monsterHpAfter = Math.Max(0, state.MonsterCurrentHp - playerHit);
            int turnsElapsed = state.TurnsElapsed + 1;
            int critCount = state.CritCount + (playerHitOutcome.Critical ? 1 : 0);
            int totalDamageDealt = state.TotalDamageDealt + playerHit;

            // Sifón de Almas (Hechicero): cura al atacar, killing blow incluido, antes de que el
            // monstruo (si sigue vivo) tenga la chance de contraatacar.
            int lifestealHeal = CombatMath.RollLifesteal(playerHit, state.Passives.LifestealChance, state.Passives.LifestealRatio);
            int playerHpAfterLifesteal = Math.Min(state.PlayerMaxHp, state.PlayerCurrentHp + lifestealHeal);
            int totalHealed = state.TotalHealed + lifestealHeal;

            if (monsterHpAfter <= 0)
            {
                var reward = state.CommandName is "hunt" or "boss"
                    ? CombatRewardCalculator.RollHuntReward(state.PlayerLevel, state.MonsterGoldBonus, state.MonsterXpBonus)
                    : CombatRewardCalculator.RollTravelReward(state.PlayerLevel);

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
                var outcome = finalState.CommandName == "boss"
                    ? await adventureRepository.ApplyBossVictoryAsync(
                        Context.User.Id, reward.Gold, reward.Xp, hpDelta, droppedItem?.ItemId, droppedItemQuantity: 1, finalState.BossZoneId!.Value)
                    : await adventureRepository.ApplyVictoryAsync(
                        Context.User.Id, reward.Gold, reward.Xp, hpDelta, droppedItem?.ItemId, droppedItemQuantity: 1);

                await ModifyOriginalResponseAsync(props =>
                {
                    props.Embed = BuildVictoryEmbed(finalState, playerHit, playerHitOutcome.Critical, lifestealHeal, reward, droppedItem, outcome);
                    props.Components = new ComponentBuilder().Build();
                });
                return;
            }

            // --- Golpe del monstruo (el jugador atacó pero no lo derribó) ---
            var monsterHitOutcome = CombatMath.ResolveMonsterHit(
                state.MonsterDamage, state.PlayerDefense, state.Passives.DodgeChanceBonus, state.Passives.DamageTakenMultiplier);
            int monsterHit = monsterHitOutcome.Damage;
            int playerHpAfter = Math.Max(0, playerHpAfterLifesteal - monsterHit);
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

                await ModifyOriginalResponseAsync(props =>
                {
                    props.Embed = BuildDefeatEmbed(finalState, playerHit, playerHitOutcome.Critical, lifestealHeal, monsterHit, monsterHpAfter);
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
            };

            if (!combatSessions.TryAdvance(Context.User.Id, session, nextState, new InteractionCombatReplyTarget(Context.Interaction)))
            {
                await FollowupAsync("Justo se resolvió tu combate por otra vía, revisá el mensaje.", ephemeral: true);
                return;
            }

            await ModifyOriginalResponseAsync(props =>
            {
                props.Embed = BuildOngoingEmbed(nextState, playerHit, playerHitOutcome.Critical, lifestealHeal, monsterHit, monsterHitOutcome.Dodged);
                props.Components = BuildCombatButtons();
            });
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! Algo falló procesando el combate, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Público para que AutoHuntModule resuelva el drop exactamente igual. /hunt: drop fijo del
    // monstruo (nunca madera/piedra/otro tipo, ver GameData/MonsterCatalog.cs). /travel: todavía
    // por rareza sorteada, pero acotado a type = 'Material' (nunca el catálogo entero) para que
    // tampoco pueda entregar un arma, amuleto o botín de recolección por error.
    public static async Task<Item?> ResolveDroppedItemAsync(IItemRepository itemRepository, CombatState state, CombatReward reward)
    {
        if (!reward.DroppedSomething)
        {
            return null;
        }

        if (state.CommandName is "hunt" or "boss")
        {
            if (state.MonsterDropItemNames.Count == 0)
            {
                return null;
            }

            string dropName = state.MonsterDropItemNames[Random.Shared.Next(state.MonsterDropItemNames.Count)];
            return await itemRepository.GetByNameAsync(dropName);
        }

        return await itemRepository.GetRandomByTypeAndRarityAsync("Material", RarityCatalog.RollTravelRarity());
    }

    // Todo lo que sigue es de solo presentación (sin dependencias de Context): público para que
    // Modules/TextCommandModule.cs pueda armar exactamente los mismos mensajes para "aa hunt".
    public static string BuildAlreadyInCombatMessage() =>
        "Ya estás en medio de un combate. Terminalo (atacando o huyendo) antes de iniciar otro.";

    public static MessageComponent BuildCombatButtons()
    {
        return new ComponentBuilder()
            .WithButton("Atacar", "btn_attack", ButtonStyle.Primary, new Emoji("⚔️"))
            .WithButton("Huir", "btn_flee", ButtonStyle.Danger, new Emoji("🏃"))
            .Build();
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

        return new EmbedBuilder()
            .WithTitle(title)
            .WithColor(Color.Orange)
            .WithDescription($"¡Un **{state.MonsterName}** {state.MonsterEmoji} salvaje aparece!")
            .AddField("❤️ Tu HP", HpLine(state.PlayerCurrentHp, state.PlayerMaxHp), true)
            .AddField($"{state.MonsterEmoji} HP de {state.MonsterName}", HpLine(state.MonsterCurrentHp, state.MonsterMaxHp), true)
            .Build();
    }

    private static Embed BuildOngoingEmbed(CombatState state, int playerHit, bool playerCritical, int lifestealHeal, int monsterHit, bool monsterDodged)
    {
        string monsterLine = monsterDodged
            ? $"💨 ¡Esquivaste el ataque del **{state.MonsterName}**!"
            : $"El **{state.MonsterName}** te hizo **{monsterHit}** de daño.";

        return new EmbedBuilder()
            .WithTitle($"⚔️ Combate contra {state.MonsterName} {state.MonsterEmoji}")
            .WithColor(Color.Gold)
            .WithDescription($"{CritPrefix(playerCritical)}Le hiciste **{playerHit}** de daño. {monsterLine}{LifestealSuffix(lifestealHeal)}")
            .AddField("❤️ Tu HP", HpLine(state.PlayerCurrentHp, state.PlayerMaxHp), true)
            .AddField($"{state.MonsterEmoji} HP de {state.MonsterName}", HpLine(state.MonsterCurrentHp, state.MonsterMaxHp), true)
            .Build();
    }

    private static Embed BuildVictoryEmbed(CombatState state, int playerHit, bool playerCritical, int lifestealHeal, CombatReward reward, Item? droppedItem, LevelUpOutcome outcome)
    {
        var player = outcome.Player;

        var embed = new EmbedBuilder()
            .WithTitle($"🏆 ¡Victoria contra {state.MonsterName} {state.MonsterEmoji}!")
            .WithColor(Color.Green)
            .WithDescription($"{CritPrefix(playerCritical)}Le hiciste **{playerHit}** de daño y lo derrotaste.{LifestealSuffix(lifestealHeal)}")
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
            embed.AddField("👑 ¡Jefe de Zona derrotado!", "Ya podés avanzar a la próxima zona con `/zona`.", false);
        }

        return embed.Build();
    }

    // state.PlayerCurrentHp ya viene con el resultado final aplicado (ver ResolveTurnAsync) — no
    // hace falta un HP "real" aparte de la base: al mostrar en unidades de combate (posiblemente
    // escaladas por un Guerrero) evitamos mezclar una cifra real de la base con un Máximo escalado.
    private static Embed BuildDefeatEmbed(CombatState state, int playerHit, bool playerCritical, int lifestealHeal, int monsterHit, int monsterHpAfter)
    {
        return new EmbedBuilder()
            .WithTitle($"💀 Derrota contra {state.MonsterName} {state.MonsterEmoji}")
            .WithColor(Color.DarkRed)
            .WithDescription(
                $"{CritPrefix(playerCritical)}Le hiciste **{playerHit}** de daño, pero el **{state.MonsterName}** te devolvió **{monsterHit}** " +
                $"y te dejó fuera de combate. Usá **/heal** para recuperarte.{LifestealSuffix(lifestealHeal)}")
            .AddField("❤️ Tu HP", HpLine(state.PlayerCurrentHp, state.PlayerMaxHp), true)
            .AddField($"{state.MonsterEmoji} HP de {state.MonsterName}", HpLine(monsterHpAfter, state.MonsterMaxHp), true)
            .AddField("📋 Resumen del combate", BuildCombatSummaryLine(state), false)
            .Build();
    }

    // "💥 ¡GOLPE CRÍTICO! " antepuesto a la línea de daño del jugador, o nada si no fue crítico.
    private static string CritPrefix(bool critical) => critical ? "💥 ¡GOLPE CRÍTICO! " : string.Empty;

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

    private static string HpLine(int current, int max) => $"{ProgressBar.Render(current, max)}\n{current}/{max}";
}
