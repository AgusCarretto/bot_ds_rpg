namespace BotDsRpg.GameData;

// La vida que se MUESTRA de un jugador. En la base está su vida «real» (la que sube +15 por nivel); contra monstruos pelea con esa vida por un factor de clase (Guerrero ×1,2, Ninja ×0,95, Arquero ×1,15,
// más el encantamiento Vida y la bendición Corazón de Brasa: PlayerCombatProfileCalculator.HpMultiplier). Hasta la v0.25.2 la pelea mostraba la vida con el factor y el resto de las pantallas (victoria, perfil, /heal, /use)
// la real, así que el máximo «cambiaba» de una pantalla a la otra (un Ninja de 340 veía 323 peleando y 340 al ganar). El dueño (2026-10-23): «necesito que siempre sea la misma vida… si es ×0,95 mostrá el resultado»:
// desde la v0.25.3 TODAS las pantallas muestran la vida con el factor. Es solo la cifra que se ve; la base sigue guardando la real y el combate hace sus cuentas como siempre.
public static class HpDisplay
{
    // La misma cuenta que usa el combate (PlayerCombatProfileCalculator.Resolve) para armar la vida de la pelea.
    public static int Scale(int realHp, double multiplier) => Math.Max(0, (int)Math.Round(realHp * multiplier));

    // Lo contrario: cuánta vida REAL (la de la base) es una cantidad de vida de las que se ven. Una comida que dice «cura 15 HP» cura 15 de la vida que ves, igual que dentro de una pelea (donde la vida YA está en esas
    // unidades): fuera de pelea, para que sean 15 en pantalla, a un Ninja (×0,95) se le suman 16 reales y a un Guerrero (×1,2) 12 (v0.25.3, el dueño: «que haga la cuenta antes si es esa clase… que quede todo óptimo»).
    // Un valor positivo nunca se redondea a cero: una comida siempre cura algo.
    public static int ToReal(int shownHp, double multiplier) =>
        shownHp <= 0 ? 0 : Math.Max(1, (int)Math.Round(shownHp / Math.Max(0.01, multiplier), MidpointRounding.AwayFromZero));
}
