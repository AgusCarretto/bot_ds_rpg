using BotDsRpg.Services;
using Discord.Commands;

// Parte de TextCommandModule (ver el comentario en TextCommandModule.cs): los recordatorios de cooldown (v0.16.0).
public partial class TextCommandModule
{
    // "aa recordatorios" (alias "aa reminders", "aa avisos") — la misma pantalla que /reminders (con la lista desplegable). Por texto no hay mensajes privados: sale en el canal.
    [Command("reminders")]
    [Alias("recordatorios", "recordatorio", "reminder", "avisos", "aviso")]
    [Summary("Los avisos del bot cuando termina una espera (cacería, viaje, talar, jefe...): \"aa recordatorios\".")]
    public async Task RemindersAsync()
    {
        try
        {
            var view = await ReminderModule.ExecuteViewAsync(userRepository, reminderRepository, Context.User.Id);
            await ReplyAsync(view.PlainMessage, embed: view.Embed, components: view.Components);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await ReplyAsync("¡Upa! No pude abrir tus recordatorios, intentá de nuevo en un momento.");
        }
    }
}
