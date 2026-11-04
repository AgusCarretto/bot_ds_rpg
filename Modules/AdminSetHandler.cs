using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;

// «aa adminset» (v0.48.1): el comando del ADMINISTRADOR para FIJAR el tier o el nivel de entrenamiento del dragón de un jugador (arreglar un dragón trabado, probar la cría, un premio). Hermano de «aa admingive»: misma seguridad,
// misma forma (lógica estática y sin Discord; el comando de texto solo le pasa lo que escribió el administrador y manda la respuesta).
//
// Seguridad, en este orden:
//  1. Solo los IDs de la variable Admin__UserIds (IAdminAccess). A cualquier otro NO le contesta nada (devuelve null); sin ningún administrador configurado explica qué variable falta.
//  2. Nunca crea cuentas ni dragones: quien no tiene cuenta o no tiene dragón no recibe nada.
//  3. Todo queda registrado: una línea [ADMIN] en el log y un evento admin_set en game_events con el antes y el después y quién lo hizo.
public static class AdminSetHandler
{
    // Devuelve lo que hay que contestar, o null si no hay que contestar nada (no es administrador).
    public static async Task<string?> ExecuteAsync(
        IAdminAccess admin,
        IUserRepository users,
        IAdminRepository adminRepository,
        IGameEvents events,
        ulong actorId,
        string? args)
    {
        if (!admin.IsAdmin(actorId))
        {
            if (admin.HasAnyAdmin)
            {
                BotLog.Info($"[ADMIN] rechazado: {actorId} intentó usar adminset");
                return null;
            }

            return "El comando de administrador está apagado: falta configurar la variable **Admin__UserIds** (los IDs de Discord de los administradores, separados por coma).";
        }

        var parse = AdminSet.Parse(args);
        if (parse.Request is not { } request)
        {
            return parse.Error;
        }

        ulong targetId = request.TargetId ?? actorId;
        if (await users.GetByDiscordIdAsync(targetId) is null)
        {
            return $"<@{targetId}> todavía no tiene cuenta: que use **/start** primero (este comando no crea cuentas).";
        }

        var result = request.Kind == AdminSetKind.DragonTier
            ? await adminRepository.SetDragonTierAsync(targetId, request.Value)
            : await adminRepository.SetDragonLevelAsync(targetId, request.Value);

        string field = request.Kind == AdminSetKind.DragonTier ? "tier" : "level";
        switch (result.Status)
        {
            case AdminDragonSetStatus.NoDragon:
                return $"<@{targetId}> no tiene dragón: se compra con **/shop buy dragon** (este comando no lo crea).";

            case AdminDragonSetStatus.AboveCap:
                return $"El dragón de <@{targetId}> es de tier **{result.Tier}** y su tope de nivel es **{result.LevelCap}**: no puedo ponerle nivel {request.Value}. " +
                       $"Subile el tier primero con `aa adminset dragon tier N`. Sigue en nivel **{result.Level}**.";
        }

        string reply;
        string detail;
        if (request.Kind == AdminSetKind.DragonTier)
        {
            reply = $"🐲 El dragón de <@{targetId}> pasó de tier **{result.BeforeTier}** a **{result.Tier}** ({DragonRules.TierName(result.Tier)}). " +
                    (result.LevelClamped
                        ? $"Su nivel de entrenamiento se recortó de {result.BeforeLevel} a **{result.Level}** (el tope de este tier). "
                        : $"Su nivel de entrenamiento sigue en **{result.Level}** (tope {result.LevelCap}). ") +
                    "Las rachas de fallos de la cría quedaron en 0.";
            detail = $"dragon tier {result.BeforeTier}->{result.Tier}" + (result.LevelClamped ? $" (nivel {result.BeforeLevel}->{result.Level})" : string.Empty);
        }
        else
        {
            reply = $"🐲 El dragón de <@{targetId}> (tier **{result.Tier}**) pasó del nivel de entrenamiento **{result.BeforeLevel}** al **{result.Level}** (tope {result.LevelCap}).";
            detail = $"dragon level {result.BeforeLevel}->{result.Level}";
        }

        BotLog.Info($"[ADMIN] {actorId} -> {targetId}: set {detail}");
        await events.RecordAsync(targetId, GameEventKinds.AdminSet, null, request.Value, $"{detail} | por {actorId}");
        return reply;
    }
}
