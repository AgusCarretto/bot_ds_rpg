using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// Todo lo que usa la Aventura, junto (para no pasar quince parámetros a cada método estático).
public sealed record AdventureDeps(
    IUserRepository Users, IAdventureRepository Adventure, ICooldownRepository Cooldowns, IMonsterRepository Monsters, IAdventureCombatStarter Starter,
    ICombatSessionService Combat, IRaidSessionService Raids, IDungeonService Dungeons, IAdventureService Adventures, IZoneBoxService ZoneBoxes,
    IItemRepository Items, IGameEvents Events, IPlayerBonusService Bonuses, IZoneRepository Zones);

// La Aventura (v0.57.0, docs/aventura.md; /aventura, aa aventura). 5 escenas con 3 caminos (🛡️ seguro, ⚔️ arriesgado, ❓ misterioso) y el Guardián del Camino al final,
// en un solo mensaje que se va editando. Las peleas son las de siempre (AdventureModule.ResolveTurnCoreAsync, CommandName "aventura"): al ganar, ese mismo turno llama a
// AfterWinAsync de acá y la pantalla vuelve a la escena siguiente. Sin comida adentro (FightLock + CombatHeal). Si morís, la penalidad de siempre y sin premio.
public class AventuraModule(AdventureDeps deps) : InteractionModuleBase<SocketInteractionContext>
{
    public const string PathPrefix = "aven_path";

    private AdventureDeps Deps => deps;

    [SlashCommand("aventura", "Una aventura de 5 escenas: elegí caminos, peleá y llegá al Guardián (cada 3 h).")]
    public async Task HandleAdventureAsync()
    {
        await DeferAsync();

        try
        {
            var view = await StartAsync(Deps, Context.User.Id);
            await FollowupAsync(view.Error, embed: view.Embed, components: view.Components, ephemeral: view.Embed is null);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! No pude arrancar la aventura, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    [ComponentInteraction($"{PathPrefix}:*:*:*")]
    public async Task HandlePathAsync(string ownerRaw, string token, string pathRaw)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync(Loc.T("Esa aventura es de otra persona: arrancá la tuya con **/aventura**."), ephemeral: true);
                return;
            }

            if (!Enum.TryParse<AdventurePath>(pathRaw, out var path))
            {
                return;
            }

            var view = await ChooseAsync(Deps, ownerId, token, path, new InteractionCombatReplyTarget(Context.Interaction));
            if (view.Embed is null)
            {
                await FollowupAsync(view.Error, ephemeral: true);
                return;
            }

            await ModifyOriginalResponseAsync(p => { p.Embed = view.Embed; p.Components = view.Components; });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! Algo falló en la aventura, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    public sealed record AdventureView(string? Error, Embed? Embed = null, MessageComponent? Components = null);

    // Arranca: cobra las 3 horas y muestra la escena 1. No arranca en pelea, sin vida, en El Fogón ni con otra aventura abierta (no cobra nada).
    public static async Task<AdventureView> StartAsync(AdventureDeps deps, ulong discordId)
    {
        // v0.58.2: con una aventura abierta eligiendo camino, /aventura vuelve a mostrar la escena (sin cobrar nada). El dueño, 2026-11-16: a Matt se le ganó una pelea y el mensaje
        // no pasó a la escena siguiente, así que quedó sin botones y trabado 3 minutos hasta que venció. Ahora siempre hay una forma de volver a verla.
        if (await TryResumeAsync(deps, discordId) is { } resumed)
        {
            return resumed;
        }

        if (FightGuard.IsInFight(deps.Combat, deps.Raids, null, discordId, deps.Dungeons) || deps.Adventures.IsInAdventure(discordId))
        {
            return new AdventureView(Loc.T("Ya estás en una pelea o en una aventura: terminala primero."));
        }

        var player = await deps.Users.GetOrCreateUserAsync(discordId);
        if (player.InGate)
        {
            return new AdventureView(Loc.T("En El Fogón Eterno no hay caminos para recorrer: volvé a una zona con **/zone**."));
        }

        if (player.CurrentHp <= 0)
        {
            return new AdventureView(Loc.T("Estás sin vida: curate con **/heal** antes de salir de aventura."));
        }

        var definition = AdventureRules.Cooldown;
        if (await deps.Cooldowns.GetRemainingAsync(discordId, definition.CommandName, definition.Duration) is { } wait)
        {
            return new AdventureView(Loc.F($"🗺️ Tu próxima aventura sale en **{TimeFormat.Remaining(wait)}**."));
        }

        if (!await deps.Adventure.TryClaimCooldownAsync(discordId, definition.CommandName, definition.Duration))
        {
            return new AdventureView(Loc.T("Justo se te adelantó otra ejecución de este comando, probá de nuevo en un toque."));
        }

        var run = deps.Adventures.Start(discordId, new Random());
        if (run is null)
        {
            return new AdventureView(Loc.T("Ya estás en una pelea o en una aventura: terminala primero."));
        }

        await deps.Events.RecordAsync(discordId, GameEventKinds.AdventureStart, player.CurrentZoneId);
        return BuildSceneView(run, player);
    }

    // Elegir un camino. Los eventos sin pelea pasan directo a la escena siguiente; las peleas arrancan el combate de siempre en este mismo mensaje.
    public static async Task<AdventureView> ChooseAsync(AdventureDeps deps, ulong discordId, string token, AdventurePath path, ICombatReplyTarget replyTarget)
    {
        var run = deps.Adventures.Peek(discordId);
        if (run is null)
        {
            return new AdventureView(Loc.T("Esa aventura ya terminó. Arrancá otra con **/aventura** cuando puedas."));
        }

        AdventureEvent? mystery = null;
        bool guardian;
        lock (run.Lock)
        {
            if (run.Token != token || run.Phase != AdventurePhase.Choosing)
            {
                return new AdventureView(Loc.T("Ese botón es de un momento anterior de la aventura."));
            }

            guardian = run.AtGuardian;
            if (!guardian && path == AdventurePath.Mystery)
            {
                mystery = AdventureRules.RollEvent(Random.Shared.Next(100));
            }

            // Reservar la jugada: un segundo click no hace nada mientras esta se resuelve.
            run.Phase = AdventurePhase.Fighting;
            run.LastActivityUtc = DateTime.UtcNow;
        }

        var player = await deps.Users.GetByDiscordIdAsync(discordId) ?? throw new InvalidOperationException("La aventura no tiene jugador.");

        // Los eventos sin pelea.
        if (mystery is AdventureEvent.Chest or AdventureEvent.Campfire or AdventureEvent.Trap or AdventureEvent.Shrine)
        {
            string note;
            switch (mystery)
            {
                case AdventureEvent.Chest:
                    lock (run.Lock) { run.LootPercent += AdventureRules.ChestBonusPercent; }
                    note = Loc.F($"🧰 Encontraste un cofre abandonado: **+{AdventureRules.ChestBonusPercent} %** de botín.");
                    break;
                case AdventureEvent.Campfire:
                    player = await deps.Users.ApplyCombatHpDeltaAsync(discordId, (int)Math.Round(player.MaxHp * AdventureRules.CampfireHealPercent / 100.0));
                    note = Loc.F($"🔥 Una fogata encendida: descansás y recuperás el **{AdventureRules.CampfireHealPercent} %** de la vida.");
                    break;
                case AdventureEvent.Trap:
                    int lose = Math.Min((int)Math.Round(player.MaxHp * AdventureRules.TrapDamagePercent / 100.0), Math.Max(0, player.CurrentHp - 1));
                    player = await deps.Users.ApplyCombatHpDeltaAsync(discordId, -lose);
                    note = Loc.F($"🕸️ ¡Una trampa! Perdés el **{AdventureRules.TrapDamagePercent} %** de la vida.");
                    break;
                default:
                    lock (run.Lock) { run.DamageBonusPercent += AdventureRules.ShrineDamagePercent; }
                    note = Loc.F($"⛩️ Un santuario: **+{AdventureRules.ShrineDamagePercent} %** de daño por el resto de la aventura.");
                    break;
            }

            lock (run.Lock)
            {
                run.Scene++;
                run.LastNote = note;
                run.Phase = AdventurePhase.Choosing;
                run.LastActivityUtc = DateTime.UtcNow;
            }

            return BuildSceneView(run, player);
        }

        // Una pelea: el Guardián, el élite (arriesgado o emboscada) o un común.
        MonsterTemplate? monster;
        bool risky = false;
        if (guardian)
        {
            var elite = await deps.Monsters.GetTravelMonsterByZoneAsync(player.CurrentZoneId);
            monster = elite is null ? null : AdventureRules.Guardian(elite);
        }
        else if (path == AdventurePath.Risky || mystery == AdventureEvent.Ambush)
        {
            risky = path == AdventurePath.Risky;
            var elite = await deps.Monsters.GetTravelMonsterByZoneAsync(player.CurrentZoneId);
            monster = elite is null ? null : AdventureRules.Scale(elite, AdventureRules.MonsterMultiplier);
        }
        else
        {
            var pool = await deps.Monsters.GetMonstersByZoneAsync(player.CurrentZoneId);
            monster = pool.Count == 0 ? null : AdventureRules.Scale(MonsterCatalog.RollFrom(pool), AdventureRules.MonsterMultiplier);
        }

        int damageBonus;
        lock (run.Lock)
        {
            damageBonus = run.DamageBonusPercent;
            run.PendingRiskyBonus = risky;
            if (mystery == AdventureEvent.Ambush)
            {
                run.LastNote = Loc.T("👤 ¡Una emboscada! El élite de la zona te cierra el paso.");
            }
        }

        var started = monster is null
            ? new CombatStartOutcome(CombatStartStatus.NoMonstersInZone, null, null)
            : await deps.Starter.PrepareAdventureFightAsync(discordId, monster, damageBonus);
        if (started.Status != CombatStartStatus.Started || !deps.Combat.TryStart(discordId, started.State!, replyTarget))
        {
            deps.Adventures.End(discordId);
            return new AdventureView(Loc.T("No pude arrancar la pelea de la aventura: se terminó acá, sin penalidad."));
        }

        lock (run.Lock)
        {
            run.LastActivityUtc = DateTime.UtcNow;
        }

        // Sin comida: los botones de la pelea van sin el desplegable (CombatHeal.HasMenu no incluye la aventura).
        return new AdventureView(null, AdventureModule.BuildEncounterEmbed(started.State!), AdventureModule.BuildCombatButtons(started.State!));
    }

    // La escena actual de una aventura que está ELIGIENDO camino (null si no hay aventura, si está peleando o si ya terminó). Le da 3 minutos nuevos para elegir: quien la pide es
    // porque no la estaba viendo (v0.58.2).
    public static async Task<AdventureView?> TryResumeAsync(AdventureDeps deps, ulong discordId)
    {
        var run = deps.Adventures.Peek(discordId);
        if (run is null)
        {
            return null;
        }

        lock (run.Lock)
        {
            if (run.Phase != AdventurePhase.Choosing)
            {
                return null;
            }

            run.LastActivityUtc = DateTime.UtcNow;
        }

        var player = await deps.Users.GetByDiscordIdAsync(discordId);
        return player is null ? null : BuildSceneView(run, player);
    }

    // Llamado por el turno que ganó una pelea de la aventura (la vida de la pelea ya está guardada). Pasa a la escena siguiente o, si era el Guardián, paga.
    public static async Task<(Embed Embed, MessageComponent Components)> AfterWinAsync(AdventureDeps deps, ulong discordId, CombatState fight)
    {
        var run = deps.Adventures.Find(discordId);
        var player = await deps.Users.GetByDiscordIdAsync(discordId) ?? throw new InvalidOperationException("La aventura no tiene jugador.");
        if (run is null)
        {
            return (new EmbedBuilder().WithTitle(Loc.T("🗺️ Aventura")).WithDescription(Loc.T("La aventura ya había terminado.")).Build(), new ComponentBuilder().Build());
        }

        bool finished;
        int loot;
        lock (run.Lock)
        {
            finished = run.AtGuardian;
            if (run.PendingRiskyBonus)
            {
                run.LootPercent += AdventureRules.RiskyBonusPercent;
                run.PendingRiskyBonus = false;
            }

            loot = run.LootPercent;
            if (!finished)
            {
                run.Scene++;
                run.LastNote = Loc.F($"⚔️ Venciste a **{fight.MonsterName}**.");
                run.Phase = AdventurePhase.Choosing;
                run.LastActivityUtc = DateTime.UtcNow;
            }
        }

        if (!finished)
        {
            var scene = BuildSceneView(run, player);
            return (scene.Embed!, scene.Components!);
        }

        deps.Adventures.End(discordId);
        return await PayAsync(deps, player, fight, loot);
    }

    // El premio: 3 viajes de oro y EXP por el botín y una chance de la caja del viaje de la zona. POR ZONA, no por nivel (el dueño, 2026-11-16: «ni que hablar
    // que el premio es por zona»): los viajes se calculan con el nivel mínimo de la zona donde la hiciste, así quedarse en una zona fácil rinde lo de esa zona.
    // v0.57.2: con el bono de oro y EXP del monstruo de viaje de la zona, como un /travel de verdad. Hasta la v0.57.1 se pasaba 0 y el «viaje» quedaba en lo que paga
    // el nivel solo: en la Zona 5 ~13 % de un viaje real, en la 10 ~4 % (el bono es casi todo lo que hace crecer el premio con la zona).
    private static async Task<(Embed Embed, MessageComponent Components)> PayAsync(AdventureDeps deps, User player, CombatState fight, int loot)
    {
        ulong discordId = (ulong)player.DiscordId;
        var bonuses = fight.Bonuses ?? await deps.Bonuses.GetAsync(discordId, player.FuegoNuevo);
        int referenceLevel = await ReferenceLevelAsync(deps, player.CurrentZoneId);
        var travelMonster = await deps.Monsters.GetTravelMonsterByZoneAsync(player.CurrentZoneId);
        var travel = RewardTravel(referenceLevel, travelMonster, bonuses);
        int gold = AdventureRules.ApplyLoot(travel.Gold * AdventureRules.RewardTravels, loot);
        int xp = AdventureRules.ApplyLoot(travel.Xp * AdventureRules.RewardTravels, loot);

        Item? box = null;
        if (Random.Shared.Next(100) < AdventureRules.BoxChancePercent && (await TravelBox.ChanceAsync(deps.ZoneBoxes, player.CurrentZoneId)).BoxName is { } boxName)
        {
            box = await deps.Items.GetByNameAsync(boxName);
        }

        var outcome = await deps.Adventure.ApplyVictoryAsync(discordId, gold, xp, 0, box?.ItemId, droppedItemQuantity: 1);
        await deps.Events.RecordAsync(discordId, GameEventKinds.AdventureWin, player.CurrentZoneId);
        if (outcome.LevelsGained > 0)
        {
            await deps.Events.RecordAsync(discordId, GameEventKinds.LevelUp, player.CurrentZoneId, outcome.LevelsGained, outcome.Player.Level.ToString());
        }

        var lines = new List<string>
        {
            Loc.T("Cruzás el portal con el Guardián a tus espaldas. ¡Llegaste al final del camino!"),
            string.Empty,
            Loc.F($"{UiEmojis.Gold} **Oro**: +{GameHistory.Number(gold)}"),
            Loc.F($"{UiEmojis.Xp} **EXP**: +{GameHistory.Number(xp)}"),
        };
        if (loot > 0)
        {
            lines.Add(Loc.F($"💰 **Botín**: +{loot} %"));
        }

        if (box is not null)
        {
            lines.Add(Loc.F($"{ItemEmojiRegistry.Label(box.Name, "📦")} — abrila con **/open**"));
        }

        var embed = new EmbedBuilder()
            .WithTitle(Loc.T("🏆 ¡Aventura completada!"))
            .WithColor(Color.Gold)
            .WithDescription(string.Join("\n", lines))
            .WithFooter(Loc.F($"La próxima aventura, en {(int)AdventureRules.Cooldown.Duration.TotalHours} horas."))
            .Build();
        return (embed, new ComponentBuilder().Build());
    }

    // UN viaje de la zona (sin la caja: la de la aventura va aparte): el nivel de referencia más el bono del monstruo de viaje. Sin monstruo de viaje, solo el nivel.
    public static CombatReward RewardTravel(int referenceLevel, MonsterTemplate? travelMonster, PlayerBonuses? bonuses) =>
        CombatRewardCalculator.RollTravelReward(referenceLevel, travelMonster?.GoldBonus ?? 0, travelMonster?.XpBonus ?? 0, bonuses, 0);

    // El nivel con el que paga la zona: su nivel mínimo (como alguien que recién llega). Sin zona conocida, 1.
    public static async Task<int> ReferenceLevelAsync(AdventureDeps deps, int zoneId) =>
        Math.Max(1, (await deps.Zones.GetByIdAsync(zoneId))?.MinLevel ?? 1);

    // La pantalla de una escena: lo que pasó antes, el texto, tu vida y el botín, y los caminos (o el botón del Guardián).
    public static AdventureView BuildSceneView(AdventureRun run, User player)
    {
        AdventureScene scene;
        int number, loot;
        string? note;
        string token;
        bool guardian;
        lock (run.Lock)
        {
            scene = run.CurrentScene;
            number = Math.Min(run.Scene, AdventureRules.Scenes);
            loot = run.LootPercent;
            note = run.LastNote;
            token = run.Token;
            guardian = run.AtGuardian;
        }

        int percent = player.MaxHp <= 0 ? 0 : (int)Math.Round(100.0 * Math.Clamp(player.CurrentHp, 0, player.MaxHp) / player.MaxHp);
        var lines = new List<string>();
        if (note is not null)
        {
            lines.Add(note);
            lines.Add(string.Empty);
        }

        lines.Add(Loc.Tr(scene.Text));
        lines.Add(string.Empty);
        lines.Add(Loc.F($"❤️ **Tu vida**: {ProgressBar.Render(percent, 100, 10)} {percent} %"));
        lines.Add(Loc.F($"💰 **Botín**: +{loot} %"));

        var embed = new EmbedBuilder()
            .WithTitle(Loc.F($"{Loc.Tr(scene.Title)} · {number}/{AdventureRules.Scenes}"))
            .WithColor(new Color(0x8E6E3D))
            .WithDescription(string.Join("\n", lines))
            .WithFooter(Loc.T("Sin comida en la aventura · 3 min por elección"));

        ulong owner = (ulong)player.DiscordId;
        var buttons = new ComponentBuilder();
        if (guardian)
        {
            buttons.WithButton(Loc.T("Enfrentar al Guardián"), $"{PathPrefix}:{owner}:{token}:{AdventurePath.Risky}", ButtonStyle.Danger, new Emoji("🗿"));
        }
        else
        {
            buttons.WithButton(Loc.T("Seguro"), $"{PathPrefix}:{owner}:{token}:{AdventurePath.Safe}", ButtonStyle.Success, new Emoji("🛡️"));
            buttons.WithButton(Loc.F($"Arriesgado (+{AdventureRules.RiskyBonusPercent} %)"), $"{PathPrefix}:{owner}:{token}:{AdventurePath.Risky}", ButtonStyle.Danger, new Emoji("⚔️"));
            buttons.WithButton(Loc.T("Misterioso"), $"{PathPrefix}:{owner}:{token}:{AdventurePath.Mystery}", ButtonStyle.Secondary, new Emoji("❓"));
        }

        return new AdventureView(null, embed.Build(), buttons.Build());
    }
}
