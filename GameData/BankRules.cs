namespace BotDsRpg.GameData;

// El banco (/bank): una cuenta que se COMPRA una sola vez y donde se guarda oro aparte de la billetera. Lo que está en el banco no lo toca la
// penalidad por muerte (GameData/DeathPenalty.cs). Sin interés ni tope de capacidad; si algún día se quiere un sumidero de oro más, la capacidad ampliable va acá.
//
// El MINIBUFF (v0.21.0, el dueño: «un minibuff al banco para tener plata en el banco un mínimo al menos… como una curva exponencial hasta el 5 %: tener más siempre te da más, pero cada vez menos»):
// el oro guardado da hasta +5 % de ORO en las peleas (cacería, viaje, jefe, jefe en equipo y /autohunt; se multiplica con las mascotas y las bendiciones, como todo lo de PlayerBonuses).
//   bonus = 5 % × (1 − e^(−saldo / τ))
// τ está medido en «cacerías de oro de tu zona» (MissionRewards.GoldUnit, la unidad de todas las recompensas del juego) y no en oro: así el banco sirve en TODAS las etapas y no solo al final
// (en la zona 1 una cacería paga 14 y en la 5 paga 375). Se calibró sobre la zona 5 con el ejemplo del dueño: 100.000 de oro dan 4,90 % ⇒ τ = 25.600 de oro = 68,27 cacerías.
public static class BankRules
{
    // Lo que cuesta abrir la cuenta (el dueño pidió 1.000).
    public const int AccountPrice = 1000;

    // El tope del bonus de oro y el τ de la curva (en cacerías de oro de la zona).
    public const double MaxGoldBonusPercent = 5.0;
    public const double GoldBonusHunts = 25_600.0 / 375.0;

    // El bonus de oro (en %, de 0 a MaxGoldBonusPercent) de un saldo, según la zona del jugador (su rank por dificultad, 1..N). Sin saldo, 0; crece siempre, cada vez más despacio.
    public static double GoldBonusPercent(long bankGold, int zoneRank)
    {
        if (bankGold <= 0)
        {
            return 0;
        }

        double hunts = bankGold / (double)MissionRewards.GoldUnit(zoneRank);
        return MaxGoldBonusPercent * (1 - Math.Exp(-hunts / GoldBonusHunts));
    }

    // «+3,2 %» (con coma decimal y dos decimales como mucho).
    public static string BonusText(double percent) =>
        "+" + Math.Round(percent, 2, MidpointRounding.AwayFromZero).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + " %";
}
