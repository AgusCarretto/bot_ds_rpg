using BotDsRpg.GameData;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

public class AdventureModule(
    ICooldownRepository cooldownRepository,
    IAdventureRepository adventureRepository,
    IUserRepository userRepository,
    IItemRepository itemRepository,
    ICombatService combatService) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("hunt", "Salí a cazar monstruos cercanos (cooldown de 1 minuto).")]
    public Task HandleHuntAsync() =>
        RunAdventureAsync(CooldownCatalog.Hunt, combatService.SimulateHunt);

    [SlashCommand("travel", "Emprendé un viaje de exploración: más difícil, mejores recompensas (cooldown de 10 minutos).")]
    public Task HandleTravelAsync() =>
        RunAdventureAsync(CooldownCatalog.Travel, combatService.SimulateTravel);

    private async Task RunAdventureAsync(CooldownDefinition definition, Func<int, CombatResult> simulate)
    {
        // La consulta de cooldown + la simulación + la transacción pueden superar los 3s
        // que da Discord antes de que la interacción expire.
        await DeferAsync();

        try
        {
            var remaining = await cooldownRepository.GetRemainingAsync(Context.User.Id, definition.CommandName, definition.Duration);
            if (remaining is not null)
            {
                await FollowupAsync(embed: BuildCooldownEmbed(definition, remaining.Value), ephemeral: true);
                return;
            }

            // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
            var player = await userRepository.GetOrCreateUserAsync(Context.User.Id);

            if (player.CurrentHp <= 0)
            {
                await FollowupAsync(embed: BuildNoHpEmbed(), ephemeral: true);
                return;
            }

            var result = simulate(player.Level);

            Item? droppedItem = result.DroppedRarity is not null
                ? await itemRepository.GetRandomByRarityAsync(result.DroppedRarity)
                : null;

            var outcome = await adventureRepository.ApplyRewardAsync(
                Context.User.Id,
                definition.CommandName,
                definition.Duration,
                result.GoldReward,
                result.XpReward,
                result.HpLost,
                droppedItem?.ItemId,
                droppedItemQuantity: 1);

            if (outcome is null)
            {
                // Perdió la carrera contra otra ejecución concurrente del mismo comando (ej. doble click).
                await FollowupAsync("Justo se te adelantó otra ejecución de este comando, probá de nuevo en un toque.", ephemeral: true);
                return;
            }

            await FollowupAsync(embed: BuildResultEmbed(definition, result, droppedItem, outcome));
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await FollowupAsync("¡Upa! Algo falló procesando tu aventura, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    private static Embed BuildCooldownEmbed(CooldownDefinition definition, TimeSpan remaining)
    {
        return new EmbedBuilder()
            .WithTitle($"⏳ {definition.Emoji} {definition.DisplayName}: todavía no podés")
            .WithDescription($"Te falta **{TimeFormat.Remaining(remaining)}** para volver a intentarlo.")
            .WithColor(Color.DarkGrey)
            .Build();
    }

    private static Embed BuildNoHpEmbed()
    {
        return new EmbedBuilder()
            .WithTitle("💀 Estás sin fuerzas")
            .WithDescription("Te quedaste sin HP. Usá **/heal** para recuperarte antes de volver a intentarlo.")
            .WithColor(Color.DarkRed)
            .Build();
    }

    private static Embed BuildResultEmbed(CooldownDefinition definition, CombatResult result, Item? droppedItem, LevelUpOutcome outcome)
    {
        string title = definition.CommandName == "hunt" ? "🏹 Resultado de la caza" : "🗺️ Resultado del viaje";
        var embed = new EmbedBuilder().WithTitle(title);
        var player = outcome.Player;

        if (!result.Victory)
        {
            embed.WithColor(Color.Red)
                .WithDescription($"{result.MonsterEmoji} Te cruzaste con **{result.MonsterName}** y esta vez no pudiste con él.")
                .AddField("💔 HP perdido", result.HpLost.ToString(), true)
                .AddField("❤️ Vida", $"{player.CurrentHp} / {player.MaxHp}", true);
            return embed.Build();
        }

        embed.WithColor(Color.Green)
            .WithDescription($"{result.MonsterEmoji} ¡Derrotaste a **{result.MonsterName}**!")
            .AddField("💰 Oro ganado", result.GoldReward.ToString(), true)
            .AddField("📊 EXP ganada", result.XpReward.ToString(), true)
            .AddField("❤️ Vida", $"{player.CurrentHp} / {player.MaxHp}" + (result.HpLost > 0 ? $" (-{result.HpLost})" : string.Empty), true);

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
}
