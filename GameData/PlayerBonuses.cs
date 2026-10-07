namespace BotDsRpg.GameData;

// Todo lo PERMANENTE que mejora a un jugador contra los monstruos, junto: sus mascotas (ya con la bendición Manada aplicada), su cantidad de Fuegos Nuevos y sus bendiciones.
// Es el único objeto que leen las recompensas (CombatRewardCalculator), el perfil de combate (PlayerCombatProfileCalculator) y la recolección (/chop, /mine): se arma UNA vez al
// empezar la pelea (Services/PlayerBonusService) y viaja en CombatState.Bonuses / RaidParticipant.Bonuses. Los duelos y la Arena NO lo reciben: se juegan con nivel + equipo + clase.
// Todo son multiplicadores sobre la base (1,0 = sin efecto); los de cada fuente se MULTIPLICAN entre sí (Fuego Nuevo × mascotas × bendiciones), que es el "presupuesto de
// multiplicadores" del spec. Las cuentas están acá y no repartidas: si una fuente cambia, cambia en un solo lugar.
public sealed record PlayerBonuses(
    PetBonuses Pets, int FuegoNuevo = 0, IReadOnlyDictionary<string, int>? BlessingLevels = null, IReadOnlyDictionary<string, long>? ProfessionXp = null)
{
    public static readonly PlayerBonuses None = new(PetBonuses.None);

    // Para las llamadas que solo conocen mascotas (y las pruebas): un PetBonuses es un PlayerBonuses sin Fuegos Nuevos ni bendiciones.
    public static implicit operator PlayerBonuses(PetBonuses pets) => new(pets);

    public int BlessingLevel(string key) =>
        BlessingLevels is not null && BlessingLevels.TryGetValue(key, out int level) ? Math.Clamp(level, 0, BlessingCatalog.MaxLevel) : 0;

    private double PetDrop => 1 + (Pets.DropPercent / 100.0);

    // La chance de drop de /hunt y de /travel se multiplica por esto (relativo: +24 % sobre un 6 % da 7,44 %); el cofre del jefe no.
    public double HuntDropMultiplier =>
        FuegoNuevoRules.HuntDropMultiplier(FuegoNuevo) * PetDrop * (1 + (BlessingCatalog.HuntDropPerLevel * BlessingLevel(BlessingCatalog.HuntDropKey)));

    public double TravelDropMultiplier =>
        FuegoNuevoRules.TravelDropMultiplier(FuegoNuevo) * PetDrop * (1 + (BlessingCatalog.TravelDropPerLevel * BlessingLevel(BlessingCatalog.TravelDropKey)));

    public double XpMultiplier =>
        FuegoNuevoRules.XpMultiplier(FuegoNuevo) * (1 + (Pets.XpPercent / 100.0)) * (1 + (BlessingCatalog.XpPerLevel * BlessingLevel(BlessingCatalog.XpKey)));

    public double GoldMultiplier =>
        (1 + (Pets.GoldPercent / 100.0)) * (1 + (BlessingCatalog.GoldPerLevel * BlessingLevel(BlessingCatalog.GoldKey)));

    // Poder (solo contra monstruos): el ataque y la defensa TOTALES (nivel + equipo) y la vida máxima de combate.
    public double AttackMultiplier => 1 + (BlessingCatalog.AttackPerLevel * BlessingLevel(BlessingCatalog.AttackKey));

    public double DefenseMultiplier =>
        (1 + (Pets.DefensePercent / 100.0)) * (1 + (BlessingCatalog.DefensePerLevel * BlessingLevel(BlessingCatalog.DefenseKey)));

    public double MaxHpMultiplier => 1 + (BlessingCatalog.HpPerLevel * BlessingLevel(BlessingCatalog.HpKey));

    // Los oficios (v0.13.0, GameData/ProfessionRules.cs): XP total de cada uno (la saca PlayerBonusService de los contadores) y su nivel. Sin datos = nivel 0 = sin efecto.
    public long ProfessionXpOf(string key) => ProfessionXp is not null && ProfessionXp.TryGetValue(key, out long xp) ? Math.Max(0, xp) : 0;

    public int ProfessionLevel(string key) => ProfessionRules.LevelFor(ProfessionXpOf(key));

    // Recolección: cuántas unidades da un /chop o un /mine (se redondea al azar, ver GatheringYield.RollScaled). Multiplica Fuego Nuevo × bendición × oficio × la mascota del Fogón.
    public double ChopMultiplier =>
        FuegoNuevoRules.GatherMultiplier(FuegoNuevo) * (1 + (BlessingCatalog.ChopPerLevel * BlessingLevel(BlessingCatalog.ChopKey)))
        * ProfessionRules.QuantityMultiplier(ProfessionLevel(ProfessionCatalog.WoodcutterKey)) * PetGather;

    public double MineMultiplier =>
        FuegoNuevoRules.GatherMultiplier(FuegoNuevo) * (1 + (BlessingCatalog.MinePerLevel * BlessingLevel(BlessingCatalog.MineKey)))
        * ProfessionRules.QuantityMultiplier(ProfessionLevel(ProfessionCatalog.MinerKey)) * PetGather;

    private double PetGather => 1 + (Pets.GatherPercent / 100.0);

    // La chance de que la rareza de lo recolectado suba un escalón (solo los oficios la dan).
    public double ChopRarityUpgrade => ProfessionRules.RarityUpgradeChance(ProfessionLevel(ProfessionCatalog.WoodcutterKey));

    public double MineRarityUpgrade => ProfessionRules.RarityUpgradeChance(ProfessionLevel(ProfessionCatalog.MinerKey));

    // Cuánto Polvo cuesta un intento de encantamiento (1,0 = lo de siempre).
    public double EnchantDustMultiplier => ProfessionRules.DustMultiplier(ProfessionLevel(ProfessionCatalog.EnchanterKey));

    // Cuánto hay que esperar para volver a alimentar a la MISMA mascota (la hora de siempre menos lo de Buen Pienso).
    public TimeSpan PetFeedCooldown =>
        PetRules.FeedCooldown - (BlessingCatalog.PetFeedReductionPerLevel * BlessingLevel(BlessingCatalog.PetFeedKey));

    // Cuánto multiplica la bendición Manada a los bonus de las mascotas (se aplica una vez, al armar Pets; está acá para mostrarlo).
    public double PetsBlessingMultiplier => 1 + (BlessingCatalog.PetsPerLevel * BlessingLevel(BlessingCatalog.PetsKey));

    public bool IsNone =>
        Pets.IsNone && FuegoNuevo == 0 && (BlessingLevels is null || BlessingLevels.Count == 0)
        && ProfessionCatalog.All.All(p => ProfessionLevel(p.Key) == 0);

    // Aplica un multiplicador a una cantidad entera (oro, EXP, ataque, defensa), redondeando al más cercano. Con 1,0 o menos no toca nada.
    public static int Scale(int amount, double multiplier) =>
        multiplier <= 1.0 ? amount : (int)Math.Round(amount * multiplier, MidpointRounding.AwayFromZero);
}
