using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// Jefes de Zona cooperativos: varios jugadores atacan al MISMO jefe compartido (HP en
// Services/IRaidSessionService.cs). A diferencia de rondas sincronizadas, cada click de "Atacar"
// resuelve el turno de QUIEN CLICKEÓ nomás — nunca espera a los demás, coherente con que el resto
// del bot es asincrónico. Reusa toda la matemática de combate solitario (GameData/CombatMath.cs) y
// la misma recompensa que /boss (GameData/CombatRewardCalculator.cs, IAdventureRepository.
// ApplyBossVictoryAsync): cada participante que contribuyó se lleva su propia recompensa COMPLETA
// al ganar, no se reparte entre el grupo. Ver Modules/AdventureModule.cs para el equivalente
// solitario del que esto deriva.
public class RaidModule(
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IMonsterRepository monsterRepository,
    IZoneRepository zoneRepository,
    IAdventureRepository adventureRepository,
    ICombatSessionService combatSessions,
    IRaidSessionService raidSessions) : InteractionModuleBase<SocketInteractionContext>
{
    private const int MinParticipantsToStart = 2;
    private const int MaxParticipants = 6;
    private static readonly TimeSpan LobbyDuration = TimeSpan.FromSeconds(60);

    [SlashCommand("raid", "Jefe de zona cooperativo: varios jugadores atacan al mismo jefe (mín. 2, cooldown de 30 min).")]
    public async Task HandleRaidAsync()
    {
        await DeferAsync();

        try
        {
            var rejection = await ValidateStartAsync(userRepository, itemRepository, monsterRepository, zoneRepository, combatSessions, raidSessions, Context.User.Id);
            if (rejection is not null)
            {
                await FollowupAsync(rejection.PlainMessage, embed: rejection.Embed, ephemeral: true);
                return;
            }

            var session = await BuildSessionAsync(userRepository, monsterRepository, zoneRepository, Context.User.Id, GameModule.GetDisplayName(Context.User));

            if (!raidSessions.TryAdd(session) || !raidSessions.TryRegisterParticipant(Context.User.Id, session.RaidId))
            {
                await FollowupAsync("Justo se te adelantó otra acción, probá de nuevo en un toque.", ephemeral: true);
                return;
            }

            session.ReplyTarget = new InteractionCombatReplyTarget(Context.Interaction);
            ScheduleLobbyTimeout(session, raidSessions, adventureRepository);

            await FollowupAsync(embed: BuildLobbyEmbed(session), components: BuildLobbyButtons(session.RaidId));
        }
        catch (Exception)
        {
            await FollowupAsync("¡Upa! No pude armar el raid ahora mismo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction("raid_join:*")]
    public async Task HandleJoinAsync(string raidIdRaw)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            if (!Guid.TryParse(raidIdRaw, out var raidId) || raidSessions.Peek(raidId) is not { } session)
            {
                await FollowupAsync("Ese raid ya no existe.", ephemeral: true);
                return;
            }

            ulong discordId = Context.User.Id;

            var rejection = await ValidateJoinAsync(userRepository, itemRepository, zoneRepository, combatSessions, raidSessions, session, discordId);
            if (rejection is not null)
            {
                await FollowupAsync(rejection.PlainMessage, embed: rejection.Embed, ephemeral: true);
                return;
            }

            var participant = await BuildParticipantAsync(userRepository, itemRepository, discordId, GameModule.GetDisplayName(Context.User));

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
                await FollowupAsync("No te pudiste sumar (el raid ya arrancó, está lleno, o ya estabas adentro).", ephemeral: true);
                return;
            }

            if (!raidSessions.TryRegisterParticipant(discordId, raidId))
            {
                lock (session.Lock)
                {
                    session.Participants.Remove(participant);
                }

                await FollowupAsync(AdventureModule.BuildAlreadyInCombatMessage(), ephemeral: true);
                return;
            }

            await session.ReplyTarget.UpdateAsync(BuildLobbyEmbed(session), BuildLobbyButtons(raidId));
            await FollowupAsync($"Te sumaste al raid contra **{session.BossName}** {session.BossEmoji}.", ephemeral: true);
        }
        catch (Exception)
        {
            await FollowupAsync("¡Upa! No te pude sumar al raid, intentá de nuevo en un momento.", ephemeral: true);
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
                await FollowupAsync("Ese raid ya no existe.", ephemeral: true);
                return;
            }

            if (Context.User.Id != session.StarterId)
            {
                await FollowupAsync("Solo quien arrancó el raid puede empezarlo antes de tiempo.", ephemeral: true);
                return;
            }

            int count;
            lock (session.Lock)
            {
                count = session.Participants.Count;
            }

            if (count < MinParticipantsToStart)
            {
                await FollowupAsync($"Hace falta un mínimo de {MinParticipantsToStart} jugadores para arrancar — todavía no se sumó nadie más.", ephemeral: true);
                return;
            }

            await TryActivateAsync(session, raidSessions, adventureRepository);
        }
        catch (Exception)
        {
            await FollowupAsync("¡Upa! Algo falló arrancando el raid, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction("raid_attack:*")]
    public async Task HandleAttackAsync(string raidIdRaw)
    {
        await DeferAsync();

        try
        {
            if (!Guid.TryParse(raidIdRaw, out var raidId) || raidSessions.Peek(raidId) is not { } session)
            {
                await FollowupAsync("Ese raid ya no existe.", ephemeral: true);
                return;
            }

            ulong discordId = Context.User.Id;
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
                    outcome = participant switch
                    {
                        null => new AttackOutcome(AttackOutcomeKind.NotAParticipant),
                        { IsActive: false } => new AttackOutcome(AttackOutcomeKind.ParticipantInactive),
                        _ => ResolveParticipantTurn(session, participant),
                    };
                }
            }

            switch (outcome.Kind)
            {
                case AttackOutcomeKind.NotActive:
                    await FollowupAsync("Este raid todavía no arrancó (o ya terminó).", ephemeral: true);
                    return;
                case AttackOutcomeKind.NotAParticipant:
                    await FollowupAsync("Todavía no te sumaste a este raid — clickeá \"Unirse\" antes de que arranque.", ephemeral: true);
                    return;
                case AttackOutcomeKind.ParticipantInactive:
                    await FollowupAsync("Ya no podés seguir peleando en este raid (te derribaron o te retiraste).", ephemeral: true);
                    return;
                case AttackOutcomeKind.Continues:
                    await session.ReplyTarget.UpdateAsync(BuildCombatEmbed(session, outcome.LogLine!), BuildCombatButtons(raidId));
                    return;
                case AttackOutcomeKind.Victory:
                    await ResolveVictoryAsync(session, outcome.LogLine!, userRepository, itemRepository, adventureRepository, raidSessions);
                    return;
                case AttackOutcomeKind.Wipe:
                    await ResolveWipeAsync(session, outcome.LogLine!, userRepository, raidSessions);
                    return;
            }
        }
        catch (Exception)
        {
            await FollowupAsync("¡Upa! Algo falló procesando tu ataque, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction("raid_flee:*")]
    public async Task HandleFleeAsync(string raidIdRaw)
    {
        await DeferAsync();

        try
        {
            if (!Guid.TryParse(raidIdRaw, out var raidId) || raidSessions.Peek(raidId) is not { } session)
            {
                await FollowupAsync("Ese raid ya no existe.", ephemeral: true);
                return;
            }

            ulong discordId = Context.User.Id;
            RaidParticipant? participant = null;
            bool raidEnded = false;

            lock (session.Lock)
            {
                var candidate = session.Participants.FirstOrDefault(p => p.DiscordId == discordId);
                if (candidate is not null && candidate.IsActive && session.Phase == RaidPhase.Active)
                {
                    candidate.HasFled = true;
                    participant = candidate;
                    raidEnded = session.Participants.All(p => !p.IsActive);
                    if (raidEnded)
                    {
                        session.Phase = RaidPhase.Resolved;
                    }
                }
            }

            if (participant is null)
            {
                await FollowupAsync("No podés retirarte de este raid ahora mismo.", ephemeral: true);
                return;
            }

            int hpDelta = participant.ToDbHpDelta(participant.CurrentHp - participant.StartingHp);
            await userRepository.ApplyCombatHpDeltaAsync(discordId, hpDelta);
            raidSessions.UnregisterParticipant(discordId);

            if (raidEnded)
            {
                raidSessions.Remove(session.RaidId);
                await session.ReplyTarget.UpdateAsync(BuildAbandonedEmbed(session), new ComponentBuilder().Build());
                return;
            }

            await session.ReplyTarget.UpdateAsync(BuildCombatEmbed(session, $"🏃 **{participant.DisplayName}** se retiró del raid."), BuildCombatButtons(raidId));
        }
        catch (Exception)
        {
            await FollowupAsync("¡Upa! No te pude retirar del raid, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // ============================================================
    // Lógica estática (sin dependencia de Context) — reusada por Modules/TextCommandModule.Combat.cs
    // ("aa raid") y por el timeout del lobby (Task.Run en background, sin ninguna instancia de
    // módulo viva para entonces).
    // ============================================================

    public sealed record RaidRejection(string? PlainMessage, Embed? Embed);

    public static async Task<RaidRejection?> ValidateStartAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IMonsterRepository monsterRepository, IZoneRepository zoneRepository,
        ICombatSessionService combatSessions, IRaidSessionService raidSessions, ulong discordId)
    {
        if (combatSessions.Peek(discordId) is not null || raidSessions.IsInAnyRaid(discordId))
        {
            return new RaidRejection(AdventureModule.BuildAlreadyInCombatMessage(), null);
        }

        var player = await userRepository.GetOrCreateUserAsync(discordId);

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

    public static async Task<RaidSession> BuildSessionAsync(
        IUserRepository userRepository, IMonsterRepository monsterRepository, IZoneRepository zoneRepository, ulong starterId, string starterDisplayName)
    {
        var player = await userRepository.GetOrCreateUserAsync(starterId);
        var boss = (await monsterRepository.GetBossByZoneAsync(player.CurrentZoneId))!; // ValidateStartAsync ya confirmó que existe
        var zone = await zoneRepository.GetByIdAsync(player.CurrentZoneId);

        int bossMaxHp = Random.Shared.Next(boss.MinHp, boss.MaxHp + 1);
        int bossDamage = Random.Shared.Next(boss.MinDamage, boss.MaxDamage + 1);

        var session = new RaidSession
        {
            RaidId = Guid.NewGuid(),
            BossName = boss.Name,
            BossEmoji = boss.Emoji,
            BossMaxHp = bossMaxHp,
            BossDamage = bossDamage,
            BossDropItemNames = boss.DropItemNames,
            BossGoldBonus = boss.GoldBonus,
            BossXpBonus = boss.XpBonus,
            ZoneId = player.CurrentZoneId,
            ZoneName = zone?.Name ?? "?",
            StarterId = starterId,
            BossCurrentHp = bossMaxHp,
        };

        return session;
    }

    public static async Task<RaidRejection?> ValidateJoinAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IZoneRepository zoneRepository,
        ICombatSessionService combatSessions, IRaidSessionService raidSessions, RaidSession session, ulong discordId)
    {
        if (combatSessions.Peek(discordId) is not null || raidSessions.IsInAnyRaid(discordId))
        {
            return new RaidRejection(AdventureModule.BuildAlreadyInCombatMessage(), null);
        }

        var player = await userRepository.GetOrCreateUserAsync(discordId);

        if (player.CurrentHp <= 0)
        {
            return new RaidRejection(null, AdventureModule.BuildNoHpEmbed());
        }

        var orderedZones = ZoneRanking.OrderByDifficulty(await zoneRepository.GetAllAsync());
        int? requiredLevel = ZoneRanking.RequiredLevelForBoss(orderedZones, session.ZoneId);

        if (requiredLevel is not null && player.Level < requiredLevel)
        {
            return new RaidRejection(null, AdventureModule.BuildNotLeveledForBossEmbed(requiredLevel.Value));
        }

        return null;
    }

    private static async Task<RaidParticipant> BuildParticipantAsync(
        IUserRepository userRepository, IItemRepository itemRepository, ulong discordId, string displayName)
    {
        var player = await userRepository.GetOrCreateUserAsync(discordId);
        var weapon = player.WeaponId is int weaponId ? await itemRepository.GetByIdAsync(weaponId) : null;
        var amulet = player.AmuletId is int amuletId ? await itemRepository.GetByIdAsync(amuletId) : null;
        var profile = PlayerCombatProfileCalculator.Resolve(player, weapon, amulet);

        return new RaidParticipant
        {
            DiscordId = discordId,
            DisplayName = displayName,
            Damage = profile.Damage,
            Defense = profile.Defense,
            Level = player.Level,
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

        var confirmed = new List<RaidParticipant>();
        var dropped = new List<RaidParticipant>();

        foreach (var participant in snapshot)
        {
            bool claimed = await adventureRepository.TryClaimCooldownAsync(participant.DiscordId, CooldownCatalog.Boss.CommandName, CooldownCatalog.Boss.Duration);
            if (claimed)
            {
                confirmed.Add(participant);
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
            await session.ReplyTarget.UpdateAsync(BuildCancelledEmbed(session, confirmed.Count), new ComponentBuilder().Build());
            return;
        }

        lock (session.Lock)
        {
            session.Participants.RemoveAll(dropped.Contains);
            session.Phase = RaidPhase.Active;
        }

        string startLine = dropped.Count > 0
            ? $"¡El jefe entra en combate! ({dropped.Count} jugador(es) no pudo(pudieron) sumarse por tener el cooldown de Jefe ocupado.)"
            : "¡El jefe entra en combate!";

        await session.ReplyTarget.UpdateAsync(BuildCombatEmbed(session, startLine), BuildCombatButtons(session.RaidId));
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
                return; // El raid ya arrancó a mano ("Empezar ya") o se resolvió por otra vía.
            }

            try
            {
                await TryActivateAsync(session, raidSessions, adventureRepository);
            }
            catch
            {
                // Si ya no se puede editar el mensaje (token vencido, mensaje borrado) no hay nada
                // más que hacer — el raid igual queda marcado Resolved/sacado del índice arriba.
            }
        }, cts.Token);
    }

    private enum AttackOutcomeKind { NotActive, NotAParticipant, ParticipantInactive, Continues, Victory, Wipe }

    private sealed record AttackOutcome(AttackOutcomeKind Kind, string? LogLine = null);

    // Se llama SIEMPRE bajo session.Lock — pura matemática en memoria, nada de I/O acá adentro.
    private static AttackOutcome ResolveParticipantTurn(RaidSession session, RaidParticipant participant)
    {
        var hit = CombatMath.ResolvePlayerHit(participant.Damage, participant.Passives.CritChanceBonus);
        session.BossCurrentHp = Math.Max(0, session.BossCurrentHp - hit.Damage);
        participant.TurnsTaken++;
        participant.TotalDamageDealt += hit.Damage;
        participant.Contributed = true;
        if (hit.Critical)
        {
            participant.CritCount++;
        }

        // Sifón de Almas (Hechicero): mismo criterio que combate solitario, cura ANTES del
        // contraataque — ver Modules/AdventureModule.ResolveTurnAsync.
        int healed = CombatMath.RollLifesteal(hit.Damage, participant.Passives.LifestealChance, participant.Passives.LifestealRatio);
        participant.CurrentHp = Math.Min(participant.MaxHp, participant.CurrentHp + healed);

        string critText = hit.Critical ? "💥 ¡GOLPE CRÍTICO! " : string.Empty;
        string healText = healed > 0 ? $" 🔮 Sifón de Almas curó {healed} HP." : string.Empty;

        if (session.BossCurrentHp <= 0)
        {
            // Resolved DENTRO del mismo lock que decide el resultado (no después, en
            // ResolveVictoryAsync): si no, entre soltar el lock y marcarlo, otro click
            // simultáneo vería la fase todavía Active y dispararía una segunda victoria
            // (recompensas duplicadas).
            session.Phase = RaidPhase.Resolved;
            return new AttackOutcome(AttackOutcomeKind.Victory, $"{critText}**{participant.DisplayName}** le dio el golpe final a **{session.BossName}**.{healText}");
        }

        var monsterHit = CombatMath.ResolveMonsterHit(session.BossDamage, participant.Defense, participant.Passives.DodgeChanceBonus, participant.Passives.DamageTakenMultiplier);
        string monsterLine;

        if (monsterHit.Dodged)
        {
            participant.DodgeCount++;
            monsterLine = $"💨 **{participant.DisplayName}** esquivó el contraataque.";
        }
        else
        {
            participant.CurrentHp = Math.Max(0, participant.CurrentHp - monsterHit.Damage);
            participant.TotalDamageTaken += monsterHit.Damage;
            monsterLine = $"El jefe le devolvió **{monsterHit.Damage}** a **{participant.DisplayName}**.";
            if (participant.IsKnockedOut)
            {
                monsterLine += $" 💀 **{participant.DisplayName}** quedó derribado.";
            }
        }

        string logLine = $"{critText}**{participant.DisplayName}** le hizo **{hit.Damage}** de daño al jefe.{healText} {monsterLine}";

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
        IAdventureRepository adventureRepository, IRaidSessionService raidSessions)
    {
        // session.Phase ya quedó en Resolved dentro del lock de ResolveParticipantTurn.
        raidSessions.Remove(session.RaidId);

        var results =new List<(RaidParticipant Participant, CombatReward Reward, Item? Drop, LevelUpOutcome Outcome)>();

        // Solo cobra quien contribuyó (pegó al menos un golpe) y no se retiró — huir siempre
        // renuncia a la recompensa, igual que en combate solitario. Cada uno se lleva TODO, no se
        // reparte entre el grupo (ver Context del plan) — cada llamada persiste, en la MISMA
        // transacción, tanto su HP final como el oro/XP/drop y el highest_zone_cleared.
        foreach (var participant in session.Participants.Where(p => p.Contributed && !p.HasFled))
        {
            var reward = CombatRewardCalculator.RollHuntReward(participant.Level, session.BossGoldBonus, session.BossXpBonus);

            Item? drop = null;
            if (reward.DroppedSomething && session.BossDropItemNames.Count > 0)
            {
                string dropName = session.BossDropItemNames[Random.Shared.Next(session.BossDropItemNames.Count)];
                drop = await itemRepository.GetByNameAsync(dropName);
            }

            int hpDelta = participant.ToDbHpDelta(participant.CurrentHp - participant.StartingHp);
            var outcome = await adventureRepository.ApplyBossVictoryAsync(
                participant.DiscordId, reward.Gold, reward.Xp, hpDelta, drop?.ItemId, droppedItemQuantity: 1, session.ZoneId);

            results.Add((participant, reward, drop, outcome));
        }

        await session.ReplyTarget.UpdateAsync(BuildVictoryEmbed(session, logLine, results), new ComponentBuilder().Build());
    }

    private static async Task ResolveWipeAsync(RaidSession session, string logLine, IUserRepository userRepository, IRaidSessionService raidSessions)
    {
        // session.Phase ya quedó en Resolved dentro del lock de ResolveParticipantTurn.
        raidSessions.Remove(session.RaidId);

        // Los que ya se habían retirado (HasFled) ya persistieron su HP al huir — no tocarlos de
        // nuevo acá o se les aplicaría el delta dos veces.
        foreach (var participant in session.Participants.Where(p => p.IsKnockedOut && !p.HasFled))
        {
            int hpDelta = participant.ToDbHpDelta(participant.CurrentHp - participant.StartingHp);
            await userRepository.ApplyCombatHpDeltaAsync(participant.DiscordId, hpDelta);
        }

        await session.ReplyTarget.UpdateAsync(BuildWipeEmbed(session, logLine), new ComponentBuilder().Build());
    }

    // ============================================================
    // Embeds y botones — de solo presentación, públicos por el mismo motivo que el resto del bot:
    // Modules/TextCommandModule.Combat.cs los reusa tal cual.
    // ============================================================

    public static Embed BuildLobbyEmbed(RaidSession session)
    {
        List<RaidParticipant> participants;
        lock (session.Lock)
        {
            participants = session.Participants.ToList();
        }

        string roster = participants.Count == 0
            ? "_Nadie se sumó todavía._"
            : string.Join('\n', participants.Select(p => $"• {p.DisplayName}"));

        return new EmbedBuilder()
            .WithTitle($"👑 Raid: {session.BossName} {session.BossEmoji}")
            .WithColor(Color.Purple)
            .WithDescription(
                $"Jefe de **{session.ZoneName}**. Clickeá **Unirse** para sumarte — se cierra en " +
                $"{(int)LobbyDuration.TotalSeconds}s o cuando quien lo arrancó clickee **Empezar ya**.\n" +
                $"Mínimo {MinParticipantsToStart}, máximo {MaxParticipants} jugadores.")
            .AddField($"👥 Anotados ({participants.Count}/{MaxParticipants})", roster, false)
            .Build();
    }

    public static MessageComponent BuildLobbyButtons(Guid raidId)
    {
        return new ComponentBuilder()
            .WithButton("Unirse", $"raid_join:{raidId}", ButtonStyle.Success, new Emoji("🙋"))
            .WithButton("Empezar ya", $"raid_start:{raidId}", ButtonStyle.Primary, new Emoji("▶️"))
            .Build();
    }

    public static Embed BuildCombatEmbed(RaidSession session, string logLine)
    {
        List<RaidParticipant> participants;
        int bossHp;
        lock (session.Lock)
        {
            participants = session.Participants.ToList();
            bossHp = session.BossCurrentHp;
        }

        string roster = string.Join('\n', participants.Select(p =>
        {
            string status = p.HasFled ? "🏃" : p.IsKnockedOut ? "💀" : "✅";
            return $"{status} **{p.DisplayName}** — {HpLine(p.CurrentHp, p.MaxHp)}";
        }));

        return new EmbedBuilder()
            .WithTitle($"⚔️ Raid contra {session.BossName} {session.BossEmoji}")
            .WithColor(Color.Gold)
            .WithDescription(logLine)
            .AddField($"{session.BossEmoji} HP de {session.BossName}", HpLine(bossHp, session.BossMaxHp), false)
            .AddField("👥 Participantes", roster, false)
            .Build();
    }

    public static MessageComponent BuildCombatButtons(Guid raidId)
    {
        return new ComponentBuilder()
            .WithButton("Atacar", $"raid_attack:{raidId}", ButtonStyle.Primary, new Emoji("⚔️"))
            .WithButton("Huir", $"raid_flee:{raidId}", ButtonStyle.Danger, new Emoji("🏃"))
            .Build();
    }

    private static Embed BuildVictoryEmbed(
        RaidSession session, string logLine, IReadOnlyList<(RaidParticipant Participant, CombatReward Reward, Item? Drop, LevelUpOutcome Outcome)> results)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"🏆 ¡{session.BossName} {session.BossEmoji} derrotado!")
            .WithColor(Color.Green)
            .WithDescription(logLine);

        if (results.Count == 0)
        {
            embed.AddField("Botín", "_Nadie llegó a pegarle un golpe — sin recompensa esta vez._", false);
        }

        foreach (var (participant, reward, drop, outcome) in results)
        {
            string dropLine = drop is not null ? $"\n🎁 {ItemDisplay.Format(drop.Emoji, drop.Name)}" : string.Empty;
            string levelLine = outcome.LevelsGained > 0 ? $"\n🎉 ¡Subió a nivel {outcome.Player.Level}!" : string.Empty;

            embed.AddField(
                participant.DisplayName,
                $"💰 {reward.Gold} oro · 📊 {reward.Xp} XP{dropLine}{levelLine}",
                true);
        }

        return embed.Build();
    }

    private static Embed BuildWipeEmbed(RaidSession session, string logLine)
    {
        return new EmbedBuilder()
            .WithTitle($"💀 El grupo cayó ante {session.BossName} {session.BossEmoji}")
            .WithColor(Color.DarkRed)
            .WithDescription($"{logLine}\n\nSin recompensa esta vez. Usá **/heal** para recuperarte.")
            .Build();
    }

    private static Embed BuildAbandonedEmbed(RaidSession session)
    {
        return new EmbedBuilder()
            .WithTitle($"🏃 Raid contra {session.BossName} {session.BossEmoji} abandonado")
            .WithColor(Color.DarkGrey)
            .WithDescription("Todos los participantes se retiraron antes de derrotarlo.")
            .Build();
    }

    private static Embed BuildCancelledEmbed(RaidSession session, int confirmedCount)
    {
        return new EmbedBuilder()
            .WithTitle($"👑 Raid contra {session.BossName} {session.BossEmoji} cancelado")
            .WithColor(Color.DarkGrey)
            .WithDescription($"No se juntaron los {MinParticipantsToStart} jugadores mínimos a tiempo ({confirmedCount} confirmado(s)).")
            .Build();
    }

    private static string HpLine(int current, int max) => $"{ProgressBar.Render(current, max)}\n{current}/{max}";
}
