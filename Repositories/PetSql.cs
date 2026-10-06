using System.Data.Common;
using BotDsRpg.Models;
using Dapper;

namespace BotDsRpg.Repositories;

// El SQL de las especies, compartido por PetRepository y por AdventureRepository (que entrega el huevo en la MISMA transacción que la primera victoria sobre el jefe).
// No abre conexión propia: opera sobre la que le pasan.
internal static class PetSql
{
    // Las columnas de una especie, con los alias que espera PetSpeciesRow (mismo criterio que MonsterRepository: comillas para conservar las mayúsculas).
    public const string SpeciesColumns = """
        s.species_id AS "SpeciesId", s.zone_id AS "ZoneId", z.name AS "ZoneName", s.name AS "Name", s.emoji AS "Emoji", s.bonus_kind AS "BonusKind",
        s.max_bonus_percent AS "MaxBonusPercent", s.egg_item_id AS "EggItemId", e.name AS "EggName"
        """;

    public const string SpeciesFrom = """
        pet_species s
        JOIN zones z ON z.zone_id = s.zone_id
        JOIN items e ON e.item_id = s.egg_item_id
        """;

    // La especie de una zona (hay una por zona), o null si esa zona no tiene.
    public static async Task<PetSpecies?> FindByZoneAsync(DbConnection connection, DbTransaction? transaction, int zoneId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<PetSpeciesRow>(new CommandDefinition(
            $"SELECT {SpeciesColumns} FROM {SpeciesFrom} WHERE s.zone_id = @ZoneId;",
            new { ZoneId = zoneId }, transaction: transaction, cancellationToken: cancellationToken));
        return row?.ToSpecies();
    }
}

internal sealed record PetSpeciesRow(
    int SpeciesId, int ZoneId, string ZoneName, string Name, string? Emoji, string BonusKind, double MaxBonusPercent, int EggItemId, string EggName)
{
    public PetSpecies ToSpecies() => new(SpeciesId, ZoneId, ZoneName, Name, Emoji, BonusKind, MaxBonusPercent, EggItemId, EggName);
}

internal sealed record OwnedPetRow(
    int SpeciesId, int ZoneId, string ZoneName, string Name, string? Emoji, string BonusKind, double MaxBonusPercent, int EggItemId, string EggName,
    int FeedPoints, DateTime? LastFedAt)
{
    public PetSpecies ToSpecies() => new(SpeciesId, ZoneId, ZoneName, Name, Emoji, BonusKind, MaxBonusPercent, EggItemId, EggName);
}
