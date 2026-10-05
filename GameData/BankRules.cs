namespace BotDsRpg.GameData;

// El banco (/bank): una cuenta que se COMPRA una sola vez y donde se guarda oro aparte de la billetera. Lo que está en el banco no lo toca la
// penalidad por muerte (GameData/DeathPenalty.cs): ese es todo su valor. Sin interés y sin tope por ahora; si algún día se quiere un sumidero de
// oro más, la capacidad ampliable va acá.
public static class BankRules
{
    // Lo que cuesta abrir la cuenta (el dueño pidió 1.000).
    public const int AccountPrice = 1000;
}
