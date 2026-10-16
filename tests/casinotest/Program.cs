using BotDsRpg.Services;

// v0.10.1: las tragamonedas dejan de regalar oro (5 símbolos, el par paga ×1,5, los tres iguales ×5; v0.12.1: el par baja a ×1,1; v0.14.1: 6 símbolos y el par sube a ×1,3).
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "OK" : "FALLA")}] {what}"); if (!ok) fails++; }

Console.WriteLine("== 1. Las cuentas exactas ==");
Check(CasinoService.SlotsSymbolList.Count == 6 && CasinoService.SlotsSymbolList.Distinct().Count() == 6, "6 símbolos distintos");
Check(Math.Abs(CasinoService.SlotsThreeMatchChance - 1.0 / 36) < 1e-12 && Math.Abs(CasinoService.SlotsAllDifferentChance - 20.0 / 36) < 1e-12 && Math.Abs(CasinoService.SlotsPairChance - 15.0 / 36) < 1e-12, $"tres iguales 2,78 %, par 41,67 %, tres distintos 55,56 % ({CasinoService.SlotsThreeMatchChance:P1} / {CasinoService.SlotsPairChance:P1} / {CasinoService.SlotsAllDifferentChance:P1})");
Check(Math.Abs(CasinoService.SlotsThreeMatchChance + CasinoService.SlotsPairChance + CasinoService.SlotsAllDifferentChance - 1) < 1e-12, "las tres chances suman 100 %");
Check(CasinoService.SlotsPairMultiplier == 1.3 && CasinoService.SlotsThreeMatchMultiplier == 5 && CasinoService.CoinflipMultiplier == 2, "el par paga ×1,3, tres iguales ×5 y el coinflip sigue en ×2");
Check(Math.Abs(CasinoService.SlotsReturnToPlayer - 24.5 / 36) < 1e-12, $"retorno esperado ×{CasinoService.SlotsReturnToPlayer:0.####} por cada 1 apostado: la casa se queda con el {(1 - CasinoService.SlotsReturnToPlayer) * 100:0.#} %");

Console.WriteLine("\n== 2. El premio de cada jugada ==");
Check(CasinoService.SlotsPayout(100, 3) == 500 && CasinoService.SlotsPayout(100, 2) == 130 && CasinoService.SlotsPayout(100, 1) == 0, "100 apostados: tres iguales 500, par 130, nada 0");
Check(CasinoService.SlotsPayout(10, 2) == 13 && CasinoService.SlotsPayout(11, 2) == 14 && CasinoService.SlotsPayout(13, 2) == 16 && CasinoService.SlotsPayout(19, 2) == 24, "el par se redondea hacia abajo (10 → 13, 11 → 14, 13 → 16, 19 → 24)");
Check(Enumerable.Range(10, 200000).All(b => CasinoService.SlotsPayout(b, 2) == b + (b * 3) / 10 && CasinoService.SlotsPayout(b, 2) >= b + 1), "con cualquier apuesta desde 10, el par da EXACTAMENTE la apuesta + el 30 % redondeado hacia abajo (sin errores de coma flotante) y al menos +1: nunca un «ganaste» que pierde");
Check(CasinoService.SlotsPayout(int.MaxValue, 3) == int.MaxValue && CasinoService.SlotsPayout(int.MaxValue, 2) > int.MaxValue / 2, "una apuesta enorme no desborda");

Console.WriteLine("\n== 3. Sorteando de verdad ==");
var casino = new CasinoService();
const int Plays = 2_000_000;
long totalBet = 0, totalBack = 0; int threes = 0, pairs = 0, none = 0;
for (int i = 0; i < Plays; i++)
{
    var r = casino.PlaySlots(100);
    totalBet += 100; totalBack += r.Payout;
    if (r.Payout == 500) threes++; else if (r.Payout == 130) pairs++; else none++;
}
double rtp = (double)totalBack / totalBet;
Check(Math.Abs(threes / (double)Plays - 1.0 / 36) < 0.002 && Math.Abs(pairs / (double)Plays - 15.0 / 36) < 0.004 && Math.Abs(none / (double)Plays - 20.0 / 36) < 0.004, $"2 millones de tiradas: tres iguales {threes * 100.0 / Plays:0.00} %, par {pairs * 100.0 / Plays:0.00} %, nada {none * 100.0 / Plays:0.00} %");
Check(rtp is > 0.665 and < 0.695, $"y vuelve ×{rtp:0.0000} de lo apostado (esperado 0,6806): la casa gana, ya no regala oro");
// el viejo agujero: apostar todo, una y otra vez, ya no duplica el oro
var rng = new CasinoService();
double richer = 0; int tries = 2000, broke = 0;
for (int p = 0; p < tries; p++) { long gold = 1000; for (int s = 0; s < 30 && gold >= 10; s++) { var r = rng.PlaySlots((int)gold); gold = gold - (int)gold + r.Payout; } richer += gold; if (gold < 10) broke++; }
Check(richer / tries < 1000, $"jugando «all» 30 veces seguidas con 1.000 de oro terminan sin nada {broke * 100.0 / tries:0.0} % de los jugadores (el promedio queda en {richer / tries:0} < 1.000; con el par en ×2 el oro se multiplicaba)");
Check(Enumerable.Range(0, 2000).Select(_ => casino.PlaySlots(50)).All(r => r.Reveal.Length == 3 && r.Reveal.All(s => CasinoService.SlotsSymbolList.Contains(s))), "cada tirada muestra tres símbolos de la lista");

Console.WriteLine(fails == 0 ? "\nTODO OK" : $"\n{fails} FALLAS");
return fails == 0 ? 0 : 1;
