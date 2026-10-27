namespace BotDsRpg.GameData;

// Las HERRAMIENTAS (v0.29.0, /tools, aa herramientas|equipamiento; las arma el Carpintero, Modules/ToolsModule.cs). Hay una hacha (para /chop) y un pico (para /mine); sin ninguna se recolecta ×1, lo de siempre.
// Cada una tiene dos tiers que se desbloquean con la zona: el de la Zona 3 (×2) y el de la Zona 5 (×3). Los datos viven en la base (items.type 'Hacha' / 'Pico', items.stat_value = el multiplicador, la receta en
// recipes con la zona que la desbloquea, Database/add_tools.sql) y lo que tiene cada jugador en player_tools; acá solo están las reglas puras.
public static class ToolRules
{
    public const string AxeKind = "axe";
    public const string PickaxeKind = "pickaxe";
    public const string AxeType = "Hacha";
    public const string PickaxeType = "Pico";

    // El tipo de herramienta que es un ítem según su items.type (null si no es una herramienta).
    public static string? KindOfItemType(string? itemType) => itemType switch
    {
        AxeType => AxeKind,
        PickaxeType => PickaxeKind,
        _ => null,
    };

    public static bool IsToolType(string? itemType) => KindOfItemType(itemType) is not null;

    public static string KindLabel(string kind) => kind == AxeKind ? "Hacha" : "Pico";

    public static string KindEmoji(string kind) => kind == AxeKind ? "🪓" : "⛏️";

    // El comando que usa cada herramienta.
    public static string KindCommand(string kind) => kind == AxeKind ? "/chop" : "/mine";

    // Cuánto multiplica la herramienta la cantidad de ESTA rareza: Común, Raro y Épico por el multiplicador de la herramienta; lo Legendario y lo Mítico siguen dando 1 (nunca sale mucho de algo raro, como en
    // GatheringYield). Con factor 1 (o menos) no cambia nada.
    public static double QuantityFactor(string rarity, double toolMultiplier) =>
        rarity is "Común" or "Raro" or "Épico" ? Math.Max(1.0, toolMultiplier) : 1.0;
}
