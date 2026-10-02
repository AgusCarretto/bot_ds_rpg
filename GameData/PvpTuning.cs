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
}
