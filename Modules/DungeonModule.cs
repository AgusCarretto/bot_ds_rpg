using BotDsRpg.GameData;
using BotDsRpg.GameData.Dungeons;
using BotDsRpg.Models;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// Las mazmorras de las Zonas 6 a 10 (v0.47.0, GameData/Dungeons): se entra con /boss (o «aa boss») estando en una de esas zonas y con su Reliquia; se juega con los botones (o «aa dg <acción>»
// si un botón no responde). Ganar la de una zona intermedia abre la siguiente; ganar la de la zona tope te manda directo contra el Asador (un mensaje nuevo con la pelea), sin curarte.
// Perder (o quedarte quieto 3 minutos) gasta la Reliquia. La lógica está en Services/DungeonService.cs; acá solo se arma lo que se ve.
public class DungeonModule(
    IDungeonService dungeons, ICombatSessionService combatSessions, IZoneRepository zoneRepository, IInventoryRepository inventoryRepository, IBuffRepository buffRepository)
    : InteractionModuleBase<SocketInteractionContext>
{
    public const string ButtonPrefix = "dg";

    // dg:{dueño}:{corrida}:{acción}. Solo el dueño juega; un botón de una corrida vieja no hace nada.
    [ComponentInteraction("dg:*:*:*")]
    public async Task HandleButtonAsync(string owner, string token, string action)
    {
        if (!ulong.TryParse(owner, out ulong ownerId) || ownerId != Context.User.Id)
        {
            await RespondAsync(Loc.T("Esta mazmorra es de otra persona: la tuya se abre con **/boss** en una zona de mazmorra."), ephemeral: true);
            return;
        }

        await DeferAsync();
        try
        {
            var result = await dungeons.ActAsync(ownerId, token, action);
            if (result.Session is null)
            {
                await FollowupAsync(Loc.T("Esa mazmorra ya terminó (o se cortó con un reinicio del bot)."), ephemeral: true);
                return;
            }

            if (result.Step == DungeonStep.Ignored && result.End == DungeonEnd.None)
            {
                return;
            }

            var (embed, components) = await RenderAsync(zoneRepository, result);
            await ModifyOriginalResponseAsync(p => { p.Embed = embed; p.Components = components; });
            if (result.Asador is { } fight)
            {
                await StartAsadorAsync(combatSessions, inventoryRepository, buffRepository, ownerId, fight, async (e, c) => await FollowupAsync(embed: e, components: c));
            }
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync(Loc.T("¡Upa! Algo falló en la mazmorra, probá de nuevo en un momento."), ephemeral: true);
        }
    }

    // ---- Lógica compartida con «aa boss» y «aa dg» (sin Context) ----

    // ¿Está parado en una zona de mazmorra? (así /boss sabe si arranca una mazmorra o la pelea de siempre.)
    public static async Task<bool> IsInDungeonZoneAsync(IUserRepository users, IZoneRepository zones, ulong discordId)
    {
        var player = await users.GetByDiscordIdAsync(discordId);
        if (player is null || player.InGate)
        {
            return false;
        }

        var zone = ZoneRanking.ForPlayer(await zones.GetAllAsync(), player.FuegoNuevo).FirstOrDefault(z => z.ZoneId == player.CurrentZoneId);
        return ZoneRanking.IsDungeonZone(zone);
    }

    // Entrar. Devuelve el texto de un rechazo, o el mensaje de la mazmorra con sus botones. onTimeout: qué hacer con el mensaje si vence por tiempo.
    public static async Task<(string? Message, Embed? Embed, MessageComponent? Components, DungeonSession? Session)> ExecuteStartAsync(IDungeonService dungeons, ulong discordId, ulong channelId)
    {
        var start = await dungeons.StartAsync(discordId, channelId);
        string? problem = start.Status switch
        {
            DungeonStartStatus.Started => null,
            DungeonStartStatus.NoAccount => Loc.T("Primero tenés que empezar tu aventura con **/start**."),
            DungeonStartStatus.NotDungeonZone => Loc.T("Acá no hay ninguna mazmorra: las tienen las zonas que abre el Fuego Nuevo."),
            DungeonStartStatus.AlreadyBusy => Loc.T("Ya estás en una pelea o en una mazmorra: terminala antes de entrar a otra."),
            DungeonStartStatus.OnCooldown => Loc.F($"⏳ La mazmorra usa el cooldown del jefe y todavía te faltan **{TimeFormat.Remaining(start.Wait ?? TimeSpan.Zero)}**."),
            DungeonStartStatus.NoHp => Loc.T("Sin vida no entrás a ninguna mazmorra: curate primero con **/heal**. Adentro no se come."),
            DungeonStartStatus.NotLeveled => Loc.F($"🚫 Para pasar esta mazmorra (y abrir la zona siguiente) necesitás nivel **{start.RequiredLevel}**."),
            DungeonStartStatus.NoRelic => start.Relic is { } relic
                ? Loc.F($"🔒 La puerta de la mazmorra pide la **{ItemDisplay.Format(relic.Emoji, relic.Name)}**. Forjala en la herrería (**/forge**): pide los drops de la zona, muchos materiales y un **Cofre de Escoria** (sale del **/travel** de estas zonas).")
                : Loc.T("Esta zona todavía no tiene su Reliquia cargada."),
            _ => Loc.T("Justo se te adelantó otra acción, probá de nuevo en un toque."),
        };

        if (problem is not null)
        {
            return (problem, null, null, null);
        }

        var session = start.Session!;
        return (null, BuildRunEmbed(session, null), BuildButtons(session), session);
    }

    // La acción escrita («aa dg agua»): misma lógica que el botón.
    public static async Task<(string? Message, DungeonActResult? Result)> ExecuteTypedAsync(IDungeonService dungeons, ulong discordId, string? word)
    {
        var session = dungeons.Peek(discordId);
        if (session is null)
        {
            return (Loc.T("No estás en ninguna mazmorra. Se entra con **/boss** en una zona de mazmorra (con su Reliquia)."), null);
        }

        if (DungeonActions.Parse(session.Puzzle.Kind, word) is not { } action)
        {
            return (Loc.F($"No entendí «{word}». En esta mazmorra: {TypedHelp(session.Puzzle.Kind)}"), null);
        }

        var result = await dungeons.ActAsync(discordId, null, action);
        return result.Step == DungeonStep.Ignored && result.End == DungeonEnd.None
            ? (Loc.T("Eso no se puede ahora (una pared, un botón apagado o algo que no corresponde)."), result)
            : (null, result);
    }

    public static string TypedHelp(DungeonKind kind) => kind switch
    {
        DungeonKind.FirePattern => Loc.T("`aa dg listo`, `aa dg agua`, `aa dg piedra`, `aa dg fuego`."),
        DungeonKind.SmokeMaze => Loc.T("`aa dg arriba`, `aa dg abajo`, `aa dg izquierda`, `aa dg derecha`."),
        DungeonKind.Embers => Loc.T("`aa dg b3` (la columna A-D y la fila 1-4 de la brasa)."),
        DungeonKind.Lock => Loc.T("`aa dg fuego|agua|piedra|hoja|rayo`, `aa dg borrar`, `aa dg probar`."),
        _ => Loc.T("`aa dg llamarada`, `aa dg hachazo`, `aa dg rafaga`."),
    };

    // El mensaje según cómo quedó: la corrida en curso o el final (ganada, perdida o vencida por tiempo).
    public static async Task<(Embed Embed, MessageComponent Components)> RenderAsync(IZoneRepository zones, DungeonActResult result)
    {
        var session = result.Session!;
        if (result.End == DungeonEnd.None)
        {
            return (BuildRunEmbed(session, result), BuildButtons(session));
        }

        Zone? next = null;
        if (result.End == DungeonEnd.Won && !session.IsTop)
        {
            var all = ZoneRanking.OrderByDifficulty(await zones.GetAllAsync());
            int index = all.ToList().FindIndex(z => z.ZoneId == session.ZoneId);
            next = index >= 0 && index + 1 < all.Count ? all[index + 1] : null;
        }

        return (BuildEndEmbed(result, next), new ComponentBuilder().Build());
    }

    public static Embed BuildRunEmbed(DungeonSession session, DungeonActResult? last)
    {
        var puzzle = session.Puzzle;
        string note = puzzle.Note is { } n ? $"\n\n{n}" : string.Empty;
        string burn = last is { Damage: > 0 } ? Loc.F($" (−{last.Damage})") : string.Empty;
        return new EmbedBuilder()
            .WithTitle(Loc.F($"🏛️ {DungeonRules.Name(puzzle.Kind)} · {session.ZoneName}"))
            .WithColor(Color.DarkPurple)
            .WithDescription(puzzle.Render() + note)
            .AddField(Loc.T("❤️ Tu HP") + burn, HpLine(session), false)
            // Footer corto (auditoría de UI, M12): el reloj y lo que está en juego. El resto de las reglas vive en /info.
            .WithFooter(Loc.T("⏱️ 3 min por jugada · sin vida perdés la Reliquia"))
            .Build();
    }

    // La vida de la mazmorra: campo de ancho completo, así que va la barra de emoji de 8 piezas del perfil (o la de texto si no están subidas),
    // con el mismo nombre de campo que en las peleas («Tu HP»).
    private static string HpLine(DungeonSession session) =>
        $"{ProgressBar.Render(session.Hp, session.MaxHp, 8, UiEmojis.BarHp)}\n{GameHistory.Number(session.Hp)}/{GameHistory.Number(session.MaxHp)}";

    public static MessageComponent BuildButtons(DungeonSession session)
    {
        var builder = new ComponentBuilder();
        foreach (var b in session.Puzzle.Buttons())
        {
            builder.WithButton(
                string.IsNullOrEmpty(b.Label) ? null : b.Label,
                $"{ButtonPrefix}:{session.OwnerId}:{session.Token}:{b.Action}",
                b.Primary ? ButtonStyle.Primary : ButtonStyle.Secondary,
                b.Emoji is null ? null : new Emoji(b.Emoji),
                disabled: b.Disabled,
                row: b.Row);
        }

        return builder.Build();
    }

    public static Embed BuildEndEmbed(DungeonActResult result, Zone? nextZone)
    {
        var session = result.Session!;
        var finish = result.Finish;
        string name = DungeonRules.Name(session.Puzzle.Kind);
        string relic = ItemDisplay.Format(session.Relic.Emoji, session.Relic.Name);
        string hp = HpLine(session);
        string note = session.Puzzle.Note is { } n ? $"{n}\n\n" : string.Empty;

        if (result.End == DungeonEnd.Won && finish is { Won: true })
        {
            var embed = new EmbedBuilder().WithColor(Color.Green).AddField(Loc.T("❤️ Tu HP"), hp, false);
            if (session.IsTop)
            {
                return embed.WithTitle(Loc.F($"🔥 ¡Saliste de {name}!"))
                    .WithDescription(note + Loc.F($"La {relic} se consumió abriendo la última puerta. Del otro lado no hay zona: hay brasas. **El Asador Eterno** te espera, y no hay tiempo para comer."))
                    .Build();
            }

            string reward = Loc.F($"Ganaste: {UiEmojis.Gold} **{GameHistory.Number(result.Gold)}** Oro, {UiEmojis.Xp} **{GameHistory.Number(finish.Xp)}** XP.")
                + (result.BoxName is { } box ? Loc.F($" Y una **{ItemEmojiRegistry.Label(box, "📦")}** (se abre con **/open**).") : string.Empty);
            string opened = nextZone is null
                ? string.Empty
                : Loc.F($"\n\nSe abrió **{nextZone.Name}** {nextZone.Emoji}: viajá con **/zona {nextZone.ZoneId}**.");
            return embed.WithTitle(Loc.F($"🏆 ¡Pasaste {name}!"))
                .WithDescription(note + Loc.F($"La {relic} se consumió abriendo la puerta. {reward}") + opened)
                .Build();
        }

        string why = result.End == DungeonEnd.TimedOut
            ? Loc.T("Te quedaste quieto demasiado: la mazmorra se cerró con vos afuera.")
            : finish is { RelicSpent: false }
                ? Loc.T("Al salir, tu Reliquia ya no estaba: sin ella la puerta no se abre.")
                : session.Hp <= 0 ? Loc.T("Te quedaste sin vida adentro.") : Loc.T("El acertijo te ganó.");
        return new EmbedBuilder()
            .WithTitle(Loc.F($"💀 {name} te ganó"))
            .WithColor(Color.DarkRed)
            .WithDescription(note + why + "\n\n" + Loc.F($"Perdiste la {relic}: hay que forjarla de nuevo para volver a entrar. La vida que te sacó la mazmorra no vuelve sola (**/heal**)."))
            .AddField(Loc.T("❤️ Tu HP"), hp, false)
            .Build();
    }

    // La pelea contra el Asador al salir de la mazmorra de la zona tope: un mensaje nuevo con la pelea (send), y la sesión de combate apunta a ese mensaje.
    public static async Task StartAsadorAsync(
        ICombatSessionService combatSessions, IInventoryRepository inventoryRepository, IBuffRepository buffRepository, ulong discordId, CombatState state,
        Func<Embed, MessageComponent, Task<IUserMessage>> send)
    {
        var message = await send(
            AdventureModule.BuildEncounterEmbed(state),
            AdventureModule.BuildCombatButtons(state, await AdventureModule.LoadHealOptionsAsync(inventoryRepository, buffRepository, discordId, state)));
        if (!combatSessions.TryStart(discordId, state, new MessageCombatReplyTarget(message)))
        {
            await message.ModifyAsync(p => { p.Content = Loc.T("Ya tenías otra pelea en curso: esta no arrancó."); p.Components = new ComponentBuilder().Build(); });
        }
    }
}
