using BotDsRpg.GameData;
using BotDsRpg.Services;
using Discord.Commands;

// Segunda parte de TextCommandModule (ver el comentario en TextCommandModule.cs): comandos de
// combate. Sin [Group]/constructor propio — misma clase de C#, las dependencias inyectadas en
// TextCommandModule.cs quedan accesibles acá también.
public partial class TextCommandModule
{
    // "aa hunt" / "aa h" — misma lógica que AdventureModule.HandleHuntAsync.
    [Command("hunt")]
    [Alias("h")]
    [Summary("Salí a cazar monstruos cercanos (cooldown de 1 minuto).")]
    public Task HuntAsync() => StartCombatAsync(CooldownCatalog.Hunt, MonsterCatalog.HuntMonsters);

    // "aa travel" / "aa t" — misma lógica que AdventureModule.HandleTravelAsync.
    [Command("travel")]
    [Alias("t")]
    [Summary("Emprendé un viaje de exploración: más difícil, mejores recompensas (cooldown de 10 minutos).")]
    public Task TravelAsync() => StartCombatAsync(CooldownCatalog.Travel, MonsterCatalog.TravelMonsters);

    // "aa autohunt" / "aa ah" — misma lógica que AutoHuntModule.HandleAutoHuntAsync. Sin botones:
    // resuelve toda la pelea de una y comparte el cooldown de /hunt (no de "aa hunt" en particular,
    // literalmente la misma entrada de la tabla cooldowns).
    [Command("autohunt")]
    [Alias("ah")]
    [Summary("Resuelve una cacería completa de una sola vez, sin botones (comparte cooldown con /hunt).")]
    public async Task AutoHuntAsync()
    {
        try
        {
            var result = await AutoHuntModule.ExecuteAsync(adventureRepository, userRepository, itemRepository, combatStarter, Context.User.Id);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception)
        {
            await ReplyAsync("¡Upa! Algo falló en la auto-cacería, intentá de nuevo en un momento.");
        }
    }

    private async Task StartCombatAsync(CooldownDefinition definition, IReadOnlyList<MonsterTemplate> monsterPool)
    {
        try
        {
            var outcome = await combatStarter.PrepareAsync(Context.User.Id, definition, monsterPool);

            switch (outcome.Status)
            {
                case CombatStartStatus.AlreadyInCombat:
                    await ReplyAsync(AdventureModule.BuildAlreadyInCombatMessage());
                    return;
                case CombatStartStatus.OnCooldown:
                    await ReplyAsync(embed: AdventureModule.BuildCooldownEmbed(definition, outcome.CooldownRemaining!.Value));
                    return;
                case CombatStartStatus.NoHp:
                    await ReplyAsync(embed: AdventureModule.BuildNoHpEmbed());
                    return;
                case CombatStartStatus.RaceLost:
                    await ReplyAsync("Justo se te adelantó otra ejecución de este comando, probá de nuevo en un toque.");
                    return;
            }

            var state = outcome.State!;

            // A diferencia del slash command, acá el mensaje tiene que existir ANTES de poder
            // armar el "reply target" (necesita el IUserMessage ya enviado para poder editarlo
            // después) — los botones que dispare esta pelea funcionan igual que los de /hunt,
            // porque un click de botón siempre llega como interacción sin importar el origen.
            var message = await ReplyAsync(embed: AdventureModule.BuildEncounterEmbed(state), components: AdventureModule.BuildCombatButtons());

            if (!combatSessions.TryStart(Context.User.Id, state, new MessageCombatReplyTarget(message)))
            {
                await ReplyAsync(AdventureModule.BuildAlreadyInCombatMessage());
            }
        }
        catch (Exception)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot.
            await ReplyAsync("¡Upa! Algo falló iniciando tu aventura, intentá de nuevo en un momento.");
        }
    }

    // Nota: no hay "aa attack"/"aa flee" — una vez que la pelea arrancó (por /hunt o "aa hunt"),
    // se sigue exclusivamente con los botones "Atacar"/"Huir" del mensaje (o "aa use" para curarte
    // con un consumible sin perder el turno de otra forma), no hay forma de atacar por texto.
}
