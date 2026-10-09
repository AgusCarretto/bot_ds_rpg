namespace BotDsRpg.GameData;

// Los ajustes de balance que valen SOLO en PvP (el duelo amistoso y la Arena). Contra monstruos las clases están calibradas una por una
// (ver ClassPassives y AbilityTuning); en un 1 contra 1 simétrico esos mismos números se acumulan: la durabilidad del Guerrero (vida x1.2,
// daño recibido x0.9, Aguante) lo hacía ganarle 74-85% a todas las clases y el Arquero perdía 80-85% contra casi todo, o sea que cualquier
// torneo lo ganaba un Guerrero. Por eso la vida de combate de PvP se escala por clase.
//
// MEDIDO (GameData/DuelEngine.Simulate, 3000-4000 duelos por cruce, a nivel 5 / 12 / 22 / 30 con su arma y su amuleto, ambos usando la
// habilidad apenas está lista): con estos factores el peor cruce queda en 68/32 en vez de 90/10, y se forma un piedra-papel-tijera
// (Guerrero > Ninja > Arquero y Hechicero > Guerrero) estable en los cuatro rangos. Con una sola palanca (la vida) ese es el piso: lo que
// queda es la interacción real entre las habilidades, y eso es parte del juego. Si se retocan las clases o las habilidades, hay que
// volver a medirlo (el arnés es chico: un doble bucle de Simulate).
public static class PvpTuning
{
    public static double HpFactor(string playerClass) => playerClass switch
    {
        "Guerrero" => 0.90,
        "Hechicero" => 1.20,
        "Arquero" => 1.25,
        _ => 1.0, // Ninja: la referencia
    };

    // La vida de combate que se usa en PvP, a partir de la de combate normal (que ya incluye la pasiva de vida del Guerrero).
    public static int MaxHp(int combatMaxHp, string playerClass) => Math.Max(1, (int)Math.Round(combatMaxHp * HpFactor(playerClass)));

    // La DEF de cada amuleto EN PvP. Desde la v0.15.1 los amuletos de las zonas 2 a 5 defienden mucho más contra monstruos (18/30/46/75 → 35/60/85/120, y la Brasa del Fogón 120 → 165) para que se
    // sienta el amuleto en /autohunt, pero todo el balance de PvP (los factores de vida de arriba, el piedra-papel-tijera) se midió con la escalera VIEJA: la defensa del rival se resta de tu ataque,
    // y con la nueva los duelos se estirarían a 3 veces más turnos y cambiaría quién gana. Por eso el duelo y la Arena siguen usando estos valores (por nombre de amuleto) y el encantamiento se
    // calcula sobre ellos. Un amuleto que no figura acá (uno nuevo, o los viejos que ya nadie puede conseguir) se usa tal cual.
    private static readonly IReadOnlyDictionary<string, int> AmuletDefenseInPvp = new Dictionary<string, int>
    {
        ["Hombreras de Cuero Grueso"] = 10,
        ["Talismán de Ceniza Bendita"] = 18,
        ["Peto de Escoria Templada"] = 30,
        ["Talismán del Volcán"] = 46,
        ["Corazón de Titán Engarzado"] = 75,
        ["Brasa del Fogón Eterno"] = 120,
    };

    // El amuleto tal como cuenta en PvP: una copia con la DEF de la escalera vieja (null si no lleva).
    public static Models.Item? Amulet(Models.Item? amulet) =>
        amulet is not null && AmuletDefenseInPvp.TryGetValue(amulet.Name, out int defense)
            ? new Models.Item
            {
                ItemId = amulet.ItemId, Name = amulet.Name, Type = amulet.Type, Rarity = amulet.Rarity, StatValue = defense, SellPrice = amulet.SellPrice,
                BuyPrice = amulet.BuyPrice, WeaponFamily = amulet.WeaponFamily, ClassRequirement = amulet.ClassRequirement, Emoji = amulet.Emoji,
                BoxMinItems = amulet.BoxMinItems, BoxMaxItems = amulet.BoxMaxItems,
            }
            : amulet;
}
