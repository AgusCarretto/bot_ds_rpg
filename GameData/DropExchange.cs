namespace BotDsRpg.GameData;

// El trueque con el tabernero (/exchange, y la lista "Cambiar drops" de la taberna): entregás 3 drops de una zona y te lleva 1 DISTINTO de la MISMA zona. Pedido del dueño:
// "cambias 3 de una zona por 1 de la misma, así si tenés mucha mala suerte compensás".
//   · Solo drops de monstruos de cacería y de viaje (los 3 de cada zona). NO madera ni minerales (eso se cambia entre jugadores), ni los trofeos de las cajas (no tienen zona),
//     ni los cofres de los jefes.
//   · Se entregan 3 del MISMO drop por cada 1 que se recibe (se puede repetir hasta MaxTimes veces en un comando).
//   · Qué zona tiene cada drop lo dice la base (monster_drops → monsters), no el código: el repositorio lo valida en la misma transacción.
// Medido contra las recetas reales con juego perfecto (cazar sin parar y viajar cada 30 minutos; un drop de cacería sale 3 % por minuto y el de viaje 2 %): el arma de clase se
// junta ~23 % más rápido (150 ➜ ~116 minutos) y el amuleto ~17 % (200 ➜ ~167); el arma general no cambia. Es un empujón moderado pensado para la mala racha, no para saltearse el farmeo.
public static class DropExchange
{
    // Cuántos se entregan y cuántos se reciben en cada cambio.
    public const int GiveAmount = 3;
    public const int GetAmount = 1;

    // Cuántos cambios se pueden hacer juntos en un comando (50 cambios = 150 drops por 50).
    public const int MaxTimes = 50;
}
