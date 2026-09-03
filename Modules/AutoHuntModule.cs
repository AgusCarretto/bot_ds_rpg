using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// Completamente separado de /hunt (AdventureModule): sin botones, resuelve todo de una sola vez.
// Comparte el mismo cooldown que /hunt a propósito (misma entrada en la tabla cooldowns, mismo
// CooldownCatalog.Hunt), así que usar uno bloquea al otro hasta que se cumpla el tiempo.
public class AutoHuntModule(
    IAdventureRepository adventureRepository,
    IUserRepository userRepository,
    IItemRepository itemRepository,
    IAdventureCombatStarter combatStarter) : InteractionModuleBase<SocketInteractionContext>
{
    private const int MaxRounds = 100; // tope defensivo; el HP del monstruo baja en cada golpe, así que en la práctica nunca se llega acá

    // Comando barra: /autohunt
    [SlashCommand("autohunt", "Resuelve una cacería completa de una sola vez, sin botones (comparte cooldown con /hunt).")]
    public async Task HandleAutoHuntAsync()
    {
        await DeferAsync();

        try
        {
            var result = await ExecuteAsync(adventureRepository, userRepository, itemRepository, combatStarter, Context.User.Id);

            if (result.PlainMessage is not null)
            {
                await FollowupAsync(result.PlainMessage, ephemeral: true);
            }
            else
            {
                await FollowupAsync(embed: result.Embed);
            }
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! Algo falló en la auto-cacería, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Exactamente uno de los dos campos del resultado viene con valor.
    public sealed record AutoHuntResult(string? PlainMessage, Embed? Embed);

    // Estático (sin dependencia de Context) para que Modules/TextCommandModule.cs comparta
    // exactamente la misma lógica en "aa autohunt"/"aa ah".
    public static async Task<AutoHuntResult> ExecuteAsync(
        IAdventureRepository adventureRepository,
        IUserRepository userRepository,
        IItemRepository itemRepository,
        IAdventureCombatStarter combatStarter,
        ulong discordId)
    {
        // Misma preparación que /hunt: mismo cooldown (CooldownCatalog.Hunt → "hunt" en la tabla
        // cooldowns), mismo chequeo de "ya en combate" y de HP, misma resolución de
        // arma/sinergia/defensa/monstruo. Reclama el cooldown acá adentro.
        var outcome = await combatStarter.PrepareAsync(discordId, CooldownCatalog.Hunt, MonsterCatalog.HuntMonsters);

        switch (outcome.Status)
        {
            case CombatStartStatus.AlreadyInCombat:
                return new AutoHuntResult("Ya estás en medio de un combate por turnos. Terminalo (atacando o huyendo) antes de auto-cazar.", null);
            case CombatStartStatus.OnCooldown:
                return new AutoHuntResult(null, AdventureModule.BuildCooldownEmbed(CooldownCatalog.Hunt, outcome.CooldownRemaining!.Value));
            case CombatStartStatus.NoHp:
                return new AutoHuntResult(null, AdventureModule.BuildNoHpEmbed());
            case CombatStartStatus.RaceLost:
                return new AutoHuntResult("Justo se te adelantó otra ejecución de este comando, probá de nuevo en un toque.", null);
        }

        var state = outcome.State!;

        // Resolución instantánea: misma matemática de golpe que un turno de /hunt (CombatMath),
        // encadenada sin esperar clicks de botón. Se acumulan los mismos contadores que /hunt por
        // turnos para poder mostrar el mismo resumen final (AdventureModule.BuildCombatSummaryLine).
        int playerHp = state.PlayerCurrentHp;
        int monsterHp = state.MonsterCurrentHp;
        int roundsElapsed = 0;
        int dodgeCount = 0;
        int critCount = 0;
        int totalDamageDealt = 0;
        int totalDamageTaken = 0;
        int totalHealed = 0;

        for (int round = 0; round < MaxRounds && playerHp > 0 && monsterHp > 0; round++)
        {
            roundsElapsed++;

            var playerHitOutcome = CombatMath.ResolvePlayerHit(state.PlayerDamage, state.Passives.CritChanceBonus);
            if (playerHitOutcome.Critical)
            {
                critCount++;
            }

            totalDamageDealt += playerHitOutcome.Damage;
            monsterHp = Math.Max(0, monsterHp - playerHitOutcome.Damage);

            // Sifón de Almas (Hechicero): igual que en /hunt por turnos, procede al atacar (killing
            // blow incluido) antes de que el monstruo, si sigue vivo, tenga la chance de contraatacar.
            int lifestealHeal = CombatMath.RollLifesteal(playerHitOutcome.Damage, state.Passives.LifestealChance, state.Passives.LifestealRatio);
            totalHealed += lifestealHeal;
            playerHp = Math.Min(state.PlayerMaxHp, playerHp + lifestealHeal);

            if (monsterHp <= 0)
            {
                break;
            }

            var monsterHitOutcome = CombatMath.ResolveMonsterHit(
                state.MonsterDamage, state.PlayerDefense, state.Passives.DodgeChanceBonus, state.Passives.DamageTakenMultiplier);
            if (monsterHitOutcome.Dodged)
            {
                dodgeCount++;
            }
            else
            {
                totalDamageTaken += monsterHitOutcome.Damage;
            }

            playerHp = Math.Max(0, playerHp - monsterHitOutcome.Damage);
        }

        var finalState = state with
        {
            PlayerCurrentHp = playerHp,
            TurnsElapsed = roundsElapsed,
            DodgeCount = dodgeCount,
            CritCount = critCount,
            TotalDamageDealt = totalDamageDealt,
            TotalDamageTaken = totalDamageTaken,
            TotalHealed = totalHealed,
        };

        if (monsterHp <= 0)
        {
            var reward = CombatRewardCalculator.RollHuntReward(state.PlayerLevel);
            Item? droppedItem = await AdventureModule.ResolveDroppedItemAsync(itemRepository, state, reward);

            // Delta (no snapshot absoluto): igual que en /hunt por turnos, así compone bien con
            // cualquier cambio de HP concurrente en la brevísima ventana que dura esta resolución.
            var levelOutcome = await adventureRepository.ApplyVictoryAsync(
                discordId, reward.Gold, reward.Xp, finalState.ToDbHpDelta(playerHp - state.PlayerStartingHp), droppedItem?.ItemId, droppedItemQuantity: 1);

            return new AutoHuntResult(null, BuildVictoryEmbed(finalState, levelOutcome, reward, droppedItem));
        }

        // Derrota: el jugador llegó a 0 HP antes de bajar al monstruo.
        await userRepository.ApplyCombatHpDeltaAsync(discordId, finalState.ToDbHpDelta(playerHp - state.PlayerStartingHp));
        return new AutoHuntResult(null, BuildDefeatEmbed(finalState));
    }

    private static Embed BuildVictoryEmbed(CombatState state, LevelUpOutcome outcome, CombatReward reward, Item? droppedItem)
    {
        var player = outcome.Player;

        var embed = new EmbedBuilder()
            .WithTitle("⚔️ Auto-Cacería Exitosa")
            .WithColor(Color.Green)
            .WithDescription(
                $"Venciste al **{state.MonsterName}** {state.MonsterEmoji}. Te quedan **{player.CurrentHp}/{player.MaxHp}** HP.\n" +
                $"Ganaste: **{reward.Gold}** Oro, **{reward.Xp}** XP.")
            .AddField("📋 Resumen del combate", AdventureModule.BuildCombatSummaryLine(state), false);

        if (droppedItem is not null)
        {
            embed.AddField("🎁 Material obtenido", $"{droppedItem.Name} ({droppedItem.Rarity})", false);
        }

        if (outcome.LevelsGained > 0)
        {
            embed.AddField("🎉 ¡Subiste de nivel!", $"Ahora sos nivel **{player.Level}** (vida máxima: {player.MaxHp}).", false);
        }

        return embed.Build();
    }

    // state.PlayerCurrentHp ya viene con el resultado final (ver ExecuteAsync) — en unidades de
    // combate, posiblemente escaladas por un Guerrero, para no mezclar una cifra real de la base
    // con un Máximo escalado.
    private static Embed BuildDefeatEmbed(CombatState state)
    {
        return new EmbedBuilder()
            .WithTitle("☠️ Derrota Rápida")
            .WithColor(Color.DarkRed)
            .WithDescription($"El **{state.MonsterName}** {state.MonsterEmoji} fue demasiado fuerte. Quedaste a **{state.PlayerCurrentHp}** HP. Usá **/heal** para recuperarte.")
            .AddField("📋 Resumen del combate", AdventureModule.BuildCombatSummaryLine(state), false)
            .Build();
    }
}
