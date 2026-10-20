using BotDsRpg.GameData;

namespace BotDsRpg.Services;

// Mantiene al día GameData/UiEmojis (los íconos propios de la interfaz) leyendo los emojis de la APLICACIÓN del bot por nombre: una vez al arrancar y cada 10 minutos, así un emoji nuevo subido al
// Developer Portal se ve sin reiniciar y sin copiar ningún id. source devuelve (nombre, código) de cada emoji de la aplicación; en el juego es el cliente de Discord, en las pruebas una lista falsa.
// Nunca tira: si Discord no responde queda la tabla que ya había (y se registra).
public sealed class UiEmojiRefresher(Func<Task<IEnumerable<(string Name, string Code)>>> source)
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(10);

    public async Task RefreshAsync()
    {
        try
        {
            UiEmojis.Set(await source());
        }
        catch (Exception ex)
        {
            BotLog.Warn(ex);
        }
    }

    public void Start() =>
        _ = Task.Run(async () =>
        {
            await RefreshAsync();

            using var timer = new PeriodicTimer(Every);
            while (await timer.WaitForNextTickAsync())
            {
                await RefreshAsync();
            }
        });
}
