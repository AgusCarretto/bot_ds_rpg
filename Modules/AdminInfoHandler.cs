using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;

// «aa admininfo» (v0.51.2): el comando del ADMINISTRADOR para ver cuánta gente juega y en cuántos servidores está el bot (el dueño, 2026-11-07: «cuántas personas hicieron /start… en cuántos servidores está»).
// Hermano de «aa admingive» y «aa adminset»: misma seguridad (solo los IDs de Admin__UserIds; a cualquier otro no le contesta nada) y solo LEE, no cambia nada.
public static class AdminInfoHandler
{
    // Los servidores los pasa quien llama (el cliente de Discord), así esto se prueba sin Discord. Devuelve null si no hay que contestar nada (no es administrador).
    public static async Task<string?> ExecuteAsync(IAdminAccess admin, IAdminRepository adminRepository, ulong actorId, int guildCount, long memberCount)
    {
        if (!admin.IsAdmin(actorId))
        {
            if (admin.HasAnyAdmin)
            {
                BotLog.Info($"[ADMIN] rechazado: {actorId} intentó usar admininfo");
                return null;
            }

            return "El comando de administrador está apagado: falta configurar la variable **Admin__UserIds** (los IDs de Discord de los administradores, separados por coma).";
        }

        var stats = await adminRepository.GetBotStatsAsync();
        return Compose(stats, guildCount, memberCount);
    }

    public static string Compose(AdminBotStats stats, int guildCount, long memberCount) =>
        $"📊 **Asado y Acero RPG v{BotVersion.Current}**\n" +
        $"👥 Cuentas (hicieron /start): **{GameHistory.Number(stats.Players)}**\n" +
        $"🔥 Activos: **{GameHistory.Number(stats.Active24h)}** en 24 h · **{GameHistory.Number(stats.Active7d)}** en 7 días\n" +
        $"♻️ Con al menos un Fuego Nuevo: **{GameHistory.Number(stats.WithFuegoNuevo)}**\n" +
        $"🏰 Servidores: **{GameHistory.Number(guildCount)}** · miembros en total: **{GameHistory.Number(memberCount)}**";
}
