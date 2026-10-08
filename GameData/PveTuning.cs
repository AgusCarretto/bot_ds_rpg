namespace BotDsRpg.GameData;

// Los ajustes de balance que valen SOLO contra monstruos (/hunt, /travel, /boss, /raid y /autohunt). Es el espejo de PvpTuning: las clases se calibraron contra monstruos una por
// una (ClassPassives, AbilityTuning) y el PvP tiene su propia vida por clase; esto corrige lo que se ve recién al jugar la escalera de zonas. Un factor de vida de combate por clase.
//
// MEDIDO (2026-10-08, el dueño: «¿el Ninja está muy OP?», con el combate real: CombatTurnResolver, los monstruos de la base, habilidad apenas está lista, sin comida ni huida):
//   · El Ninja perdona la falta de equipo mejor que nadie (su esquive vale más cuanto más daño entra): al jefe de la Zona 2 con arma de Zona 1 y sin amuleto, nivel 13, le pierde
//     35 % de los intentos contra 51 % el Guerrero, 68 % el Hechicero y 87 % el Arquero. Con el equipo de la escalera, en cambio, el Guerrero rinde mejor que él.
//   · El Arquero es la clase floja de verdad: pierde 2 a 4 veces más que las otras, igual que en PvP (por eso PvpTuning ya le da ×1,25 de vida).
// Con ×0,95 el Ninja conserva una ventaja chica sin equipo y queda entre el Guerrero y el Hechicero con equipo (×0,90 se pasaba y quedaba peor que el Hechicero). Con ×1,15 el Arquero
// queda a la par del Hechicero con equipo propio (jefe de Z3 nivel 16: 8 % contra 9 %) y sigue siendo la clase más frágil sin equipo: el dueño dijo que su crítico pega mucho según
// cómo se juegue, así que se quedó con el más bajo de los dos que se probaron (×1,25 lo dejaba entre el Guerrero y el Hechicero).
//
// Entra por PlayerCombatProfileCalculator.Resolve y SOLO cuando hay bonuses (las peleas contra monstruos los pasan; los duelos y la Arena no, y por eso el PvP no se entera): la vida
// extra se suma al MaxHpMultiplier de la clase, igual que la bendición Corazón de Brasa, así CombatState.ToDbHpDelta sigue devolviendo vida real. Si se retocan las clases o los
// monstruos hay que volver a medirlo.
public static class PveTuning
{
    public static double HpFactor(string playerClass) => playerClass switch
    {
        "Ninja" => 0.95,
        "Arquero" => 1.15,
        _ => 1.0,
    };
}
