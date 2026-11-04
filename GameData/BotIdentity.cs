namespace BotDsRpg.GameData;

// Cómo se ve el propio bot hacia afuera. Hoy una sola cosa: la URL de su foto (el ícono de Asado y Acero), que el pie del perfil usa de ícono junto al puesto del ranking («Top: #1»).
// Estático a propósito, como UiEmojis y NpcImages: los armadores de pantallas son funciones puras que no reciben el cliente de Discord; Program.ReadyAsync lo llena al conectar.
// Sin cargar (recién arrancado, o en una prueba) es null y el pie sale solo con el texto.
public static class BotIdentity
{
    private static volatile string? _avatarUrl;

    public static string? AvatarUrl
    {
        get => _avatarUrl;
        set => _avatarUrl = string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
