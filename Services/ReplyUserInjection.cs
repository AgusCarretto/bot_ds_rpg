using Discord;
using Discord.Commands;

namespace BotDsRpg.Services;

// Marca un comando de texto que acepta «responder el mensaje de alguien» como si lo hubieras mencionado aunque su primer parámetro no sea un IUser (hoy, «aa cd»: el jugador va dentro de un texto libre).
// Los comandos cuyo primer parámetro SÍ es un IUser (aa p, aa dragon breed, aa give...) no necesitan la marca.
[AttributeUsage(AttributeTargets.Method)]
public sealed class AcceptsReplyUserAttribute : Attribute;

// Responder el mensaje de alguien = mencionarlo (v0.51.3, el dueño: «responder el mensaje de Polar Frost y poner aa dragon breed, entonces eso ya activa el breed con Polar Frost; así con cualquier cosa, por
// ejemplo responder y que al poner aa p salga el perfil de esa persona»). Es PURO (sin Discord): recibe el texto del comando (sin el prefijo), los comandos que Discord.Net encontró para ese texto y el id de a
// quién se le respondió, y devuelve el texto con la mención puesta (o null si no corresponde tocar nada). Quien lo usa (Program.HandleTextMessageAsync) ejecuta ese texto en vez del original.
//
// REGLAS (todas para no pisar nunca lo que la persona escribió):
//   • Solo si el comando recibe un jugador: su primer parámetro es un IUser (o varios, «params IUser[]») o está marcado con [AcceptsReplyUser].
//   • Solo si falta el jugador: lo escrito después del nombre del comando no trae una mención «<@...>» y tiene UN argumento menos que los parámetros del comando (aa give 5k = el monto y falta el jugador;
//     aa give polar 5k ya trae los dos). Para los marcados con [AcceptsReplyUser] hace falta que no se haya escrito nada («aa cd orden cazar 1» sigue siendo lo de siempre).
//   • La mención va JUSTO después del nombre del comando, que es donde cada uno de esos comandos espera al jugador (aa give <@id> 5k, aa trade <@id> «Roble» «Hierro»).
public static class ReplyUserInjection
{
    public static string? Rewrite(string input, IReadOnlyList<CommandMatch> matches, ulong replyUserId)
    {
        if (string.IsNullOrWhiteSpace(input) || matches.Count == 0)
        {
            return null;
        }

        // Discord.Net devuelve también los comandos de nombre más corto que empiezan igual («dragon» y «dragon breed» para «dragon breed»): el que se ejecuta es el de nombre más largo.
        string alias = matches.Select(m => m.Alias).OrderByDescending(a => a.Length).First();
        if (alias.Length == 0 || input.Length < alias.Length || !input.StartsWith(alias, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string rest = input[alias.Length..];
        if (rest.Length > 0 && !char.IsWhiteSpace(rest[0]))
        {
            return null;   // el nombre del comando no termina ahí (otro comando que empieza igual): no tocar nada
        }

        string args = rest.Trim();
        if (args.Contains("<@", StringComparison.Ordinal))
        {
            return null;   // ya mencionó a alguien: manda lo escrito
        }

        int typed = CountArguments(args);
        foreach (var match in matches.Where(m => string.Equals(m.Alias, alias, StringComparison.OrdinalIgnoreCase)))
        {
            var command = match.Command;
            bool marked = command.Attributes.OfType<AcceptsReplyUserAttribute>().Any();
            bool userFirst = command.Parameters.Count > 0 && IsUserType(command.Parameters[0].Type);
            if (!marked && !userFirst)
            {
                continue;
            }

            bool missing = marked ? typed == 0 : typed <= command.Parameters.Count - 1;
            if (missing)
            {
                return input[..alias.Length] + $" <@{replyUserId}>" + rest;
            }
        }

        return null;
    }

    private static bool IsUserType(Type type)
    {
        if (type.IsArray)
        {
            type = type.GetElementType()!;
        }

        return typeof(IUser).IsAssignableFrom(type);
    }

    // Cuántos argumentos se escribieron, con las comillas como las lee Discord.Net: «"Cuero Curtido" 5» son dos. Las comillas curvas también (el celular cambia las rectas solo).
    public static int CountArguments(string args)
    {
        const string quotes = "\"“”„«»";
        int count = 0;
        bool inQuote = false, inWord = false;
        foreach (char c in args)
        {
            if (quotes.Contains(c))
            {
                if (!inWord && !inQuote)
                {
                    count++;
                }

                inQuote = !inQuote;
                inWord = inQuote;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuote)
            {
                inWord = false;
                continue;
            }

            if (!inWord)
            {
                count++;
                inWord = true;
            }
        }

        return count;
    }
}
