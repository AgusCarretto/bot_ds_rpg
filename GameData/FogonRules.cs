using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Lo que le pasó a la puerta en una victoria sobre un jefe (LevelUpOutcome.Gate): None = nada, Opened = era la PRIMERA vez que vencía al jefe de la última zona (se abrió El Fogón
// Eterno), Cleared = le ganó al Asador Eterno (se habilita el Fuego Nuevo).
public enum GateEvent { None, Opened, Cleared }

// El Fogón Eterno (v0.11.0, "Zona 0"): la puerta al Fuego Nuevo. Reglas puras (sin base de datos) — los datos (zona, equipo, recetas y jefe) están en Database/seed_fogon.sql.
//   · Se abre al vencer al jefe de la ÚLTIMA zona de la escalera.
//   · Para entrar (/zona 0) hay que llevar PUESTOS el arma y el amuleto del Fogón (uno solo de cada uno, iguales para todas las clases) y tener el nivel de la puerta.
//   · Adentro no hay cacería ni viajes ni raid: solo /boss, contra el Asador Eterno. Ganarle devuelve al jugador a la última zona y habilita el Fuego Nuevo.
// El jugador nunca tiene current_zone_id = 0: users.in_gate dice que está parado en la puerta (su zona sigue siendo la última normal), así todo lo que mira la zona actual no cambia.
public static class FogonRules
{
    public const int GateZoneId = 0;

    public const string WeaponName = "Trinche del Asador Eterno";
    public const string AmuletName = "Brasa del Fogón Eterno";
    public const string BossName = "El Asador Eterno";

    public static bool IsKeyWeapon(Item? weapon) => weapon is not null && weapon.Name == WeaponName;

    public static bool IsKeyAmulet(Item? amulet) => amulet is not null && amulet.Name == AmuletName;

    public static bool HasKeyGear(Item? weapon, Item? amulet) => IsKeyWeapon(weapon) && IsKeyAmulet(amulet);

    // La última zona de la escalera (la de más nivel), o null si no hay ninguna. orderedZones: GameData/ZoneRanking.OrderByDifficulty de las zonas normales.
    public static Zone? LastZone(IReadOnlyList<Zone> orderedZones) => orderedZones.Count == 0 ? null : orderedZones[^1];

    // ¿Ya venció al jefe de la última zona? (highest_zone_cleared guarda un zone_id; se compara por POSICIÓN en el orden de dificultad, como el resto de la progresión).
    public static bool IsGateOpen(IReadOnlyList<Zone> orderedZones, int highestZoneCleared) =>
        orderedZones.Count > 0 && highestZoneCleared > 0 && ZoneRanking.RankOf(orderedZones, highestZoneCleared) >= orderedZones.Count;

    public enum EntryStatus { Ok, NotOpen, LevelTooLow, MissingGear }

    // MissingWeapon / MissingAmulet: qué pieza le falta (solo con MissingGear).
    public sealed record EntryCheck(EntryStatus Status, bool MissingWeapon, bool MissingAmulet);

    // Las condiciones para ENTRAR a la puerta, en este orden: que esté abierta, el nivel y el equipo. gate: la zona puerta (su MinLevel es el nivel mínimo).
    public static EntryCheck CheckEntry(IReadOnlyList<Zone> orderedZones, Zone gate, int playerLevel, int highestZoneCleared, Item? weapon, Item? amulet)
    {
        if (!IsGateOpen(orderedZones, highestZoneCleared))
        {
            return new EntryCheck(EntryStatus.NotOpen, false, false);
        }

        if (playerLevel < gate.MinLevel)
        {
            return new EntryCheck(EntryStatus.LevelTooLow, false, false);
        }

        return HasKeyGear(weapon, amulet)
            ? new EntryCheck(EntryStatus.Ok, false, false)
            : new EntryCheck(EntryStatus.MissingGear, !IsKeyWeapon(weapon), !IsKeyAmulet(amulet));
    }

    // Los textos de las dos victorias (primera vez sobre el jefe de la última zona, y ganarle al Asador).
    public static readonly string OpenedText =
        $"¡Se abrió **El Fogón Eterno**, la puerta al Fuego Nuevo! Forjá el **{WeaponName}** y la **{AmuletName}** en la herrería (**/forge**) y entrá con **/zona 0**. Mirá cómo funciona con **/info tema:fogon**.";

    public static readonly string ClearedText =
        "Volviste a la última zona y se habilitó el **Fuego Nuevo**: volver a empezar, más rápido, con porcentajes permanentes, una bendición y todo lo que juntaste (mascotas, oro y logros). " +
        "Es voluntario: mirá lo que se va y lo que se queda con **/fuegonuevo**, y cómo funciona con **/info tema:fuego**.";

    // Lo que falta, en palabras: "el Trinche del Asador Eterno y la Brasa del Fogón Eterno".
    public static string MissingText(EntryCheck check) => (check.MissingWeapon, check.MissingAmulet) switch
    {
        (true, true) => $"el **{WeaponName}** y la **{AmuletName}**",
        (true, false) => $"el **{WeaponName}**",
        (false, true) => $"la **{AmuletName}**",
        _ => string.Empty,
    };
}
