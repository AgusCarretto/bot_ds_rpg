namespace BotDsRpg.GameData;

// La vida que se MUESTRA de un jugador. En la base está su vida «real» (la que sube +15 por nivel); contra monstruos pelea con esa vida por un factor de clase (Guerrero ×1,2, Ninja ×0,95, Arquero ×1,15,
// más el encantamiento Vida y la bendición Corazón de Brasa: PlayerCombatProfileCalculator.HpMultiplier). Hasta la v0.25.2 la pelea mostraba la vida con el factor y el resto de las pantallas (victoria, perfil, /heal, /use)
// la real, así que el máximo «cambiaba» de una pantalla a la otra (un Ninja de 340 veía 323 peleando y 340 al ganar). El dueño (2026-10-23): «necesito que siempre sea la misma vida… si es ×0,95 mostrá el resultado»:
// desde la v0.25.3 TODAS las pantallas muestran la vida con el factor. Es solo la cifra que se ve; la base sigue guardando la real y el combate hace sus cuentas como siempre.
public static class HpDisplay
{
    // La misma cuenta que usa el combate (PlayerCombatProfileCalculator.Resolve) para armar la vida de la pelea.
    public static int Scale(int realHp, double multiplier) => Math.Max(0, (int)Math.Round(realHp * multiplier));
}
