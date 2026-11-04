namespace BotDsRpg.GameData;

// Los tres comandos de recolección (/chop, /mine y, desde la v0.48.0, /fish) comparten un solo flujo (Modules/GatheringModule.ExecuteGatherAsync); esto dice cuál es cuál a partir del cooldown que
// recibe (el normal o su versión avanzada) y de qué oficio, evento y herramienta cuelga cada uno. Pura, sin base.
public static class GatherKinds
{
    public const string Chop = "chop";
    public const string Mine = "mine";
    public const string Fish = "fish";

    // chop / chop_adv → Chop, fish / fish_adv → Fish, todo lo demás (mine / mine_adv) → Mine, como siempre.
    public static string Of(CooldownDefinition definition) => Of(definition.CommandName);

    public static string Of(string commandName) => commandName switch
    {
        "chop" or "chop_adv" => Chop,
        "fish" or "fish_adv" => Fish,
        _ => Mine,
    };

    public static string ProfessionKey(string kind) => kind switch
    {
        Chop => ProfessionCatalog.WoodcutterKey,
        Fish => ProfessionCatalog.FisherKey,
        _ => ProfessionCatalog.MinerKey,
    };

    // El evento que cuenta usos (misiones y logros) y el que lleva la XP del oficio.
    public static string UseEvent(string kind) => kind switch { Chop => GameEventKinds.Chop, Fish => GameEventKinds.Fish, _ => GameEventKinds.Mine };

    public static string XpEvent(string kind) => kind switch { Chop => GameEventKinds.ChopXp, Fish => GameEventKinds.FishXp, _ => GameEventKinds.MineXp };

    // El evento que cuenta las unidades: /chop y /mine suman a gathered_units; la pesca tiene el suyo (fish_caught) para no tocar el logro Recolector ni la misión de juntar materiales.
    public static string UnitsEvent(string kind) => kind == Fish ? GameEventKinds.FishCaught : GameEventKinds.GatheredUnits;
}
