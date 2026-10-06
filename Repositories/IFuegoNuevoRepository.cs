namespace BotDsRpg.Repositories;

public enum RenewStatus
{
    Ok,
    NoAccount,    // el jugador no existe
    NotCleared,   // todavía no le ganó al Asador Eterno en esta vuelta (o un reinicio anterior ya lo gastó)
    StaleCount,   // el botón es de otra vuelta: el contador de Fuegos Nuevos ya no es el que vio al confirmar (doble click, dos instancias)
    InvalidClass, // la clase elegida no existe
}

// Lo que se ve ANTES de confirmar: qué se pierde (con cantidades) y qué se queda. LostByType: unidades de la mochila por tipo de ítem que se van (sin huevos ni comida de mascotas);
// KeptEggs/KeptPetFood: lo que se salva de la mochila. Las armas vienen por nombre (null = no lleva), con el tier de su encantamiento (0 = ninguno).
public sealed record RenewPreview(
    string Class, int Level, int FuegoNuevo, bool GateCleared,
    int Gold, int BankGold, bool HasBank, int Dust,
    string? WeaponName, int WeaponEnchant, string? AmuletName, int AmuletEnchant,
    IReadOnlyDictionary<string, long> LostByType, long KeptEggs, long KeptPetFood, int Pets, int Blessings);

// Resultado del reinicio. NewNumber: el Fuego Nuevo que acaba de hacer (el contador nuevo). Offer: las 3 bendiciones para elegir (null si ya las tiene todas al máximo).
// SatchelPetFood/SatchelBoxes: lo que le dio la Alforja del Fogonero al renacer (0 sin ella).
public sealed record RenewOutcome(
    RenewStatus Status, int NewNumber, string? ClassBefore, int LevelBefore, BlessingOffer? Offer, int SatchelPetFood, int SatchelBoxes);

// El Fuego Nuevo (v0.12.0, GameData/FuegoNuevoRules.cs): el reinicio voluntario que se habilita al vencer al Asador Eterno. Es UNA transacción: toma la fila del jugador, comprueba
// que lo tiene habilitado y que el contador es el que el jugador vio, y recién ahí borra, reinicia, anota el historial y deja la oferta de bendiciones. Doble click o dos instancias = un solo reinicio.
public interface IFuegoNuevoRepository
{
    // La pantalla de confirmación. Solo lee; null si el jugador no existe.
    Task<RenewPreview?> GetPreviewAsync(ulong discordId, CancellationToken cancellationToken = default);

    // Reinicia al jugador con la clase elegida. expectedCount: el contador de Fuegos Nuevos que se veía al confirmar (users.fuego_nuevo), para que un botón viejo no reinicie dos veces.
    Task<RenewOutcome> RenewAsync(ulong discordId, int expectedCount, string newClass, CancellationToken cancellationToken = default);
}
