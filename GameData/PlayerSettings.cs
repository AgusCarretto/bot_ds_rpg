namespace BotDsRpg.GameData;

// Lo que cada jugador puede configurar (v0.26.1, /config): cómo quiere VER el bot. Nada de esto toca el juego (ni números, ni cooldowns, ni recompensas): solo qué se muestra.
// Sumar una opción: un campo acá con su valor de siempre por defecto, una columna en player_settings (Database/add_player_settings.sql) y su botón en Modules/ConfigModule.cs.
public sealed record PlayerSettings(bool ShowFightSummary = true)
{
    // Lo que ve quien nunca tocó nada (o cuando la base no responde): todo como siempre.
    public static readonly PlayerSettings Default = new();
}

// Las opciones que se pueden pedir escribiendo («aa config resumen off»). Cada una con las palabras con que se la nombra (español e inglés, sin tildes ni mayúsculas).
public enum SettingKey
{
    FightSummary,
}

public static class SettingWords
{
    private static readonly string[] FightSummaryWords = ["summary", "fightsummary", "fight summary", "resumen", "resumen del combate", "resumendelcombate", "tabla", "tablita", "stats"];

    public static SettingKey? KeyOf(string? text)
    {
        string word = (text ?? string.Empty).Trim().ToLowerInvariant();
        return FightSummaryWords.Contains(word) ? SettingKey.FightSummary : null;
    }

    // «on», «off», «sí», «no», «mostrar», «ocultar»... → true / false; «toggle» o «cambiar» → null con toggle = true (el que llama invierte el valor de ahora). null y toggle=false si no se entendió.
    public static (bool? Value, bool Toggle) ParseSwitch(string? text) => (text ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "on" or "si" or "sí" or "yes" or "true" or "1" or "activar" or "activado" or "mostrar" or "show" or "enable" or "enabled" => (true, false),
        "off" or "no" or "false" or "0" or "desactivar" or "desactivado" or "ocultar" or "sacar" or "hide" or "disable" or "disabled" => (false, false),
        "toggle" or "cambiar" or "alternar" or "switch" => (null, true),
        _ => (null, false),
    };
}
