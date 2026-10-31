using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;

// «aa code» / «/code» (v0.37.0): canjear un código por premios, y «aa admincode»: el panel del administrador para crearlos. Toda la lógica está acá, estática y sin Discord; el comando de barra y el de texto
// solo le pasan lo que escribió el jugador.
public static class CodeHandler
{
    // Lo que hay que contestar: un texto (algo no salió) o la pantalla del premio. Nunca los dos.
    public sealed record CodeReply(string? PlainMessage, Embed? Embed);

    public static async Task<CodeReply> RedeemAsync(
        IRedeemCodeRepository codes, IGameEvents events, ulong discordId, string? typed)
    {
        string? code = RedeemCodeRules.Normalize(typed);
        if (code is null)
        {
            return Plain(Loc.T("Eso no parece un código. Se escribe así: `aa code CODIGO` (o `/code`): letras y números, sin espacios."));
        }

        var result = await codes.RedeemAsync(discordId, code);
        switch (result.Status)
        {
            case RedeemStatus.NoAccount:
                return Plain(Loc.T("Primero creá tu personaje con **/start**."));
            case RedeemStatus.UnknownCode:
                return Plain(Loc.T("Ese código no existe. Fijate que esté bien escrito."));
            case RedeemStatus.Inactive:
                return Plain(Loc.T("Ese código ya no está disponible."));
            case RedeemStatus.Expired:
                return Plain(Loc.T("Ese código venció."));
            case RedeemStatus.Exhausted:
                return Plain(Loc.T("Ese código ya se agotó: lo canjeó todo el cupo de personas."));
            case RedeemStatus.AlreadyRedeemed:
                return Plain(Loc.T("Ya canjeaste ese código."));
            case RedeemStatus.NoRewards:
                return Plain(Loc.T("Ese código no tiene premios cargados: avisale al staff."));
        }

        await events.RecordAsync(discordId, GameEventKinds.CodeRedeem, null, 1, code);
        if (result.LevelsGained > 0)
        {
            // Sale el cartel de «¡SUBISTE DE NIVEL!» como con cualquier otra EXP (GameData/LevelUpCard.cs, por el evento).
            await events.RecordAsync(discordId, GameEventKinds.LevelUp, null, result.LevelsGained, result.NewLevel.ToString());
        }

        return new CodeReply(null, BuildRedeemedEmbed(code, result));
    }

    public static Embed BuildRedeemedEmbed(string code, RedeemResult result)
    {
        var lines = result.Rewards.Select(RewardLine).ToList();
        if (result.LevelsGained > 0)
        {
            lines.Add(Loc.F($"🎉 ¡Subiste de nivel! Ahora sos nivel **{result.NewLevel}**."));
        }

        if (result.Capped)
        {
            lines.Add(Loc.T("Algún total llegó al tope que aguanta la cuenta."));
        }

        var embed = new EmbedBuilder()
            .WithTitle(Loc.T("🎟️ ¡Código canjeado!"))
            .WithColor(Color.Gold)
            .WithDescription(Loc.F($"Canjeaste **{code}** y recibiste:") + "\n\n" + string.Join('\n', lines));
        if (result.Rewards.Any(r => r.Kind == RedeemRewardKind.Item))
        {
            embed.WithFooter(Loc.T("📦 Abrí tus cajas con /open"));
        }

        return embed.Build();
    }

    // Una línea por premio. El Polvo y el oro con su ícono de siempre.
    public static string RewardLine(RedeemedReward reward) => reward.Kind switch
    {
        RedeemRewardKind.Gold => Loc.F($"{UiEmojis.Gold} **{GameHistory.Number(reward.Amount)}** de oro"),
        RedeemRewardKind.Dust => Loc.F($"✨ **{GameHistory.Number(reward.Amount)}** de Polvo"),
        RedeemRewardKind.Xp => Loc.F($"{UiEmojis.Xp} **{GameHistory.Number(reward.Amount)}** de EXP"),
        _ => $"🎁 **{GameHistory.Number(reward.Amount)}×** {ItemDisplay.Format(reward.ItemEmoji, reward.ItemName ?? "?")}",
    };

    private static CodeReply Plain(string text) => new(text, null);

    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
    // El panel del administrador. Misma seguridad que «aa admingive» (IAdminAccess): a quien no es administrador NO se le contesta nada; sin administradores configurados dice qué variable falta.

    public static async Task<string?> AdminAsync(
        IAdminAccess admin, IRedeemCodeRepository codes, IItemRepository items, IAdminRepository adminRepository, IGameEvents events, ulong actorId, string? args)
    {
        if (!admin.IsAdmin(actorId))
        {
            if (admin.HasAnyAdmin)
            {
                BotLog.Info($"[ADMIN] rechazado: {actorId} intentó usar admincode");
                return null;
            }

            return "El comando de administrador está apagado: falta configurar la variable **Admin__UserIds** (los IDs de Discord de los administradores, separados por coma).";
        }

        string text = (args ?? string.Empty).Trim();
        int space = text.IndexOfAny([' ', '\t', '\n']);
        string sub = (space < 0 ? text : text[..space]).ToLowerInvariant();
        string rest = space < 0 ? string.Empty : text[(space + 1)..].Trim();

        switch (sub)
        {
            case "create" or "crear" or "new":
                return await CreateAsync(codes, items, adminRepository, events, actorId, rest);
            case "list" or "lista" or "ls":
                return await ListAsync(codes);
            case "info":
                return await InfoAsync(codes, rest);
            case "off" or "apagar":
                return await SetActiveAsync(codes, events, actorId, rest, false);
            case "on" or "prender":
                return await SetActiveAsync(codes, events, actorId, rest, true);
            case "edit" or "editar":
                return await EditAsync(codes, items, adminRepository, events, actorId, rest);
            case "delete" or "borrar" or "del":
                return await DeleteAsync(codes, events, actorId, rest);
            default:
                return RedeemCodeRules.CreateUsage;
        }
    }

    private static async Task<string> CreateAsync(
        IRedeemCodeRepository codes, IItemRepository items, IAdminRepository adminRepository, IGameEvents events, ulong actorId, string args)
    {
        var parse = RedeemCodeRules.ParseCreate(args);
        if (parse.Spec is not { } spec)
        {
            return parse.Error!;
        }

        var (resolved, error) = await ResolveRewardsAsync(items, adminRepository, spec);
        if (error is not null)
        {
            return error;
        }

        var created = await codes.CreateAsync(spec with { Rewards = resolved }, actorId);
        if (created.Status == CreateCodeStatus.Duplicate)
        {
            return $"Ya existe un código **{spec.Code}**. Elegí otro, o apagá el viejo con `aa admincode off {spec.Code}`.";
        }

        if (created.Status == CreateCodeStatus.ItemMissing)
        {
            return $"No pude crear el código: el ítem **{created.Detail}** no está en el catálogo.";
        }

        string detail = $"creó {spec.Code} ({Limits(spec.MaxUses, spec.Duration)}): {string.Join(", ", resolved.Select(Describe))}";
        BotLog.Info($"[ADMIN] {actorId} {detail}");
        await events.RecordAsync(actorId, GameEventKinds.AdminCode, null, 1, detail);

        return $"🎟️ Código **{spec.Code}** creado ({Limits(spec.MaxUses, spec.Duration)}).\nPremios: {string.Join(" · ", resolved.Select(Describe))}.\n" +
               $"Los jugadores lo canjean con `/code` (privado) o `aa code {spec.Code}`.";
    }

    // «edit CODIGO [personas] [duración] | premios»: misma sintaxis que create, y REEMPLAZA todo (el tope, la duración contada desde ahora y la lista de premios entera). Los que ya
    // lo canjearon no lo pueden canjear de nuevo, y si estaba apagado sigue apagado.
    private static async Task<string> EditAsync(
        IRedeemCodeRepository codes, IItemRepository items, IAdminRepository adminRepository, IGameEvents events, ulong actorId, string args)
    {
        var parse = RedeemCodeRules.ParseCreate(args);
        if (parse.Spec is not { } spec)
        {
            return parse.Error == RedeemCodeRules.CreateUsage ? RedeemCodeRules.EditUsage : parse.Error!;
        }

        var (resolved, error) = await ResolveRewardsAsync(items, adminRepository, spec);
        if (error is not null)
        {
            return error;
        }

        var edited = await codes.EditAsync(spec with { Rewards = resolved });
        if (edited.Status == CreateCodeStatus.NotFound)
        {
            return $"No encontré el código **{spec.Code}**. Mirá los que hay con `aa admincode list`.";
        }

        if (edited.Status == CreateCodeStatus.ItemMissing)
        {
            return $"No pude editar el código: el ítem **{edited.Detail}** no está en el catálogo.";
        }

        string detail = $"editó {spec.Code} ({Limits(spec.MaxUses, spec.Duration)}): {string.Join(", ", resolved.Select(Describe))}";
        BotLog.Info($"[ADMIN] {actorId} {detail}");
        await events.RecordAsync(actorId, GameEventKinds.AdminCode, null, 1, detail);

        var info = await codes.GetAsync(spec.Code);
        return $"✏️ Código **{spec.Code}** editado ({Limits(spec.MaxUses, spec.Duration)}).\nPremios: {string.Join(" · ", resolved.Select(Describe))}.\n" +
               (info is null ? string.Empty : $"Estado: {StateLine(info)}. Los que ya lo canjearon no lo pueden canjear de nuevo.");
    }

    private static async Task<string> DeleteAsync(IRedeemCodeRepository codes, IGameEvents events, ulong actorId, string rest)
    {
        string? code = RedeemCodeRules.Normalize(rest);
        var result = code is null ? new DeleteCodeResult(DeleteCodeStatus.NotFound, 0) : await codes.DeleteAsync(code);
        switch (result.Status)
        {
            case DeleteCodeStatus.NotFound:
                return "No encontré ese código. Mirá los que hay con `aa admincode list`.";
            case DeleteCodeStatus.AlreadyUsed:
                return $"**{code}** ya se canjeó {result.Uses} {(result.Uses == 1 ? "vez" : "veces")}: no lo borro, porque se perdería quién lo canjeó. Apagalo con `aa admincode off {code}`.";
        }

        string detail = $"borró {code}";
        BotLog.Info($"[ADMIN] {actorId} {detail}");
        await events.RecordAsync(actorId, GameEventKinds.AdminCode, null, 1, detail);
        return $"🗑️ El código **{code}** se borró. El nombre quedó libre para crearlo de nuevo.";
    }

    // Los ítems se resuelven con el nombre del catálogo (sin importar mayúsculas ni acentos) y solo valen los de la mochila: un código no da equipo ni herramientas.
    // Error = el mensaje para el administrador si algún premio no sirve (y entonces no se crea ni se edita nada).
    private static async Task<(List<RedeemReward> Rewards, string? Error)> ResolveRewardsAsync(IItemRepository items, IAdminRepository adminRepository, RedeemCodeSpec spec)
    {
        var resolved = new List<RedeemReward>();
        foreach (var reward in spec.Rewards)
        {
            if (reward.Kind != RedeemRewardKind.Item)
            {
                resolved.Add(reward);
                continue;
            }

            var item = await items.GetByNameAsync(reward.ItemName!);
            if (item is null)
            {
                var near = await adminRepository.SearchItemNamesAsync(reward.ItemName!, 8);
                return (resolved, near.Count == 0
                    ? $"No encontré ningún ítem llamado **{reward.ItemName}**."
                    : $"No encontré **{reward.ItemName}**. ¿Quisiste decir: {string.Join(", ", near.Select(n => $"**{n}**"))}?");
            }

            if (item.Type is "Weapon" or "Amulet" || ToolRules.KindOfItemType(item.Type) is not null)
            {
                return (resolved, $"**{item.Name}** es equipo o una herramienta: los códigos solo dan ítems de la mochila (cajas, comida, materiales, huevos...).");
            }

            resolved.Add(reward with { ItemName = item.Name });
        }

        return (resolved, null);
    }

    private static async Task<string> ListAsync(IRedeemCodeRepository codes)
    {
        var all = await codes.ListAsync(15);
        if (all.Count == 0)
        {
            return "Todavía no hay códigos. Crealos con `aa admincode create`.";
        }

        return "🎟️ **Códigos** (los 15 más nuevos):\n" + string.Join('\n', all.Select(c => $"• **{c.Code}** — {StateLine(c)}"));
    }

    private static async Task<string> InfoAsync(IRedeemCodeRepository codes, string rest)
    {
        string? code = RedeemCodeRules.Normalize(rest);
        var info = code is null ? null : await codes.GetAsync(code);
        if (info is null)
        {
            return "No encontré ese código. Mirá los que hay con `aa admincode list`.";
        }

        return $"🎟️ **{info.Code}** — {StateLine(info)}\nPremios: {string.Join(" · ", info.Rewards.Select(r => Describe(new RedeemReward(r.Kind, r.Amount, r.ItemName))))}\nCreado por <@{info.CreatedBy}>.";
    }

    private static async Task<string> SetActiveAsync(IRedeemCodeRepository codes, IGameEvents events, ulong actorId, string rest, bool active)
    {
        string? code = RedeemCodeRules.Normalize(rest);
        if (code is null || !await codes.SetActiveAsync(code, active))
        {
            return "No encontré ese código. Mirá los que hay con `aa admincode list`.";
        }

        string detail = $"{(active ? "prendió" : "apagó")} {code}";
        BotLog.Info($"[ADMIN] {actorId} {detail}");
        await events.RecordAsync(actorId, GameEventKinds.AdminCode, null, 1, detail);
        return active ? $"✅ El código **{code}** está activo." : $"⏸️ El código **{code}** quedó apagado: nadie lo puede canjear hasta que lo prendas.";
    }

    private static string Describe(RedeemReward reward) => reward.Kind switch
    {
        RedeemRewardKind.Gold => $"{GameHistory.Number(reward.Amount)} de oro",
        RedeemRewardKind.Dust => $"{GameHistory.Number(reward.Amount)} de Polvo",
        RedeemRewardKind.Xp => $"{GameHistory.Number(reward.Amount)} de EXP",
        _ => $"{GameHistory.Number(reward.Amount)}× {reward.ItemName}",
    };

    private static string Limits(int? maxUses, TimeSpan? duration) =>
        $"{(maxUses is int n ? $"hasta {GameHistory.Number(n)} personas" : "sin tope de personas")}, {(duration is { } d ? $"dura {TimeFormat.Remaining(d)}" : "no vence")}";

    private static string StateLine(RedeemCodeInfo c)
    {
        string uses = c.MaxUses is int max ? $"{GameHistory.Number(c.Uses)}/{GameHistory.Number(max)} canjes" : $"{GameHistory.Number(c.Uses)} canjes";
        string expiry = c.ExpiresAt is { } at
            ? (at <= DateTime.UtcNow ? "venció" : $"vence en {TimeFormat.Remaining(at - DateTime.UtcNow)}")
            : "no vence";
        return $"{uses} · {expiry} · {(c.Active ? "activo" : "apagado")}";
    }
}
