namespace BotDsRpg.Services;

// Parámetros del raid que se leen una sola vez al arrancar (Program.cs). Estático porque toda la
// lógica del raid vive en métodos estáticos (patrón de todo el bot: el mismo código lo usan el
// slash command, el comando de texto y el timeout del lobby en background), que no reciben DI.
public static class RaidSettings
{
    public const int DefaultMinParticipants = 2;
    public const int MaxParticipants = 6;

    // Mínimo de jugadores para que un raid pueda arrancar. 2 por defecto (con uno solo ya existe
    // /boss); se puede bajar a 1 con la config "Raid:MinParticipants" (variable Raid__MinParticipants)
    // para probar el flujo completo de botones sin necesitar una segunda cuenta.
    public static int MinParticipants { get; private set; } = DefaultMinParticipants;

    public static void Configure(int minParticipants) =>
        MinParticipants = Math.Clamp(minParticipants, 1, MaxParticipants);
}
