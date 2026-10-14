using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Lo que vende el Tabernero del Inframundo (v0.17.0; /deepshop, aa deepshop): los dos Míticos de recolección, por Polvo. Key: cómo se lo nombra (corteza, meteorito).
public sealed record DeepShopItem(string Key, string ItemName, string UseLine, IReadOnlyList<string> Aliases);

// Reglas PURAS del vendedor del inframundo. Por qué existe (el dueño, 2026-10-14): la Corteza del Árbol de Vida y el Fragmento de Meteorito salen al 0,5 % por /chop y /mine y hacen falta UNO de cada
// uno por vuelta (el Trinche y la Brasa del Fogón), y el Fuego Nuevo borra la mochila: juntarlos tarda de media 25 h por vuelta y una de cada cien pasa de 88 h, sin bajar nunca con las vueltas. Acá se
// compran con Polvo, que sale de /dismantle. El Polvo de un Mítico desmantelado es 500 (Dismantling.DustPerUnit): comprar a 3.000 deja siempre una diferencia, así que no se puede desmantelar-comprar en círculo.
public static class DeepShop
{
    // El precio de CADA uno, en Polvo. El dueño: «3000: todo lo que farmeo me da para mucho más; luego vemos si lo dejamos más caro». Es el único lugar donde cambiarlo.
    public const int PricePerUnit = 3000;

    // Cuántos se compran por vez (un pedido escrito). La lista desplegable compra de a uno.
    public const int MaxPerPurchase = 10;

    public static readonly IReadOnlyList<DeepShopItem> Stock =
    [
        new("corteza", "Corteza del Árbol de Vida", "La pide el Trinche del Asador Eterno (el equipo del Fogón: **/forge-special**).", ["corteza", "arbol", "árbol", "arbol de vida", "madera", "vida"]),
        new("meteorito", "Fragmento de Meteorito", "Lo pide la Brasa del Fogón Eterno (el equipo del Fogón: **/forge-special**).", ["meteorito", "meteoro", "fragmento", "mineral", "fragmento de meteorito"]),
    ];

    // Qué quiso decir el jugador: la clave, el nombre del ítem o una palabra suelta (sin importar mayúsculas ni tildes). Null si no se entiende o hay más de uno posible.
    public static DeepShopItem? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string wanted = text.Trim();
        var exact = Stock.Where(i => AutocompleteText.SameName(i.Key, wanted) || AutocompleteText.SameName(i.ItemName, wanted) || i.Aliases.Any(a => AutocompleteText.SameName(a, wanted))).ToList();
        return exact.Count == 1 ? exact[0] : null;
    }

    // El vendedor se conoce al vencer al jefe de la ÚLTIMA zona (la puerta del Fogón abierta, la misma regla que /zona 0) y, una vez que hiciste un Fuego Nuevo, ya lo conocés para siempre
    // (el Fuego Nuevo vuelve a cerrar la puerta, pero uno no se olvida de dónde compró). Ese segundo caso es mi decisión: si debe cerrarse otra vez con cada vuelta, es sacar la segunda condición.
    public static bool IsOpen(IReadOnlyList<Zone> orderedZones, User player) =>
        FogonRules.IsGateOpen(orderedZones, player.HighestZoneCleared) || player.FuegoNuevo >= 1;

    public static int Cost(int quantity) => PricePerUnit * quantity;
}
