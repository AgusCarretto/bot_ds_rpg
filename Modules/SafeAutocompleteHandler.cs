using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// Base de TODAS las listas desplegables (autocompletado) de comandos de barra. Hace dos cosas que antes no estaban:
//   · si armar la lista falla (la base tardó, se cortó la conexión), lo REGISTRA con BotLog.Error en vez de dejar que Discord
//     muestre "no se pudieron cargar las opciones" sin dejar rastro en el log de nadie, y devuelve la lista vacía;
//   · saca del código de cada lista lo que era idéntico (leer lo escrito y quién es el jugador).
// Cada lista solo implementa BuildAsync. El valor de cada opción sigue siendo lo que el comando ya aceptaba (nombre de ítem, id de
// zona), así que escribir a mano en vez de elegir de la lista sigue funcionando igual.
public abstract class SafeAutocompleteHandler : AutocompleteHandler
{
    public sealed override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        try
        {
            string typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;
            return AutocompletionResult.FromSuccess(await BuildAsync(context.User.Id, typed, services));
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            return AutocompletionResult.FromSuccess([]);
        }
    }

    // Las opciones a mostrar (hasta 25, ver AutocompleteText.MaxChoices). Las listas NO deben crearle cuenta a nadie: usar
    // IUserRepository.GetByDiscordIdAsync y no GetOrCreateUserAsync.
    protected abstract Task<IReadOnlyList<AutocompleteResult>> BuildAsync(ulong userId, string typed, IServiceProvider services);
}
