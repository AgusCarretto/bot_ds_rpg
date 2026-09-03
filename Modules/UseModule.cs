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
    ICombatSessionService combatSessions) : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /use
    [SlashCommand("use", "Usá un consumible de tu inventario para curar HP (funciona incluso en combate, sin gastar oro).")]
    public async Task HandleUseAsync([Summary("item", "Nombre del consumible que querés usar.")] string itemName)
    {
        await DeferAsync();

        try
        {
            var result = await ExecuteUseAsync(userRepository, itemRepository, inventoryRepository, combatSessions, Context.User.Id, itemName);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: result.Embed is null);
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! No pude usar ese ítem, intentá de nuevo en un momento.", ephemeral: true);
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
        ulong discordId,
        string itemName)
    {
        var item = await itemRepository.GetByNameAsync(itemName);
        if (item is null)
        {
            return new UseResult($"No encontré ningún ítem llamado **{itemName}**.", null);
        }

        if (item.Type != "Consumable")
        {
            return new UseResult($"**{item.Name}** es de tipo `{item.Type}` y no se puede usar (solo Consumibles).", null);
        }

        var session = combatSessions.Peek(discordId);

        // --- Fuera de combate: se comporta como un /heal gratuito, directo contra la base. ---
        if (session is null)
        {
            var player = await userRepository.GetOrCreateUserAsync(discordId);
            if (player.CurrentHp >= player.MaxHp)
            {
                return new UseResult(null, new EmbedBuilder()
                    .WithTitle("❤️ Ya estás al máximo")
                    .WithDescription($"Tenés {player.CurrentHp}/{player.MaxHp} HP, no necesitás usar **{item.Name}** todavía.")
                    .WithColor(Color.Green)
                    .Build());
            }

            if (!await inventoryRepository.TryConsumeAsync(discordId, item.ItemId, 1))
            {
                return new UseResult($"No tenés **{item.Name}** en tu inventario.", null);
            }

            var healed = await userRepository.RestoreHpAsync(discordId, item.StatValue);

            return new UseResult(null, new EmbedBuilder()
                .WithTitle("🍖 ¡Usaste un consumible!")
                .WithDescription($"Usaste **{item.Name}** y recuperaste HP. Ahora tenés **{healed.CurrentHp}/{healed.MaxHp}** HP.")
                .WithColor(Color.Green)
                .Build());
        }

        // --- En combate: cura gratis, pero le cede el turno al monstruo — igual que atacar,
        // usar un ítem también implica el riesgo de recibir un golpe (si no, sería curación
        // ilimitada sin ningún costo mientras dure el inventario). ---
        var state = session.State;

        if (state.PlayerCurrentHp >= state.PlayerMaxHp)
        {
            return new UseResult($"Ya estás al máximo de HP ({state.PlayerCurrentHp}/{state.PlayerMaxHp}), no hace falta usar **{item.Name}** ahora.", null);
        }

        // El ítem se descuenta ANTES de resolver el turno: si en el rarísimo caso de que el
        // combate se resuelva por otra vía (botón/timeout) justo en el medio, el consumo ya
        // aplicado no se revierte — mismo tipo de ventana angosta que "RaceLost" en
        // AdventureCombatStarter, documentada a propósito en vez de agregar una compensación.
        if (!await inventoryRepository.TryConsumeAsync(discordId, item.ItemId, 1))
        {
            return new UseResult($"No tenés **{item.Name}** en tu inventario.", null);
        }

        int healedHp = Math.Min(state.PlayerMaxHp, state.PlayerCurrentHp + item.StatValue);
        var monsterHitOutcome = CombatMath.ResolveMonsterHit(
            state.MonsterDamage, state.PlayerDefense, state.Passives.DodgeChanceBonus, state.Passives.DamageTakenMultiplier);
        int monsterHit = monsterHitOutcome.Damage;
        int playerHpAfter = Math.Max(0, healedHp - monsterHit);
        int turnsElapsed = state.TurnsElapsed + 1;
        int dodgeCount = state.DodgeCount + (monsterHitOutcome.Dodged ? 1 : 0);
        int totalDamageTaken = state.TotalDamageTaken + monsterHit;

        if (playerHpAfter <= 0)
        {
            if (!combatSessions.TryAdvance(discordId, session, null, null))
            {
                return new UseResult("Justo se resolvió tu combate por otra vía, revisá el mensaje.", null);
            }

            var finalState = state with
            {
                PlayerCurrentHp = playerHpAfter,
                TurnsElapsed = turnsElapsed,
                DodgeCount = dodgeCount,
                TotalDamageTaken = totalDamageTaken,
            };

            await userRepository.ApplyCombatHpDeltaAsync(discordId, finalState.ToDbHpDelta(playerHpAfter - state.PlayerStartingHp));

            await session.ReplyTarget.UpdateAsync(BuildCombatDefeatEmbed(finalState, item, monsterHit), new ComponentBuilder().Build());

            return new UseResult(null, new EmbedBuilder()
                .WithTitle("☠️ Te curaste, pero no alcanzó")
                .WithDescription($"Usaste **{item.Name}**, pero el **{state.MonsterName}** {state.MonsterEmoji} te remató mientras tanto.")
                .WithColor(Color.DarkRed)
                .Build());
        }

        var nextState = state with
        {
            PlayerCurrentHp = playerHpAfter,
            TurnsElapsed = turnsElapsed,
            DodgeCount = dodgeCount,
            TotalDamageTaken = totalDamageTaken,
        };

        if (!combatSessions.TryAdvance(discordId, session, nextState, session.ReplyTarget))
        {
            return new UseResult("Justo se resolvió tu combate por otra vía, revisá el mensaje.", null);
        }

        await session.ReplyTarget.UpdateAsync(BuildCombatOngoingEmbed(nextState, item, monsterHit, monsterHitOutcome.Dodged), AdventureModule.BuildCombatButtons());

        string resultDescription = monsterHitOutcome.Dodged
            ? $"Usaste **{item.Name}** y recuperaste HP. El **{state.MonsterName}** {state.MonsterEmoji} intentó golpearte, ¡pero esquivaste el ataque! 💨"
            : $"Usaste **{item.Name}** y recuperaste HP, pero el **{state.MonsterName}** {state.MonsterEmoji} aprovechó para golpearte.";

        return new UseResult(null, new EmbedBuilder()
            .WithTitle("🍖 ¡Usaste un consumible en combate!")
            .WithDescription(resultDescription)
            .WithColor(Color.Gold)
            .Build());
    }

    private static Embed BuildCombatOngoingEmbed(CombatState state, Item item, int monsterHit, bool monsterDodged)
    {
        string monsterLine = monsterDodged
            ? $"El **{state.MonsterName}** {state.MonsterEmoji} intentó golpearte, ¡pero esquivaste el ataque! 💨"
            : $"El **{state.MonsterName}** te hizo **{monsterHit}** de daño mientras te curabas.";

        return new EmbedBuilder()
            .WithTitle($"⚔️ Combate contra {state.MonsterName} {state.MonsterEmoji}")
            .WithColor(Color.Gold)
            .WithDescription($"Usaste **{item.Name}**. {monsterLine}")
            .AddField("❤️ Tu HP", HpLine(state.PlayerCurrentHp, state.PlayerMaxHp), true)
            .AddField($"{state.MonsterEmoji} HP de {state.MonsterName}", HpLine(state.MonsterCurrentHp, state.MonsterMaxHp), true)
            .Build();
    }

    // state.PlayerCurrentHp ya viene con el resultado final aplicado — en unidades de combate,
    // posiblemente escaladas por un Guerrero, para no mezclar una cifra real de la base con un
    // Máximo escalado.
    private static Embed BuildCombatDefeatEmbed(CombatState state, Item item, int monsterHit)
    {
        return new EmbedBuilder()
            .WithTitle($"💀 Derrota contra {state.MonsterName} {state.MonsterEmoji}")
            .WithColor(Color.DarkRed)
            .WithDescription(
                $"Usaste **{item.Name}**, pero el **{state.MonsterName}** te hizo **{monsterHit}** de daño " +
                "y te dejó fuera de combate. Usá **/heal** para recuperarte.")
            .AddField("❤️ Tu HP", HpLine(state.PlayerCurrentHp, state.PlayerMaxHp), true)
            .AddField("📋 Resumen del combate", AdventureModule.BuildCombatSummaryLine(state), false)
            .Build();
    }

    private static string HpLine(int current, int max) => $"{ProgressBar.Render(current, max)}\n{current}/{max}";
}
