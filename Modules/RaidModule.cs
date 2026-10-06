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
    IRaidSessionService raidSessions,
    IGameEvents gameEvents,
    IBuffRepository buffRepository,
    ICooldownRepository cooldownRepository,
    IPlayerBonusService bonusService) : InteractionModuleBase<SocketInteractionContext>
{
    // Ver Services/RaidSettings.cs: el mínimo es configurable (2 por defecto).
    private static int MinParticipantsToStart => RaidSettings.MinParticipants;
    private const int MaxParticipants = RaidSettings.MaxParticipants;
    public static readonly TimeSpan LobbyDuration = TimeSpan.FromSeconds(60);

    [SlashCommand("raid", "Jefe de zona cooperativo: varios jugadores atacan al mismo jefe (mín. 2, cooldown de 5 h).")]
    public async Task HandleRaidAsync()
    {
        await DeferAsync();

        try
        {
            var rejection = await ValidateStartAsync(
                userRepository, itemRepository, monsterRepository, zoneRepository, combatSessions, raidSessions, Context.User.Id, cooldownRepository);
            if (rejection is not null)
            {
                await FollowupAsync(rejection.PlainMessage, embed: rejection.Embed, ephemeral: true);
                return;
            }

            var session = await BuildSessionAsync(
                userRepository, itemRepository, monsterRepository, zoneRepository, buffRepository, Context.User.Id, GameModule.GetDisplayName(Context.User), bonusService);

            if (!raidSessions.TryAdd(session) || !raidSessions.TryRegisterParticipant(Context.User.Id, session.RaidId))
            {
                await FollowupAsync("Justo se te adelantó otra acción, probá de nuevo en un toque.", ephemeral: true);
                return;
            }

            session.ReplyTarget = new InteractionCombatReplyTarget(Context.Interaction);
            ScheduleLobbyTimeout(session, raidSessions, adventureRepository);

            await FollowupAsync(embed: BuildLobbyEmbed(session), components: BuildLobbyButtons(session.RaidId));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
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

            // Va antes de ValidateJoinAsync: quien ya está adentro (ej. quien arrancó el lobby) figura
            // como "ocupado" y recibiría el mensaje engañoso de "ya estás en medio de un combate".
            bool alreadyIn;
            lock (session.Lock)
            {
                alreadyIn = session.Participants.Any(p => p.DiscordId == discordId);
            }

            if (alreadyIn)
            {
                await FollowupAsync("Ya estás anotado en este raid.", ephemeral: true);
                return;
            }

            var rejection = await ValidateJoinAsync(userRepository, itemRepository, zoneRepository, combatSessions, raidSessions, session, discordId, cooldownRepository);
            if (rejection is not null)
            {
                await FollowupAsync(rejection.PlainMessage, embed: rejection.Embed, ephemeral: true);
                return;
            }

            var participant = await BuildParticipantAsync(
                userRepository, itemRepository, buffRepository, discordId, GameModule.GetDisplayName(Context.User), bonusService);

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
        catch (Exception ex)
        {
            BotLog.Error(ex);
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
                await FollowupAsync($"Hace falta un mínimo de {MinParticipantsToStart} jugadores para arrancar — por ahora hay {count}.", ephemeral: true);
                return;
            }

            await TryActivateAsync(session, raidSessions, adventureRepository);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! Algo falló arrancando el raid, intentá de nuevo en un momento.", ephemeral: true);
        }
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
                        _ => ResolveParticipantTurn(session, participant, useAbility),
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
                case AttackOutcomeKind.AbilityUnavailable:
                    await FollowupAsync(outcome.LogLine!, ephemeral: true);
                    return;
                case AttackOutcomeKind.Continues:
                    await session.ReplyTarget.UpdateAsync(BuildCombatEmbed(session, outcome.LogLine!), BuildCombatButtons(raidId));
                    return;
                case AttackOutcomeKind.Victory:
                    await ResolveVictoryAsync(session, outcome.LogLine!, userRepository, itemRepository, adventureRepository, raidSessions, gameEvents);
                    return;
                case AttackOutcomeKind.Wipe:
                    await ResolveWipeAsync(session, outcome.LogLine!, userRepository, raidSessions, gameEvents);
                    return;
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
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
        catch (Exception ex)
        {
            BotLog.Error(ex);
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

    public static async Task<RaidRejection?> ValidateJoinAsync(
        IUserRepository userRepository, IItemRepository itemRepository, IZoneRepository zoneRepository,
        ICombatSessionService combatSessions, IRaidSessionService raidSessions, RaidSession session, ulong discordId,
        ICooldownRepository? cooldownRepository = null)
    {
        if (combatSessions.Peek(discordId) is not null || raidSessions.IsInAnyRaid(discordId))
        {
            return new RaidRejection(AdventureModule.BuildAlreadyInCombatMessage(), null);
        }

        if (await CooldownRejectionAsync(cooldownRepository, discordId) is { } onCooldown)
        {
            return onCooldown;
        }

        var player = await userRepository.GetOrCreateUserAsync(discordId);

        if (player.InGate)
        {
            return new RaidRejection(null, AdventureModule.BuildInGateEmbed());
        }

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
            .WithTitle($"⏳ {CooldownCatalog.Raid.Emoji} Raid: todavía no podés")
            .WithDescription($"Te falta **{TimeFormat.Remaining(remaining)}** para entrar a un raid. El raid y el jefe (**/boss**) comparten el mismo cooldown.")
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
            catch (Exception ex)
            {
                // Si ya no se puede editar el mensaje (token vencido, mensaje borrado) no hay nada
                // más que hacer — el raid igual queda marcado Resolved/sacado del índice arriba. Se registra:
                // acá también puede haber fallado la base al activar el raid.
                BotLog.Error(ex);
            }
        }, cts.Token);
    }

    private enum AttackOutcomeKind { NotActive, NotAParticipant, ParticipantInactive, AbilityUnavailable, Continues, Victory, Wipe }

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
        IAdventureRepository adventureRepository, IRaidSessionService raidSessions, IGameEvents gameEvents)
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
            // "Primera vez" por participante: el cofre es 100% solo para quien nunca había derrotado a este jefe (se mira ANTES de aplicar la victoria).
            bool firstClear = ((await userRepository.GetByDiscordIdAsync(participant.DiscordId))?.HighestZoneCleared ?? 0) < session.ZoneId;
            var reward = CombatRewardCalculator.RollBossReward(participant.Level, session.BossGoldBonus, session.BossXpBonus, firstClear, participant.Bonuses);

            Item? drop = null;
            if (reward.DroppedSomething && session.BossDropItemNames.Count > 0)
            {
                string dropName = session.BossDropItemNames[Random.Shared.Next(session.BossDropItemNames.Count)];
                drop = await itemRepository.GetByNameAsync(dropName);
            }

            int hpDelta = participant.ToDbHpDelta(participant.CurrentHp - participant.StartingHp);
            var outcome = await adventureRepository.ApplyBossVictoryAsync(
                participant.DiscordId, reward.Gold, reward.Xp, hpDelta, drop?.ItemId, droppedItemQuantity: 1, session.ZoneId);

            await gameEvents.RecordVictoryAsync(participant.DiscordId, GameEventKinds.RaidWin, outcome);

            results.Add((participant, reward, drop, outcome));
        }

        await session.ReplyTarget.UpdateAsync(BuildVictoryEmbed(session, logLine, results), new ComponentBuilder().Build());
    }

    private static async Task ResolveWipeAsync(
        RaidSession session, string logLine, IUserRepository userRepository, IRaidSessionService raidSessions, IGameEvents gameEvents)
    {
        // session.Phase ya quedó en Resolved dentro del lock de ResolveParticipantTurn.
        raidSessions.Remove(session.RaidId);

        // Los que ya se habían retirado (HasFled) ya persistieron su HP al huir — no tocarlos de
        // nuevo acá o se les aplicaría el delta dos veces.
        // Los caídos pagan la misma penalidad que en un combate solitario (EXP del nivel a 0, 5 % del oro de la billetera): el mensaje es compartido,
        // así que avisa la regla en general en vez de listar a cada uno.
        foreach (var participant in session.Participants.Where(p => p.IsKnockedOut && !p.HasFled))
        {
            int hpDelta = participant.ToDbHpDelta(participant.CurrentHp - participant.StartingHp);
            await userRepository.ApplyCombatHpDeltaAsync(participant.DiscordId, hpDelta);
            await AdventureModule.ApplyDeathPenaltyAsync(userRepository, participant.DiscordId);
            await gameEvents.RecordAsync(participant.DiscordId, GameEventKinds.FightLost, session.ZoneId, detail: "raid");
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
            .WithMonsterPortrait(session.BossPortrait) // la cara del jefe en todos los mensajes del raid
            .WithDescription(
                $"Jefe de **{session.ZoneName}**. Quien arrancó el raid ya está adentro; el resto clickea " +
                $"**Unirse** — se cierra en {(int)LobbyDuration.TotalSeconds}s o cuando quien lo arrancó clickee **Empezar ya**.\n" +
                $"Mínimo {MinParticipantsToStart}, máximo {MaxParticipants} jugadores (si no llegan al mínimo, se cancela).\n" +
                "💪 Es un jefe de raid: mucho más duro que **/boss**, y su vida crece con cada jugador que se suma. " +
                "Ir con arma equipada casi es obligatorio.")
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
            // Estado de la habilidad de cada uno (el botón "Habilidad" es compartido y genérico).
            string abilityLine = p.IsActive ? "\n" + AdventureModule.BuildAbilityStatusLine(p.PlayerClass, p.Ability) : string.Empty;
            return $"{status} **{p.DisplayName}** — {HpLine(p.CurrentHp, p.MaxHp)}{abilityLine}";
        }));

        return new EmbedBuilder()
            .WithTitle($"⚔️ Raid contra {session.BossName} {session.BossEmoji}")
            .WithColor(Color.Gold)
            .WithMonsterPortrait(session.BossPortrait)
            .WithDescription(logLine)
            .AddField($"{session.BossEmoji} HP de {session.BossName}", HpLine(bossHp, session.BossMaxHp), false)
            .AddField("👥 Participantes", roster, false)
            .Build();
    }

    public static MessageComponent BuildCombatButtons(Guid raidId)
    {
        return new ComponentBuilder()
            .WithButton("Atacar", $"raid_attack:{raidId}", ButtonStyle.Primary, new Emoji("⚔️"))
            .WithButton("Habilidad", $"raid_ability:{raidId}", ButtonStyle.Success, new Emoji("✨"))
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

    private static Embed BuildFailedEmbed(RaidSession session)
    {
        return new EmbedBuilder()
            .WithTitle($"👑 Raid contra {session.BossName} {session.BossEmoji} cancelado")
            .WithColor(Color.DarkGrey)
            .WithDescription("Algo falló al arrancar el raid. Probá de nuevo con **/raid** en un momento.")
            .Build();
    }

    private static string HpLine(int current, int max) => $"{ProgressBar.Render(current, max)}\n{current}/{max}";
}
