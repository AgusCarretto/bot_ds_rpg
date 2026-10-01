namespace BotDsRpg.GameData;

// Lo que da un ítem al usarlo (item_buffs): +AttackPercent% de ataque durante Minutes minutos. Hoy solo los banquetes Míticos.
public sealed record ItemBuff(int AttackPercent, int Minutes);

// El buff de ataque ACTIVO de un jugador (player_buffs): hasta cuándo dura y qué ítem lo dio.
public sealed record ActiveBuff(int AttackPercent, DateTime ExpiresAt, string? Source)
{
    public TimeSpan Remaining => ExpiresAt - DateTime.UtcNow;
}

// La cuenta PURA del buff de ataque. Se aplica sobre el ataque TOTAL del jugador (nivel + arma con su sinergia de clase), no
// solo sobre el arma. Los buffs NO se acumulan: usar otro banquete reemplaza al anterior (player_buffs guarda uno solo), y en
// plena pelea Rescale recalcula desde el ataque sin buff para no multiplicar dos veces.
public static class AttackBuff
{
    public static int Apply(int baseDamage, int percent) =>
        percent <= 0 ? baseDamage : (int)Math.Round(baseDamage * (1 + percent / 100.0));

    // El daño que tenía la pelea con "currentPercent" ya aplicado, llevado a "newPercent": primero se le saca el buff viejo y
    // después se le pone el nuevo (así usar un segundo banquete en la misma pelea no apila +15% sobre +15%).
    public static int Rescale(int currentDamage, int currentPercent, int newPercent)
    {
        int baseDamage = currentPercent <= 0 ? currentDamage : (int)Math.Round(currentDamage / (1 + currentPercent / 100.0));
        return Apply(baseDamage, newPercent);
    }
}
