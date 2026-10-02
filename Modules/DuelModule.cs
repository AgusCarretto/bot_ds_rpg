using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// Resultado de desafiar a alguien: un rechazo simple (PlainMessage) o el desafío con sus botones (Content = la mención del desafiado).
public sealed record DuelChallengeResult(string? PlainMessage, string? Content, Embed? Embed, MessageComponent? Components);

// /fight @jugador (aa fight @jugador): duelo AMISTOSO. Uno desafía, el otro acepta con un botón y pelean por turnos con el mismo motor del
// resto del combate (GameData/DuelEngine.cs): atacar, usar la habilidad de la clase o rendirse. Es solo por el honor: los dos arrancan con la
// vida completa, no se toca la base (ni vida, ni oro, ni XP, ni cooldowns) y lo único que queda es el registro de quién ganó. Cada turno tiene
// 60 segundos; el que no juega a tiempo pierde. Estado en memoria (Services/DuelService.cs).
public class DuelModule(IUserRepository userRepository, IDuelService duels, IDuelFighterFactory fighters) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("fight", "Desafiá a otro jugador a un duelo amistoso (no se gana ni se pierde nada: es por el honor).")]
    public async Task HandleFightAsync([Summary("player", "A quién querés desafiar.")] IUser player)
    {
        await DeferAsync();

        try
        {
            var result = await ExecuteChallengeAsync(userRepository, duels, Context.User, player);
            await FollowupAsync(result.PlainMessage ?? result.Content, embed: result.Embed, components: result.Components, ephemeral: result.Embed is null);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude armar el desafío, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction("duel_accept:*")]
    public async Task HandleAcceptAsync(string idRaw)
    {
        await DeferAsync();

        try
        {
            if (!Guid.TryParse(idRaw, out var id) || duels.PeekChallenge(id) is not { } challenge)
            {
                await FollowupAsync("Ese desafío ya venció o ya se resolvió.", ephemeral: true);
                return;
            }

            if (Context.User.Id != challenge.TargetId)
            {
                await FollowupAsync("Este desafío no es para vos.", ephemeral: true);
                return;
            }

            // Sacarlo de forma atómica: si dos clicks llegan a la vez, solo uno arranca el duelo.
            if (duels.TryTakeChallenge(id) is not { } taken)
            {
                await FollowupAsync("Ese desafío ya venció o ya se resolvió.", ephemeral: true);
                return;
            }

            var a = await fighters.BuildAsync(taken.ChallengerId, taken.ChallengerName);
            var b = await fighters.BuildAsync(taken.TargetId, taken.TargetName);
            if (a is null || b is null)
            {
                await FollowupAsync("Alguno de los dos ya no tiene cuenta: el duelo no se puede armar.", ephemeral: true);
                return;
            }

            var session = duels.TryStart(a, b, new InteractionCombatReplyTarget(Context.Interaction), Render);
            if (session is null)
            {
                await FollowupAsync("Alguno de los dos ya está en otro duelo: terminenlo y vuelvan a desafiarse.", ephemeral: true);
                return;
            }

            var (embed, components) = Render(session);
            await ModifyOriginalResponseAsync(p =>
            {
                p.Content = string.Empty;
                p.Embed = embed;
                p.Components = components;
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude arrancar el duelo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Rechazar (el desafiado) o cancelar (quien desafió).
    [ComponentInteraction("duel_decline:*")]
    public async Task HandleDeclineAsync(string idRaw)
    {
        await DeferAsync();

        try
        {
            if (!Guid.TryParse(idRaw, out var id) || duels.PeekChallenge(id) is not { } challenge)
            {
                await FollowupAsync("Ese desafío ya venció o ya se resolvió.", ephemeral: true);
                return;
            }

            if (Context.User.Id != challenge.TargetId && Context.User.Id != challenge.ChallengerId)
            {
                await FollowupAsync("Este desafío no es tuyo.", ephemeral: true);
                return;
            }

            if (duels.TryTakeChallenge(id) is null)
            {
                await FollowupAsync("Ese desafío ya venció o ya se resolvió.", ephemeral: true);
                return;
            }

            bool byTarget = Context.User.Id == challenge.TargetId;
            var embed = new EmbedBuilder()
                .WithTitle("❌ Duelo cancelado")
                .WithColor(Color.DarkGrey)
                .WithDescription(byTarget
                    ? $"**{challenge.TargetName}** rechazó el duelo de **{challenge.ChallengerName}**. Será para otra."
                    : $"**{challenge.ChallengerName}** retiró el desafío a **{challenge.TargetName}**.")
                .Build();

            await ModifyOriginalResponseAsync(p =>
            {
                p.Content = string.Empty;
                p.Embed = embed;
                p.Components = new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude cancelar el desafío, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction("duel_attack:*")]
    public Task HandleAttackAsync(string idRaw) => ActAsync(idRaw, PlayerAction.Attack);

    [ComponentInteraction("duel_ability:*")]
    public Task HandleAbilityAsync(string idRaw) => ActAsync(idRaw, PlayerAction.Ability);

    private async Task ActAsync(string idRaw, PlayerAction action)
    {
        await DeferAsync();

        try
        {
            if (!Guid.TryParse(idRaw, out var id))
            {
                await FollowupAsync("Ese duelo ya terminó.", ephemeral: true);
                return;
            }

            var result = duels.TryAct(id, Context.User.Id, action, new InteractionCombatReplyTarget(Context.Interaction));
            if (await RejectAsync(result) || result.Session is not { } session)
            {
                return;
            }

            var (embed, components) = Render(session);
            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = embed;
                p.Components = components;
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! Algo falló en el duelo, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction("duel_forfeit:*")]
    public async Task HandleForfeitAsync(string idRaw)
    {
        await DeferAsync();

        try
        {
            if (!Guid.TryParse(idRaw, out var id))
            {
                await FollowupAsync("Ese duelo ya terminó.", ephemeral: true);
                return;
            }

            var result = duels.TryForfeit(id, Context.User.Id, new InteractionCombatReplyTarget(Context.Interaction));
            if (await RejectAsync(result) || result.Session is not { } session)
            {
                return;
            }

            var (embed, components) = Render(session);
            await ModifyOriginalResponseAsync(p =>
            {
                p.Embed = embed;
                p.Components = components;
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude registrar la rendición, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Los motivos por los que un click no hace nada, con un aviso que ve solo quien clickeó. True si se rechazó.
    private async Task<bool> RejectAsync(DuelActResult result)
    {
        string? message = result.Status switch
        {
            DuelActStatus.NotFound or DuelActStatus.AlreadyOver => "Ese duelo ya terminó.",
            DuelActStatus.NotParticipant => "Ese duelo no es tuyo.",
            DuelActStatus.NotYourTurn => "Todavía no es tu turno.",
            DuelActStatus.AbilityUnavailable => "Tu habilidad todavía está en enfriamiento.",
            _ => null,
        };

        if (message is null)
        {
            return false;
        }

        await FollowupAsync(message, ephemeral: true);
        return true;
    }

    // ---- Lógica compartida con "aa fight" (sin Context) ----

    public static async Task<DuelChallengeResult> ExecuteChallengeAsync(
        IUserRepository userRepository, IDuelService duels, IUser challenger, IUser target)
    {
        if (target.IsBot)
        {
            return new DuelChallengeResult("Los bots no pelean: elegí a otra persona.", null, null, null);
        }

        if (target.Id == challenger.Id)
        {
            return new DuelChallengeResult("No podés desafiarte a vos mismo, ¡buscate un rival!", null, null, null);
        }

        var challengerUser = await userRepository.GetByDiscordIdAsync(challenger.Id);
        if (challengerUser is null)
        {
            return new DuelChallengeResult("Primero tenés que empezar tu aventura con **/start**.", null, null, null);
        }

        string targetName = GameModule.GetDisplayName(target);
        var targetUser = await userRepository.GetByDiscordIdAsync(target.Id);
        if (targetUser is null)
        {
            return new DuelChallengeResult($"**{targetName}** todavía no empezó su aventura (le falta **/start**).", null, null, null);
        }

        if (duels.IsInDuel(challenger.Id))
        {
            return new DuelChallengeResult("Ya estás en un duelo: terminalo antes de armar otro.", null, null, null);
        }

        if (duels.IsInDuel(target.Id))
        {
            return new DuelChallengeResult($"**{targetName}** ya está en pleno duelo. Esperá a que termine.", null, null, null);
        }

        string challengerName = GameModule.GetDisplayName(challenger);
        var challenge = duels.CreateChallenge(challenger.Id, challengerName, target.Id, targetName);

        var embed = new EmbedBuilder()
            .WithTitle("⚔️ ¡Duelo amistoso!")
            .WithColor(Color.Orange)
            .WithDescription(
                $"{MentionUtils.MentionUser(challenger.Id)} desafía a {MentionUtils.MentionUser(target.Id)}.\n\n" +
                "Es **amistoso**: no se pierde ni se gana nada, es solo por el honor. Los dos arrancan con la vida completa " +
                "(en PvP la vida se ajusta un poco por clase para que sea parejo).")
            .AddField($"🥊 {challengerName}", FighterLine(challengerUser.Class, challengerUser.Level), true)
            .AddField($"🥊 {targetName}", FighterLine(targetUser.Class, targetUser.Level), true)
            .WithFooter($"{targetName}: aceptá cuando quieras (el desafío vence en {(int)DuelService.ChallengeLifetime.TotalMinutes} minutos).")
            .Build();

        var buttons = new ComponentBuilder()
            .WithButton("Aceptar", $"duel_accept:{challenge.Id}", ButtonStyle.Success, new Emoji("⚔️"))
            .WithButton("Rechazar", $"duel_decline:{challenge.Id}", ButtonStyle.Secondary, new Emoji("✖️"))
            .Build();

        return new DuelChallengeResult(null, MentionUtils.MentionUser(target.Id), embed, buttons);
    }

    private static string FighterLine(string playerClass, int level)
    {
        string emoji = ClassCatalog.All.FirstOrDefault(c => c.Name == playerClass)?.Emoji ?? "🥊";
        return $"{emoji} {playerClass} · nivel {level}";
    }

    // El mensaje del duelo en su estado actual: en curso (con los botones del que tiene el turno) o terminado (resumen y sin botones).
    // Pública para probarla; toma el lock de la sesión (reentrante) para leer un estado consistente.
    public static (Embed Embed, MessageComponent Components) Render(DuelSession session)
    {
        lock (session.Lock)
        {
            var state = session.State;
            string log = session.Log.Count == 0 ? string.Empty : string.Join("\n", session.Log);

            var embed = new EmbedBuilder()
                .WithTitle($"⚔️ {state.A.Fighter.Name} vs {state.B.Fighter.Name}")
                .AddField(SideTitle(state.A), SideBody(state.A), true)
                .AddField(SideTitle(state.B), SideBody(state.B), true);

            if (session.End == DuelEnd.None)
            {
                var actor = state.Actor;
                embed.WithColor(Color.Orange)
                    .WithDescription($"{log}{(log.Length > 0 ? "\n\n" : string.Empty)}▶️ Le toca a {MentionUtils.MentionUser(actor.Fighter.DiscordId)}.")
                    .WithFooter($"Duelo amistoso · cada turno tiene {(int)DuelService.TurnTimeout.TotalSeconds} s · no se pierde nada");

                return (embed.Build(), ActiveButtons(session.Id, actor));
            }

            var winner = session.Winner!;
            string how = session.End switch
            {
                DuelEnd.Knockout => "Se quedó sin vida.",
                DuelEnd.Forfeit => "Se rindió.",
                DuelEnd.TimedOut => "No jugó a tiempo.",
                _ => "Se acabó el tiempo de combate: gana el que tiene más vida.",
            };

            embed.WithTitle($"🏆 ¡{winner.Fighter.Name} ganó el duelo!")
                .WithColor(Color.Green)
                .WithDescription($"{log}\n\n{how}")
                .AddField("📋 Resumen", Summary(state), false)
                .WithFooter("Duelo amistoso: no se ganó ni se perdió nada. ¡Revancha cuando quieran!");

            return (embed.Build(), new ComponentBuilder().Build());
        }
    }

    private static string SideTitle(DuelSide side) => $"{(ClassCatalog.All.FirstOrDefault(c => c.Name == side.Fighter.PlayerClass)?.Emoji ?? "🥊")} {side.Fighter.Name}";

    private static string SideBody(DuelSide side)
    {
        string ability = AdventureModule.BuildAbilityStatusLine(side.Fighter.PlayerClass, side.Status);
        return $"{ProgressBar.Render(side.Hp, side.Fighter.MaxHp)}\n{Math.Max(0, side.Hp)}/{side.Fighter.MaxHp}{(ability.Length > 0 ? $"\n{ability}" : string.Empty)}";
    }

    private static string Summary(DuelState state) =>
        $"**{state.A.Fighter.Name}**: {state.A.DamageDealt} de daño · {state.A.Crits} crít. · {state.A.Dodges} esquives\n" +
        $"**{state.B.Fighter.Name}**: {state.B.DamageDealt} de daño · {state.B.Crits} crít. · {state.B.Dodges} esquives\n" +
        $"Duró {state.Actions} acciones.";

    // Los botones son los del que tiene el turno: el de habilidad lleva SU habilidad (y sus turnos de enfriamiento). Solo él puede apretarlos
    // (el servicio lo valida), pero la rendición es de los dos.
    private static MessageComponent ActiveButtons(Guid id, DuelSide actor)
    {
        var builder = new ComponentBuilder()
            .WithButton("Atacar", $"duel_attack:{id}", ButtonStyle.Primary, new Emoji("⚔️"));

        var ability = ClassAbilities.For(actor.Fighter.PlayerClass);
        if (ability is not null)
        {
            int cooldown = actor.Status.CooldownRemaining;
            builder.WithButton(
                cooldown > 0 ? $"{ability.Name} ({cooldown})" : ability.Name, $"duel_ability:{id}", ButtonStyle.Success,
                new Emoji(ability.Emoji), disabled: cooldown > 0);
        }

        return builder.WithButton("Rendirse", $"duel_forfeit:{id}", ButtonStyle.Danger, new Emoji("🏳️")).Build();
    }
}
