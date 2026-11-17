namespace BotDsRpg.GameData;

// Los tres caminos de cada escena. Seguro: pelea contra un monstruo común. Arriesgado: contra el élite, con más botín. Misterioso: un evento al azar.
public enum AdventurePath
{
    Safe,
    Risky,
    Mystery,
}

// Lo que puede salir en el camino misterioso.
public enum AdventureEvent
{
    Chest,      // 🧰 +30 % de botín
    Campfire,   // 🔥 recuperás el 30 % de la vida
    Trap,       // 🕸️ perdés el 15 % de la vida (nunca te mata: te deja en 1)
    Shrine,     // ⛩️ +10 % de daño por el resto de la aventura
    Ambush,     // 👤 pelea contra el élite, sin el +20 %
}

// Una escena del camino: el texto que se lee antes de elegir.
public sealed record AdventureScene(string Key, string Title, string Text);

// La Aventura (v0.57.0, docs/aventura.md; el dueño 2026-11-16): 5 escenas con 3 caminos cada una y el Guardián del Camino al final. Una sola aventura para todas las
// zonas: la dificultad es la de la zona donde estás parado (sus monstruos) y no tu nivel. Cada 3 horas, sin comida adentro, difícil a propósito («ir siempre a la segura
// y llegar al final en zona 1 nivel 1 te daría DEMASIADO»). Premio: 3 viajes de oro y EXP por el botín, y una chance de la caja de la zona. Si morís, la penalidad de
// siempre y sin premio. Los multiplicadores son propuesta, «a medir».
public static class AdventureRules
{
    public const string CommandName = "aventura";
    public static readonly CooldownDefinition Cooldown = new(CommandName, Loc.Mark("Aventura"), "🗺️", TimeSpan.FromHours(3));

    public const int Scenes = 5;                       // la última es el Guardián
    public const double MonsterMultiplier = 1.3;       // la VIDA de comunes y élites de la aventura, contra los de afuera
    // v0.59.0 (el dueño, 2026-11-17: «el camino difícil, un poquito más difícil que el travel normal… que pegue ×1,3, y el hunt mismo que pegue poquito más, tipo 1,2, por más
    // que sea safe es difícil… algo para FN más altos: la puede hacer cualquiera, pero si la quiere hacer alguien de zona o nivel bajo, que le cueste bastante»). Eligió que vaya
    // ENCIMA del ×1,3 de antes y solo en el daño: el común pega ×1,3 × 1,2 = ×1,56 y el élite (arriesgado y emboscada) ×1,3 × 1,3 = ×1,69. La vida sigue en ×1,3.
    public const double SafeDamageMultiplier = MonsterMultiplier * 1.2;
    public const double RiskyDamageMultiplier = MonsterMultiplier * 1.3;
    // El Guardián: el élite de la zona ×2,0 de vida (v0.59.0, el dueño: «agregale poquito de vida… a ×2 también, igual que el ataque»; era ×1,6) y ×2,0 de daño (v0.57.2: «es muy
    // fácil para la mayoría»; era ×1,6 × 1,25).
    public const double GuardianMultiplier = 2.0;
    public const double GuardianDamageMultiplier = 2.0;
    public const int RiskyBonusPercent = 20;
    public const int ChestBonusPercent = 30;
    public const int CampfireHealPercent = 30;
    public const int TrapDamagePercent = 15;
    public const int ShrineDamagePercent = 10;
    public const int RewardTravels = 3;
    public const int BoxChancePercent = 25;
    public static readonly TimeSpan ChoiceTimeout = TimeSpan.FromMinutes(3);

    // El evento del camino misterioso, de un número 0–99: cofre 25, fogata 20, trampa 20, santuario 15, emboscada 20.
    public static AdventureEvent RollEvent(int roll) => roll switch
    {
        < 25 => AdventureEvent.Chest,
        < 45 => AdventureEvent.Campfire,
        < 65 => AdventureEvent.Trap,
        < 80 => AdventureEvent.Shrine,
        _ => AdventureEvent.Ambush,
    };

    // Un monstruo de afuera agrandado para la aventura (sin drops: el premio es el del final). damageMultiplier null = el mismo que la vida.
    public static MonsterTemplate Scale(MonsterTemplate monster, double multiplier, double? damageMultiplier = null) => monster with
    {
        MinHp = (int)Math.Round(monster.MinHp * multiplier),
        MaxHp = (int)Math.Round(monster.MaxHp * multiplier),
        MinDamage = (int)Math.Round(monster.MinDamage * (damageMultiplier ?? multiplier)),
        MaxDamage = (int)Math.Round(monster.MaxDamage * (damageMultiplier ?? multiplier)),
        DropItemNames = [],
    };

    // El común del camino seguro y el élite del arriesgado (y de la emboscada), con los multiplicadores de la aventura.
    public static MonsterTemplate SafeMonster(MonsterTemplate common) => Scale(common, MonsterMultiplier, SafeDamageMultiplier);
    public static MonsterTemplate RiskyMonster(MonsterTemplate elite) => Scale(elite, MonsterMultiplier, RiskyDamageMultiplier);

    public static MonsterTemplate Guardian(MonsterTemplate elite) =>
        Scale(elite, GuardianMultiplier, GuardianDamageMultiplier) with { Name = GuardianName };

    // «aa aventura seguro / arriesgado / misterioso» (v0.59.0): el camino escrito, sin tildes ni mayúsculas, en español o inglés, con atajos (s / a / m, 1 / 2 / 3).
    // En la escena del Guardián cualquiera vale (y también «guardian»): ahí hay un solo camino. Null = no es un camino.
    public static AdventurePath? ParsePath(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string word = TextNormalization.RemoveDiacritics(text.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0]).ToLowerInvariant();
        return word switch
        {
            "seguro" or "segura" or "safe" or "s" or "1" => AdventurePath.Safe,
            "arriesgado" or "arriesgada" or "riesgo" or "dificil" or "risky" or "risk" or "hard" or "a" or "r" or "2" => AdventurePath.Risky,
            "misterioso" or "misteriosa" or "misterio" or "mystery" or "mysterious" or "m" or "?" or "3" => AdventurePath.Mystery,
            "guardian" or "jefe" or "boss" or "g" => AdventurePath.Risky,
            _ => null,
        };
    }

    public const string GuardianName = "Guardián del Camino";

    // El premio: los 3 viajes por el botín acumulado (100 = ×1).
    public static int ApplyLoot(int amount, int lootPercent) => (int)Math.Round(amount * (100 + Math.Max(0, lootPercent)) / 100.0, MidpointRounding.AwayFromZero);

    // Las escenas del camino (salen en orden al azar; la 5.ª siempre es la del Guardián). {0} no hace falta: el monstruo se nombra en la pelea.
    public static readonly IReadOnlyList<AdventureScene> Pool =
    [
        new("bosque", Loc.Mark("🌲 El bosque cerrado"), Loc.Mark("Los árboles tapan el sol. Algo se mueve entre los troncos y el sendero se abre en tres.")),
        new("puente", Loc.Mark("🌉 El puente roto"), Loc.Mark("Un puente de sogas cuelga sobre un barranco. Faltan tablas, y del otro lado se oye un gruñido.")),
        new("cueva", Loc.Mark("🕳️ La boca de la cueva"), Loc.Mark("Una cueva respira aire caliente. Adentro brilla algo; afuera, huellas frescas.")),
        new("rio", Loc.Mark("🌊 El río crecido"), Loc.Mark("El río bajó con fuerza. Se puede vadear, buscar el vado de las piedras o seguir la orilla.")),
        new("ruinas", Loc.Mark("🏛️ Las ruinas"), Loc.Mark("Columnas caídas y un altar partido. Alguien dejó brasas que todavía humean.")),
        new("pantano", Loc.Mark("🐸 El pantano"), Loc.Mark("El barro te llega a las rodillas. Hay un camino de troncos, uno de juncos y uno que nadie usa.")),
        new("paso", Loc.Mark("⛰️ El paso de montaña"), Loc.Mark("El viento corta. Un sendero angosto sube; otro baja por las piedras sueltas.")),
        new("campamento", Loc.Mark("⛺ El campamento abandonado"), Loc.Mark("Carpas rotas y una olla volcada. Se fueron rápido. ¿De qué escapaban?")),
        new("niebla", Loc.Mark("🌫️ La niebla"), Loc.Mark("No ves ni tus manos. Se escuchan pasos que no son los tuyos.")),
        new("cruce", Loc.Mark("🪧 El cruce de caminos"), Loc.Mark("Tres carteles, todos borrados. Uno tiene marcas de garras.")),
        new("mina", Loc.Mark("⛏️ La mina vieja"), Loc.Mark("Rieles oxidados se pierden en la oscuridad. Una vagoneta todavía tiene algo adentro.")),
        new("fogon", Loc.Mark("🔥 El fogón apagado"), Loc.Mark("Un fogón frío en medio del claro. Alguien te estuvo esperando… o te está esperando.")),
        // v0.59.0: diez más (el dueño: «más escenas», con 12 y 4 por aventura se repetían rápido).
        new("molino", Loc.Mark("🌾 El molino abandonado"), Loc.Mark("Las aspas giran solas aunque no corre viento. La puerta está abierta y adentro cruje la madera.")),
        new("cementerio", Loc.Mark("🪦 El cementerio viejo"), Loc.Mark("Cruces torcidas y una tumba recién abierta. La tierra todavía está húmeda.")),
        new("tormenta", Loc.Mark("⛈️ La tormenta"), Loc.Mark("Se larga a llover de golpe. Un rayo ilumina una cabaña a lo lejos y algo que se mueve en el pastizal.")),
        new("estancia", Loc.Mark("🏚️ La estancia en ruinas"), Loc.Mark("El casco de una estancia sin techo. En la galería quedó una parrilla con brasas tibias.")),
        new("arroyo", Loc.Mark("🪨 El arroyo seco"), Loc.Mark("Piedras blancas donde antes corría agua. Entre ellas brilla algo, y las huellas que lo rodean son grandes.")),
        new("totem", Loc.Mark("🗿 Los tótems"), Loc.Mark("Tres tótems de piedra miran el sendero. Uno tiene los ojos pintados de rojo, fresco.")),
        new("tunel", Loc.Mark("🚇 El túnel de raíces"), Loc.Mark("Las raíces forman un túnel bajo la tierra. Se oye agua al fondo, y algo que rasca.")),
        new("carreta", Loc.Mark("🛒 La carreta volcada"), Loc.Mark("Una carreta tumbada en el camino, con la carga desparramada. Los bueyes no están; hay marcas de arrastre.")),
        new("lago", Loc.Mark("🏞️ El lago quieto"), Loc.Mark("El agua está tan quieta que parece un espejo. En el reflejo, alguien te mira desde la otra orilla.")),
        new("faro", Loc.Mark("🗼 La torre del vigía"), Loc.Mark("Una torre de piedra con una luz que se prende y se apaga. Alguien hace señas desde arriba… o te está llamando para algo.")),
    ];

    public static readonly AdventureScene Final = new("guardian", Loc.Mark("🗿 El Guardián del Camino"), Loc.Mark("El camino termina en un portal de piedra. Algo enorme se levanta para cerrarte el paso."));

    // Las 4 escenas de esta corrida, sin repetir (la 5.ª es Final).
    public static IReadOnlyList<AdventureScene> PickScenes(Random random) =>
        Pool.OrderBy(_ => random.Next()).Take(Scenes - 1).ToList();
}
