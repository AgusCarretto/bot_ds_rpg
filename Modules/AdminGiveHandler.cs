using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;

// «aa admingive» (v0.34.0): el comando del ADMINISTRADOR para dar oro, Polvo, EXP o cualquier ítem del catálogo sin tenerlos (probar cosas, sorteos y regalos de cajas u oro). Distinto de /give a propósito: /give mueve oro
// ENTRE jugadores y sale de lo que uno tiene; esto crea de la nada, así que va separado y blindado. Toda la lógica está acá, estática y sin Discord (el comando de texto solo le pasa lo que escribió el administrador y manda la respuesta).
//
// Seguridad, en este orden:
//  1. Solo los IDs de la variable Admin__UserIds (IAdminAccess). A cualquier otro NO le contesta nada (devuelve null): ni siquiera confirma que el comando existe. La única excepción es que NO haya ningún administrador configurado: ahí explica
//     qué variable falta (no hay nada que robar: sin admins no se puede dar nada).
//  2. Nunca crea cuentas: quien no tiene cuenta no recibe nada.
//  3. Todo queda registrado: una línea [ADMIN] en el log del servidor y un evento admin_give en game_events (quién dio qué a quién), que sirve para auditar sorteos.
public static class AdminGiveHandler
{
    // Devuelve lo que hay que contestar, o null si no hay que contestar nada (no es administrador).
    public static async Task<string?> ExecuteAsync(
        IAdminAccess admin,
        IUserRepository users,
        IItemRepository items,
        IProgressionRepository progression,
        IAdminRepository adminRepository,
        IGameEvents events,
        ulong actorId,
        string? args)
    {
        if (!admin.IsAdmin(actorId))
        {
            if (admin.HasAnyAdmin)
            {
                BotLog.Info($"[ADMIN] rechazado: {actorId} intentó usar admingive");
                return null;
            }

            return "El comando de administrador está apagado: falta configurar la variable **Admin__UserIds** (los IDs de Discord de los administradores, separados por coma).";
        }

        var parse = AdminGive.Parse(args);
        if (parse.Request is not { } request)
        {
            return parse.Error;
        }

        ulong targetId = request.TargetId ?? actorId;
        if (await users.GetByDiscordIdAsync(targetId) is null)
        {
            return $"<@{targetId}> todavía no tiene cuenta: que use **/start** primero (este comando no crea cuentas).";
        }

        string reply;
        string detail;
        switch (request.Kind)
        {
            case AdminGiveKind.Gold:
            {
                var result = await adminRepository.GiveGoldAsync(targetId, request.Amount);
                reply = $"💰 Le diste **{GameHistory.Number(request.Amount)}** de oro a <@{targetId}>: ahora tiene **{GameHistory.Number(result.NewTotal)}**.{CappedNote(result)}";
                detail = $"gold {request.Amount}";
                break;
            }

            case AdminGiveKind.Dust:
            {
                var result = await adminRepository.GiveDustAsync(targetId, request.Amount);
                reply = $"✨ Le diste **{GameHistory.Number(request.Amount)}** de Polvo a <@{targetId}>: ahora tiene **{GameHistory.Number(result.NewTotal)}**.{CappedNote(result)}";
                detail = $"dust {request.Amount}";
                break;
            }

            case AdminGiveKind.Xp:
            {
                var outcome = await progression.AddXpAsync(targetId, request.Amount);
                reply = $"📊 Le diste **{GameHistory.Number(request.Amount)}** de EXP a <@{targetId}>: ahora es **nivel {outcome.Player.Level}**" +
                        (outcome.LevelsGained > 0 ? $" (subió {outcome.LevelsGained})." : ".");
                detail = $"xp {request.Amount}";
                break;
            }

            default:
            {
                string name = request.ItemName!;
                var item = await items.GetByNameAsync(name);
                if (item is null)
                {
                    var near = await adminRepository.SearchItemNamesAsync(name, 8);
                    return near.Count == 0
                        ? $"No encontré ningún ítem llamado **{name}**."
                        : $"No encontré **{name}**. ¿Quisiste decir: {string.Join(", ", near.Select(n => $"**{n}**"))}?";
                }

                var result = await adminRepository.GiveItemAsync(targetId, item, request.Amount);
                string label = ItemDisplay.Format(item.Emoji, item.Name);
                reply = result.Slot switch
                {
                    "arma" => $"⚔️ Le puso **{label}** de arma a <@{targetId}> (pisa la que llevaba y arranca sin encantamiento).",
                    "amuleto" => $"🛡️ Le puso **{label}** de amuleto a <@{targetId}> (pisa el que llevaba y arranca sin encantamiento).",
                    "herramienta" => $"🪓 Le puso **{label}** de herramienta a <@{targetId}> (pisa la del mismo tipo).",
                    _ => $"🎁 Le diste **{GameHistory.Number(request.Amount)}× {label}** a <@{targetId}>: ahora tiene **{GameHistory.Number(result.NewTotal)}**.{CappedNote(result)}",
                };
                detail = $"item {item.Name} x{request.Amount}";
                break;
            }
        }

        BotLog.Info($"[ADMIN] {actorId} -> {targetId}: {detail}");
        await events.RecordAsync(targetId, GameEventKinds.AdminGive, null, request.Amount, $"{detail} | por {actorId}");
        return reply;
    }

    private static string CappedNote(AdminGiveResult result) => result.Capped ? " (llegó al tope que aguanta la cuenta)" : string.Empty;
}
