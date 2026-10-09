using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// Lo que muestra la pantalla de recordatorios: el mensaje de texto (si no hay cuenta), el embed y los controles.
public sealed record ReminderView(string? PlainMessage, Embed? Embed, MessageComponent? Components);

// /reminders (aa recordatorios|reminders|avisos): los recordatorios de cooldown (v0.16.0, GameData/Reminders.cs). El bot avisa en el canal donde usaste un comando cuando termina su espera
// (cacería, viaje, talar, minar, jefe/raid, comprar caja, mascotas, diario). Esta pantalla deja elegir cuáles querés recibir: una lista desplegable con TODOS los tipos, donde los marcados
// son los que recibís (lo que desmarcás se apaga y se borra el aviso que estaba pendiente), y dos botones para prender o apagar todo de una. Es efímera: son tus ajustes.
// Los ids de los controles llevan a quién pertenecen (como el resto de las escenas): nadie toca los ajustes con el mensaje de otro.
public class ReminderModule(IUserRepository userRepository, IReminderRepository reminderRepository) : InteractionModuleBase<SocketInteractionContext>
{
    public const string MenuPrefix = "reminders_pick";
    public const string AllPrefix = "reminders_all";

    [SlashCommand("reminders", "Los avisos del bot cuando termina una espera (cacería, viaje, talar, jefe...): elegí cuáles recibir.")]
    public async Task HandleRemindersAsync()
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var view = await ExecuteViewAsync(userRepository, reminderRepository, Context.User.Id);
            await FollowupAsync(view.PlainMessage, embed: view.Embed, components: view.Components, ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude abrir tus recordatorios, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction($"{MenuPrefix}:*")]
    public async Task HandlePickAsync(string ownerRaw, string[] selected)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Esos ajustes son de otra persona: abrí los tuyos con **/reminders**.", ephemeral: true);
                return;
            }

            var view = await ExecuteSetAsync(userRepository, reminderRepository, Context.User.Id, selected);
            await ModifyOriginalResponseAsync(p =>
            {
                p.Content = view.PlainMessage;
                p.Embed = view.Embed;
                p.Components = view.Components ?? new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude guardar eso, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    [ComponentInteraction($"{AllPrefix}:*:*")]
    public async Task HandleAllAsync(string ownerRaw, string mode)
    {
        await DeferAsync();

        try
        {
            if (!ulong.TryParse(ownerRaw, out ulong ownerId) || ownerId != Context.User.Id)
            {
                await FollowupAsync("Esos ajustes son de otra persona: abrí los tuyos con **/reminders**.", ephemeral: true);
                return;
            }

            // "on" = recibir todos (nada apagado); "off" = apagar todos.
            var view = await ExecuteSetAsync(userRepository, reminderRepository, Context.User.Id, mode == "on" ? ReminderCatalog.Keys : []);
            await ModifyOriginalResponseAsync(p =>
            {
                p.Content = view.PlainMessage;
                p.Embed = view.Embed;
                p.Components = view.Components ?? new ComponentBuilder().Build();
            });
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude guardar eso, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // La pantalla de ajustes del jugador. Pública y sin Context para que "aa recordatorios" muestre exactamente lo mismo.
    public static async Task<ReminderView> ExecuteViewAsync(IUserRepository userRepository, IReminderRepository reminderRepository, ulong discordId)
    {
        if (await userRepository.GetByDiscordIdAsync(discordId) is null)
        {
            return NoAccount();
        }

        return Compose(await reminderRepository.GetOffKindsAsync(discordId), discordId);
    }

    // Guarda lo que el jugador quiere RECIBIR (las claves marcadas): todo lo demás queda apagado. Una clave que no existe se ignora (nunca se guarda algo armado a mano).
    public static async Task<ReminderView> ExecuteSetAsync(
        IUserRepository userRepository, IReminderRepository reminderRepository, ulong discordId, IEnumerable<string> receiveKeys)
    {
        if (await userRepository.GetByDiscordIdAsync(discordId) is null)
        {
            return NoAccount();
        }

        var off = OffKindsFor(receiveKeys);
        await reminderRepository.SetOffKindsAsync(discordId, off);
        return Compose(off, discordId);
    }

    // De lo que se quiere recibir a lo que queda apagado (pura, para probarla).
    public static IReadOnlyList<string> OffKindsFor(IEnumerable<string> receiveKeys)
    {
        var receive = receiveKeys.ToHashSet();
        return ReminderCatalog.Keys.Where(k => !receive.Contains(k)).ToList();
    }

    private static ReminderView NoAccount() =>
        new("Todavía no tenés cuenta: empezá con **/start** y después el bot te avisa cuando termine cada espera.", null, null);

    // La pantalla a partir de los tipos apagados (pura, para probarla sin base de datos).
    public static ReminderView Compose(IReadOnlyCollection<string> offKinds, ulong discordId)
    {
        var off = offKinds.ToHashSet();
        int on = ReminderCatalog.All.Count(k => !off.Contains(k.Key));

        var lines = ReminderCatalog.All.Select(k =>
        {
            string wait = ReminderCatalog.WaitOf(k.Key) is { } w ? ReminderCatalog.WaitLabel(w) : "?";
            return off.Contains(k.Key)
                ? $"🔕 {k.Emoji} ~~{k.Name}~~ · cada {wait}"
                : $"✅ {k.Emoji} **{k.Name}** · cada {wait}";
        });

        var embed = new EmbedBuilder()
            .WithTitle("⏰ Tus recordatorios")
            .WithColor(OnboardingModule.BrandColor)
            .WithDescription(
                "Cuando termina una espera, el bot te avisa en el canal donde usaste el comando. " +
                $"Los de cacería, talar y minar (esperas cortas) se borran solos a los {(int)ReminderCatalog.ShortLived.TotalMinutes} minutos.\n\n" +
                string.Join('\n', lines))
            .WithFooter(on == ReminderCatalog.All.Count
                ? "Recibís todos. Desmarcá en la lista los que no querés."
                : $"Recibís {on} de {ReminderCatalog.All.Count}. Marcá en la lista los que querés recibir.");

        var menu = new SelectMenuBuilder()
            .WithCustomId($"{MenuPrefix}:{discordId}")
            .WithPlaceholder("Marcá los que querés recibir")
            .WithMinValues(0)
            .WithMaxValues(ReminderCatalog.All.Count);
        foreach (var kind in ReminderCatalog.All)
        {
            // Sin emojis en las opciones (si Discord rechaza uno, rechaza el mensaje entero).
            menu.AddOption(kind.Name, kind.Key, kind.Command, isDefault: !off.Contains(kind.Key));
        }

        var components = new ComponentBuilder()
            .WithSelectMenu(menu)
            .WithButton("Recibir todos", $"{AllPrefix}:{discordId}:on", ButtonStyle.Success, new Emoji("🔔"), disabled: on == ReminderCatalog.All.Count, row: 1)
            .WithButton("Silenciar todos", $"{AllPrefix}:{discordId}:off", ButtonStyle.Secondary, new Emoji("🔕"), disabled: on == 0, row: 1)
            .Build();

        return new ReminderView(null, embed.Build(), components);
    }
}
