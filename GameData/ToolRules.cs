namespace BotDsRpg.GameData;

// Las HERRAMIENTAS (v0.29.0, /tools, aa herramientas|equipamiento; las arma el Carpintero, Modules/ToolsModule.cs). Hay una hacha (para /chop), un pico (para /mine) y, desde la v0.48.0, una caña (para /fish); sin ninguna se recolecta ×1, lo de siempre.
// Cada una tiene dos tiers que se desbloquean con la zona: el de la Zona 3 (×2) y el de la Zona 5 (×3). Los datos viven en la base (items.type 'Hacha' / 'Pico', items.stat_value = el multiplicador, la receta en
// recipes con la zona que la desbloquea, Database/add_tools.sql) y lo que tiene cada jugador en player_tools; acá solo están las reglas puras.
public static class ToolRules
{
    public const string AxeKind = "axe";
    public const string PickaxeKind = "pickaxe";
    public const string AxeType = "Hacha";
    public const string PickaxeType = "Pico";
    public const string RodKind = "rod";
    public const string RodType = "Caña";

    // Los tipos de herramienta, en el orden en que se muestran.
    public static readonly string[] Kinds = [AxeKind, PickaxeKind, RodKind];

    // El tipo de herramienta que es un ítem según su items.type (null si no es una herramienta).
    public static string? KindOfItemType(string? itemType) => itemType switch
    {
        AxeType => AxeKind,
        PickaxeType => PickaxeKind,
        RodType => RodKind,
        _ => null,
    };

    public static bool IsToolType(string? itemType) => KindOfItemType(itemType) is not null;

    public static string KindLabel(string kind) => kind switch { AxeKind => "Hacha", RodKind => "Caña", _ => "Pico" };

    public static string KindEmoji(string kind) => kind switch { AxeKind => "🪓", RodKind => "🎣", _ => "⛏️" };

    // El comando que usa cada herramienta.
    public static string KindCommand(string kind) => kind switch { AxeKind => "/chop", RodKind => "/fish", _ => "/mine" };

    // Los tiers de herramienta: la zona (por su posición en la escalera) que los abre, cuánto multiplican y cómo se llaman. Son los mismos datos de Database/add_tools.sql (los ítems y las recetas): la
    // prueba toolstest comprueba que coinciden con la base, así que si se suma un tier hay que sumarlo en los dos lados.
    public sealed record ToolTier(int ZoneRank, int Multiplier, string Name);

    public static readonly IReadOnlyList<ToolTier> Tiers = [new(3, 2, "Hierro Forjado"), new(5, 3, "Escoria Templada")];

    // El título del aviso (campo de la pantalla de victoria sobre el jefe).
    public static string UnlockTitle => Loc.T("🔨 ¡Herramientas nuevas!");

    // El aviso para quien vence por primera vez (de esta vuelta) al jefe de la zona bossRank (v0.52.1, el dueño: «avisá al matar el boss de la zona 2, al desbloquear la 3, que tenés las tools; mismo con la
    // zona 5: sino se pierde el tener las tools»). Esa victoria abre la zona siguiente y, si ella abre un tier de herramientas, se lo cuenta. null si no abre ninguno. Dice «al entrar a la Zona N» y no
    // «ya podés»: abrir la zona también pide el nivel, y las herramientas se hacen cuando la zona está abierta del todo. El Fuego Nuevo borra las herramientas, así que se avisa en cada vuelta.
    public static string? UnlockNotice(int bossRank)
    {
        var tier = Tiers.FirstOrDefault(t => t.ZoneRank == bossRank + 1);
        return tier is null
            ? null
            : Loc.F($"Al entrar a la **Zona {tier.ZoneRank}** vas a poder hacer en el Carpintero (`/tools`) el hacha, el pico y la caña de **{tier.Name}**: ×{tier.Multiplier} de lo que recolectás. Se hacen solo con materiales.");
    }

    // Cuánto multiplica la herramienta la cantidad de ESTA rareza: Común, Raro y Épico por el multiplicador de la herramienta; lo Legendario y lo Mítico siguen dando 1 (nunca sale mucho de algo raro, como en
    // GatheringYield). Con factor 1 (o menos) no cambia nada.
    public static double QuantityFactor(string rarity, double toolMultiplier) =>
        rarity is "Común" or "Raro" or "Épico" ? Math.Max(1.0, toolMultiplier) : 1.0;
}
