namespace BotDsRpg.Models;

// Cuántas veces pasó un tipo de evento (con un detalle puntual, o null si no lo tiene) y cuánto sumó su amount: la materia prima del historial por juego.
public sealed record EventTotal(string Kind, string? Detail, long Count, long Amount);

// Un evento puntual, para listar los últimos (por ejemplo los duelos de /duels).
public sealed record RecentEvent(DateTime OccurredAtUtc, string Kind, string? Detail, long Amount);
