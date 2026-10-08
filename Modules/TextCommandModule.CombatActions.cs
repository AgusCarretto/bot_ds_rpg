using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): jugar el turno del combate ESCRIBIENDO, para cuando un botón se traba.
// Discord le da al bot 3 segundos para contestar un clic; si el bot demora (o Discord no se lo entrega a tiempo) el botón dice "no respondió" y el turno no se juega. Un comando de texto
// no tiene ese límite. Vale para /hunt, /travel, /boss y /raid: juega EXACTAMENTE el mismo turno que el botón (AdventureModule.ResolveTurnCoreAsync / RaidModule.ResolveAttackCoreAsync) y el
// resultado sale como un MENSAJE NUEVO con botones nuevos (el viejo queda sin botones). Si no hay combate en curso, avisa.
public partial class TextCommandModule
{
    // "aa attack" — lo mismo que el botón "Atacar".
    [Command("attack")]
    [Alias("atk", "atacar", "ataque")]
    [Summary("Atacá en tu combate escribiendo (para cuando el botón se traba): \"aa attack\". Sirve en /hunt, /travel, /boss y /raid.")]
    public Task AttackAsync() => CombatActionAsync(fled: false, useAbility: false);

    // "aa ability" — lo mismo que el botón de la habilidad de tu clase.
    [Command("ability")]
    [Alias("skill", "hab", "habilidad")]
    [Summary("Usá la habilidad de tu clase en el combate escribiendo (para cuando el botón se traba): \"aa ability\".")]
    public Task AbilityAsync() => CombatActionAsync(fled: false, useAbility: true);

    // "aa flee" — lo mismo que el botón "Huir".
    [Command("flee")]
    [Alias("huir")]
    [Summary("Huí de tu combate escribiendo (para cuando el botón se traba): \"aa flee\". Huir no da recompensa.")]
    public Task FleeAsync() => CombatActionAsync(fled: true, useAbility: false);

    private async Task CombatActionAsync(bool fled, bool useAbility)
    {
        try
        {
            ulong discordId = Context.User.Id;

            // Combate solitario (hunt, travel, boss).
            if (combatSessions.Peek(discordId) is not null)
            {
                await AdventureModule.ResolveTurnCoreAsync(
                    combatSessions, userRepository, itemRepository, adventureRepository, inventoryRepository, buffRepository, gameEvents, zoneBoxService,
                    discordId, fled, useAbility, new MessageCombatTurnOutput(Context.Channel));
                return;
            }

            // Raid (el lobby o la pelea activa): el id del raid sale del índice jugador -> raid.
            if (raidSessions.RaidIdOf(discordId) is { } raidId)
            {
                var output = new MessageRaidTurnOutput(Context.Channel);
                if (fled)
                {
                    await RaidModule.ResolveFleeCoreAsync(raidSessions, userRepository, raidId, discordId, output);
                }
                else
                {
                    await RaidModule.ResolveAttackCoreAsync(
                        raidSessions, userRepository, itemRepository, adventureRepository, gameEvents, zoneBoxService, raidId, discordId, useAbility, output);
                }

                return;
            }

            await ReplyAsync("No tenés ningún combate activo. Empezá uno con /hunt, /travel, /boss, /raid o \"aa hunt\".");
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! Algo falló procesando el combate, intentá de nuevo en un momento.");
        }
    }
}
