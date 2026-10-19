namespace BotDsRpg.Services;

// Parámetros del jefe en equipo que se leen una sola vez al arrancar (Program.cs). Estático porque toda la
// lógica vive en métodos estáticos (patrón de todo el bot: el mismo código lo usan el
// slash command, el comando de texto y los relojes en background), que no reciben DI.
public static class RaidSettings
{
    public const int DefaultMinParticipants = 2;
    public const int MaxParticipants = 6;

    // Mínimo de jugadores para que el jefe en equipo pueda arrancar. 2 por defecto (con uno solo ya existe
    // /boss); se puede bajar a 1 con la config "Raid:MinParticipants" (variable Raid__MinParticipants)
    // para probar el flujo completo de botones sin necesitar una segunda cuenta.
    public static int MinParticipants { get; private set; } = DefaultMinParticipants;

    public static void Configure(int minParticipants) =>
        MinParticipants = Math.Clamp(minParticipants, 1, MaxParticipants);

    // Cuánto esperan las invitaciones antes de arrancar con quienes ya aceptaron (o cancelar si no llegan al mínimo). Settable solo para las pruebas.
    public static TimeSpan InviteWindow { get; set; } = TimeSpan.FromSeconds(120);

    // Cuánto tiene cada jugador para jugar SU turno (el mismo tiempo que el combate solitario, CombatSessionService.TurnTimeout): el primer turno vencido se pierde, el segundo seguido
    // lo retira del combate. Settable solo para las pruebas.
    public static TimeSpan TurnTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
