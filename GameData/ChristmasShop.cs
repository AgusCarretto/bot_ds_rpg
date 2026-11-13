namespace BotDsRpg.GameData;

// El Reno (v0.57.0, el dueño 2026-11-13): la mascota del evento, comprada con 4.500 Regalos. Bonus FIJO (no come ni evoluciona): +15 % de XP y de oro y /travel en
// 25 minutos en vez de 30. Para siempre: queda como un contador de player_stats (StatKey = 1), que no toca ni el Fuego Nuevo ni el fin del evento. No cuenta en PvP
// (como todo PlayerBonuses).
public static class RenoRules
{
    public const string StatKey = "xmas_reno";
    public const int XpGoldPercent = 15;
    public static readonly TimeSpan TravelCooldown = TimeSpan.FromMinutes(25);
}

// Qué hace lo que se compra en la tienda navideña.
public enum ChristmasShopKind
{
    Bell,       // 🔔 Campana: suma una al saldo; «aa campana» la usa para reiniciar /hunt
    Sock,       // 🧦 Calcetín: se abre al comprarlo (oro + EXP + caja de la zona + Polvo)
    Candle,     // 🕯️ Vela: +50 % de Regalos por 1 hora (solo mientras caen)
    Unlock,     // títulos y el Reno: una sola vez, quedan en player_stats (UnlockStat = 1)
}

// Un artículo de la tienda. Price en Regalos; UnlockStat solo en los Unlock.
public sealed record ChristmasShopItem(string Key, string Name, string Emoji, int Price, ChristmasShopKind Kind, string Description, string? UnlockStat = null)
{
    public bool OneTime => Kind == ChristmasShopKind.Unlock;
}

// La tienda navideña (docs/navidad.md). Abre durante todo el evento (1 de diciembre → 6 de enero). Los precios los decidió el dueño (2026-11-13: Campana 3, Calcetín 50, Vela 70, Reno 4.500;
// la Bolsa de Polvo se sacó: «no me gusta»). Los premios del Calcetín y los títulos son propuesta, «a medir». El banner de nieve se suma cuando esté el arte.
public static class ChristmasShop
{
    public const int CandleGiftPercent = 50;                       // la Vela: +50 % de Regalos
    public static readonly TimeSpan CandleDuration = TimeSpan.FromHours(1);
    public const int SockDust = 50;                                // «a medir»
    public static readonly RewardSpec SockReward = new(10, 2, BoxGrant.Daily); // «a medir»: oro y EXP como un /daily del día 4–5 y la caja diaria de tu zona
    public const int MaxBellsPerPurchase = 100;

    public const string TitleElfStat = "xmas_title_elfo";
    public const string TitleSpiritStat = "xmas_title_espiritu";

    public static readonly IReadOnlyList<ChristmasShopItem> Items =
    [
        new("campana", "Campana", "🔔", 3, ChristmasShopKind.Bell, Loc.Mark("Reinicia la espera de /hunt. Comprá las que quieras y usalas con «aa campana».")),
        new("calcetin", "Calcetín", "🧦", 50, ChristmasShopKind.Sock, Loc.Mark("Se abre al comprarlo: oro, EXP, una caja de tu zona y Polvo.")),
        new("vela", "Vela", "🕯️", 70, ChristmasShopKind.Candle, Loc.Mark("+50 % de Regalos durante 1 hora (hasta el 24 de diciembre).")),
        new("elfo", "Elfo del Fogón", "🧝", 500, ChristmasShopKind.Unlock, Loc.Mark("Un título para siempre."), TitleElfStat),
        new("espiritu", "Espíritu de la Navidad", "🌟", 1500, ChristmasShopKind.Unlock, Loc.Mark("Un título para siempre."), TitleSpiritStat),
        new("reno", "Reno", "🦌", 4500, ChristmasShopKind.Unlock, Loc.Mark("Mascota para siempre: +15 % de XP y de oro, y /travel en 25 minutos."), RenoRules.StatKey),
    ];

    public static ChristmasShopItem? Find(string? key)
    {
        string wanted = (key ?? string.Empty).Trim().ToLowerInvariant();
        return Items.FirstOrDefault(i => i.Key == wanted);
    }

    // Lo que se puede comprar en ESA etapa: todo mientras dura el evento, salvo la Vela, que solo sirve mientras caen Regalos.
    public static bool Available(ChristmasShopItem item, ChristmasPhase phase) =>
        phase == ChristmasPhase.Farm || (phase == ChristmasPhase.Shop && item.Kind != ChristmasShopKind.Candle);

    // Regalos con la Vela prendida: +50 % redondeado para arriba (1 Regalo de /hunt pasa a 2).
    public static int WithCandle(int gifts, bool candleLit) =>
        candleLit && gifts > 0 ? (int)Math.Ceiling(gifts * (100 + CandleGiftPercent) / 100.0) : gifts;
}

public static class ChristmasShopText
{
    // El nombre para mostrar: los títulos van como «Título «Nombre»»; el resto, su nombre (nombres propios: quedan en español).
    public static string Label(ChristmasShopItem item) =>
        item.Kind == ChristmasShopKind.Unlock && item.UnlockStat != RenoRules.StatKey ? Loc.F($"Título «{item.Name}»") : item.Name;
}
