using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /use consume un Consumible del inventario para curar HP sin gastar oro — a diferencia de /heal
// (que sí cuesta oro y por eso queda bloqueado durante el combate, ver TavernModule), este comando
// funciona tanto fuera como dentro de un combate por turnos activo.
public class UseModule(
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IInventoryRepository inventoryRepository,
    ICombatSessionService combatSessions,
    IBuffRepository buffRepository,
    IRaidSessionService raidSessions,
    IGameEvents gameEvents,
    IPlayerBonusService bonusService) : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /use
    [SlashCommand("use", "Usá un consumible de tu inventario para curar HP (funciona incluso en combate, sin gastar oro).")]
    public async Task HandleUseAsync([Summary("item", "Elegí de la lista la comida que querés usar.")] [Autocomplete(typeof(UseItemAutocompleteHandler))] string itemName)
    {
        await DeferAsync();

        try
        {
            var result = await ExecuteUseAsync(userRepository, itemRepository, inventoryRepository, combatSessions, buffRepository, Context.User.Id, itemName, raidSessions, gameEvents, bonusService);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: result.Embed is null);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync(Loc.T("¡Upa! No pude usar ese ítem, intentá de nuevo en un momento."), ephemeral: true);
        }
    }

    // Estático (sin dependencia de Context) para que Modules/TextCommandModule.cs comparta
    // exactamente la misma lógica en "aa use". Exactamente uno de los dos campos del resultado
    // viene con valor.
    public sealed record UseResult(string? PlainMessage, Embed? Embed);

    public static async Task<UseResult> ExecuteUseAsync(
        IUserRepository userRepository,
        IItemRepository itemRepository,
        IInventoryRepository inventoryRepository,
        ICombatSessionService combatSessions,
        IBuffRepository buffRepository,
        ulong discordId,
        string itemName,
        IRaidSessionService? raidSessions = null,
        IGameEvents? gameEvents = null,
        IPlayerBonusService? bonusService = null)
    {
        // Dentro de un jefe en equipo que ya está peleando, comer es una ACCIÓN DEL COMBATE (v0.19.0): gasta el turno, es una vez por jugador y lo maneja el propio combate. Antes /use ahí curaba la vida de la base
        // y no la de la pelea (que vive en memoria): la comida se gastaba sin servir. Se pasa raidSessions solo desde los comandos reales; las pruebas viejas no lo pasan y siguen igual.
        if (raidSessions is not null && raidSessions.IsInActiveFight(discordId))
        {
            return new UseResult(
                gameEvents is null
                    ? Loc.T("Estás peleando en equipo: para comer usá el botón 🍖 **Comer** del combate cuando sea tu turno.")
                    : await RaidModule.ExecuteEatAsync(raidSessions, userRepository, itemRepository, inventoryRepository, buffRepository, gameEvents, discordId, itemName),
                null);
        }

        var item = await itemRepository.GetByNameAsync(itemName);
        if (item is null)
        {
            return new UseResult(Loc.F($"No encontré ningún ítem llamado **{itemName}**."), null);
        }

        // Lo de las mascotas (v0.10.0) no se come: la comida es para ellas y el huevo se abre.
        if (item.Type == ShopCatalog.PetFoodType)
        {
            return new UseResult(Loc.F($"**{ItemDisplay.Format(item.Emoji, item.Name)}** es para tus mascotas, no para vos: dásela con **/pet feed**."), null);
        }

        if (item.Type == "Huevo")
        {
            return new UseResult(Loc.F($"**{ItemDisplay.Format(item.Emoji, item.Name)}** no se usa: se abre con **/open**."), null);
        }

        if (item.Type != "Consumable")
        {
            return new UseResult(Loc.F($"**{ItemDisplay.Format(item.Emoji, item.Name)}** es de tipo `{item.Type}` y no se puede usar (solo Consumibles)."), null);
        }

        // Los banquetes (comida Mítica) además de curar dan un buff de ataque (item_buffs). Se usan aunque estés al máximo de HP:
        // lo que importa es el buff.
        var buff = await buffRepository.GetItemBuffAsync(item.ItemId);

        var session = combatSessions.Peek(discordId);

        // --- Fuera de combate: se comporta como un /heal gratuito, directo contra la base. ---
        if (session is null)
        {
            var player = await userRepository.GetOrCreateUserAsync(discordId);
            // La vida que dice el mensaje es la de siempre en todas las pantallas: con el factor de clase (GameData/HpDisplay.cs).
            double hpMultiplier = await bonusService.HpMultiplierAsync(player);
            if (player.CurrentHp >= player.MaxHp && buff is null)
            {
                return new UseResult(null, new EmbedBuilder()
                    .WithTitle(AlreadyFullTitle())
                    .WithDescription(Loc.F($"Tenés {HpDisplay.Scale(player.CurrentHp, hpMultiplier)}/{HpDisplay.Scale(player.MaxHp, hpMultiplier)} HP, no necesitás usar **{ItemDisplay.Format(item.Emoji, item.Name)}** todavía."))
                    .WithColor(Color.Green)
                    .Build());
            }

            if (!await inventoryRepository.TryConsumeAsync(discordId, item.ItemId, 1))
            {
                return new UseResult(Loc.F($"No tenés **{ItemDisplay.Format(item.Emoji, item.Name)}** en tu inventario."), null);
            }

            // La comida cura item.StatValue de la vida que ves (como dentro de una pelea): a la base se le suma la vida real equivalente (GameData/HpDisplay.cs).
            var healed = await userRepository.RestoreHpAsync(discordId, HpDisplay.ToReal(item.StatValue, hpMultiplier));
            if (buff is not null)
            {
                await buffRepository.ActivateAttackAsync(discordId, buff.AttackPercent, buff.Minutes, item.Name);
            }

            return new UseResult(null, new EmbedBuilder()
                .WithTitle(Loc.T("🍖 ¡Usaste un consumible!"))
                .WithDescription(Loc.F($"Usaste **{ItemDisplay.Format(item.Emoji, item.Name)}** y recuperaste HP. Ahora tenés **{HpDisplay.Scale(healed.CurrentHp, hpMultiplier)}/{HpDisplay.Scale(healed.MaxHp, hpMultiplier)}** HP.") + BuffLine(buff))
                .WithItemThumbnail(item.Emoji)
                .WithColor(Color.Green)
                .Build());
        }

        // --- En combate: cura gratis, pero le cede el turno al monstruo — igual que atacar,
        // usar un ítem también implica el riesgo de recibir un golpe (si no, sería curación
        // ilimitada sin ningún costo mientras dure el inventario). ---
        var state = session.State;

        // En /hunt, /travel y /boss te curás UNA sola vez por pelea, sea con el desplegable del combate (que pasa por acá) o
        // escribiendo /use: los dos comparten el límite (ver GameData/CombatHeal.cs).
        bool healLimited = CombatHeal.IsLimited(state.CommandName);
        if (healLimited && state.HealUsed)
        {
            return new UseResult(Loc.T("🍖 Ya te curaste en esta pelea: solo se puede **una vez** por combate."), null);
        }

        if (state.PlayerCurrentHp >= state.PlayerMaxHp && buff is null)
        {
            return new UseResult(Loc.F($"Ya estás al máximo de HP ({state.PlayerCurrentHp}/{state.PlayerMaxHp}), no hace falta usar **{ItemDisplay.Format(item.Emoji, item.Name)}** ahora."), null);
        }

        // El ítem se descuenta ANTES de resolver el turno: si en el rarísimo caso de que el
        // combate se resuelva por otra vía (botón/timeout) justo en el medio, el consumo ya
        // aplicado no se revierte — mismo tipo de ventana angosta que "RaceLost" en
        // AdventureCombatStarter, documentada a propósito en vez de agregar una compensación.
        if (!await inventoryRepository.TryConsumeAsync(discordId, item.ItemId, 1))
        {
            return new UseResult(Loc.F($"No tenés **{ItemDisplay.Format(item.Emoji, item.Name)}** en tu inventario."), null);
        }

        int healedHp = Math.Min(state.PlayerMaxHp, state.PlayerCurrentHp + item.StatValue);

        // Curarse cede el turno: el monstruo contraataca igual, con los efectos de habilidad que
        // haya activos (Aguante / Sombra) aplicados, y el turno cuenta para el enfriamiento (ver
        // GameData/CombatTurnResolver.ResolveCounterTurn).
        var counter = CombatTurnResolver.ResolveCounterTurn(
            new CombatantProfile(state.PlayerDamage, state.PlayerDefense, state.PlayerClass, state.Passives),
            state.Ability,
            state.MonsterDamage);
        var monsterHitOutcome = counter.Hit;
        int monsterHit = monsterHitOutcome.Damage;
        int playerHpAfter = Math.Max(0, healedHp - monsterHit);
        int turnsElapsed = state.TurnsElapsed + 1;
        int dodgeCount = state.DodgeCount + (monsterHitOutcome.Dodged ? 1 : 0);
        int totalDamageTaken = state.TotalDamageTaken + monsterHit;

        if (playerHpAfter <= 0)
        {
            if (!combatSessions.TryAdvance(discordId, session, null, null))
            {
                return new UseResult(Loc.T("Justo se resolvió tu combate por otra vía, revisá el mensaje."), null);
            }

            var finalState = state with
            {
                PlayerCurrentHp = playerHpAfter,
                TurnsElapsed = turnsElapsed,
                DodgeCount = dodgeCount,
                TotalDamageTaken = totalDamageTaken,
            };

            await userRepository.ApplyCombatHpDeltaAsync(discordId, finalState.ToDbHpDelta(playerHpAfter - state.PlayerStartingHp));
            var penalty = await AdventureModule.ApplyDeathPenaltyAsync(userRepository, discordId);

            await session.ReplyTarget.UpdateAsync(BuildCombatDefeatEmbed(finalState, item, monsterHit, penalty), new ComponentBuilder().Build());

            return new UseResult(null, new EmbedBuilder()
                .WithTitle(Loc.T("☠️ Te curaste, pero no alcanzó"))
                .WithDescription(Loc.F($"Usaste **{ItemDisplay.Format(item.Emoji, item.Name)}**, pero el **{state.MonsterName}** {state.MonsterEmoji} te remató mientras tanto."))
                .WithColor(Color.DarkRed)
                .Build());
        }

        var nextState = state with
        {
            PlayerCurrentHp = playerHpAfter,
            TurnsElapsed = turnsElapsed,
            DodgeCount = dodgeCount,
            TotalDamageTaken = totalDamageTaken,
            Ability = counter.Status,
            // Un banquete en plena pelea: el buff vale YA (se recalcula el ataque desde el que tenía sin buff, así no se apila).
            PlayerDamage = buff is null ? state.PlayerDamage : AttackBuff.Rescale(state.PlayerDamage, state.AttackBuffPercent, buff.AttackPercent),
            AttackBuffPercent = buff?.AttackPercent ?? state.AttackBuffPercent,
            // Gastó la curación de la pelea: el desplegable queda deshabilitado (AdventureModule.BuildCombatButtons).
            HealUsed = state.HealUsed || healLimited,
        };

        if (!combatSessions.TryAdvance(discordId, session, nextState, session.ReplyTarget))
        {
            return new UseResult(Loc.T("Justo se resolvió tu combate por otra vía, revisá el mensaje."), null);
        }

        if (buff is not null)
        {
            await buffRepository.ActivateAttackAsync(discordId, buff.AttackPercent, buff.Minutes, item.Name);
        }

        await session.ReplyTarget.UpdateAsync(BuildCombatOngoingEmbed(nextState, item, monsterHit, monsterHitOutcome.Dodged), AdventureModule.BuildCombatButtons(nextState));

        string resultDescription = monsterHitOutcome.Dodged
            ? Loc.F($"Usaste **{ItemDisplay.Format(item.Emoji, item.Name)}** y recuperaste HP. El **{state.MonsterName}** {state.MonsterEmoji} intentó golpearte, ¡pero esquivaste el ataque! 💨") + BuffLine(buff)
            : Loc.F($"Usaste **{ItemDisplay.Format(item.Emoji, item.Name)}** y recuperaste HP, pero el **{state.MonsterName}** {state.MonsterEmoji} aprovechó para golpearte.") + BuffLine(buff);

        return new UseResult(null, new EmbedBuilder()
            .WithTitle(Loc.T("🍖 ¡Usaste un consumible en combate!"))
            .WithDescription(resultDescription)
            .WithColor(Color.Gold)
            .Build());
    }

    // El título de «ya estás al máximo». La taberna reconoce con él que comer no hizo falta (TabernaModule.ExecuteActionAsync): lo compara con ESTA función y no con un texto, porque cambia con el idioma.
    public static string AlreadyFullTitle() => Loc.T("❤️ Ya estás al máximo");

    // "⚔️ +15% de ataque durante 30 minutos." (con salto de línea adelante), o nada si el ítem no da buff.
    private static string BuffLine(ItemBuff? buff) =>
        buff is null ? string.Empty : Loc.F($"\n⚔️ **+{buff.AttackPercent}% de ataque** durante {buff.Minutes} minutos.");

    private static Embed BuildCombatOngoingEmbed(CombatState state, Item item, int monsterHit, bool monsterDodged)
    {
        string monsterLine = monsterDodged
            ? Loc.F($"El **{state.MonsterName}** {state.MonsterEmoji} intentó golpearte, ¡pero esquivaste el ataque! 💨")
            : Loc.F($"El **{state.MonsterName}** te hizo **{monsterHit}** de daño mientras te curabas.");

        return new EmbedBuilder()
            .WithTitle(Loc.F($"⚔️ Combate contra {state.MonsterName} {state.MonsterEmoji}"))
            .WithColor(Color.Gold)
            .WithMonsterPortrait(state.MonsterPortrait)
            .WithDescription(Loc.F($"Usaste **{ItemDisplay.Format(item.Emoji, item.Name)}**. {monsterLine}"))
            .AddField(Loc.T("❤️ Tu HP"), HpLine(state.PlayerCurrentHp, state.PlayerMaxHp), true)
            .AddField(Loc.F($"{state.MonsterEmoji} HP de {state.MonsterName}"), HpLine(state.MonsterCurrentHp, state.MonsterMaxHp), true)
            .Build();
    }

    // state.PlayerCurrentHp ya viene con el resultado final aplicado — en unidades de combate,
    // posiblemente escaladas por un Guerrero, para no mezclar una cifra real de la base con un
    // Máximo escalado.
    private static Embed BuildCombatDefeatEmbed(CombatState state, Item item, int monsterHit, DeathPenaltyOutcome? penalty = null)
    {
        var embed = new EmbedBuilder()
            .WithTitle(Loc.F($"💀 Derrota contra {state.MonsterName} {state.MonsterEmoji}"))
            .WithColor(Color.DarkRed)
            .WithMonsterPortrait(state.MonsterPortrait)
            .WithDescription(
                Loc.F($"Usaste **{ItemDisplay.Format(item.Emoji, item.Name)}**, pero el **{state.MonsterName}** te hizo **{monsterHit}** de daño y te dejó fuera de combate. Usá **/heal** para recuperarte.") +
                $"\n\n{AdventureModule.MonsterSays(state, NpcDialogue.BossLine.Victory)}")
            .AddField(Loc.T("❤️ Tu HP"), HpLine(state.PlayerCurrentHp, state.PlayerMaxHp), true)
            .AddField(Loc.T("📋 Resumen del combate"), AdventureModule.BuildCombatSummaryLine(state), false);

        return AdventureModule.WithDeathPenalty(embed, penalty).Build();
    }

    private static string HpLine(int current, int max) => $"{ProgressBar.Render(current, max)}\n{current}/{max}";
}
