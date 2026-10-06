namespace BotDsRpg.Models;

// Una especie de mascota (tabla pet_species; el catálogo lo carga Database/seed_pets.sql): una por zona. BonusKind es gold / xp / defense / drop (GameData/PetRules.cs);
// MaxBonusPercent es lo que da al nivel máximo. EggItemId/EggName: el huevo (un ítem) que se entrega la primera vez que se vence al jefe de su zona.
public sealed record PetSpecies(
    int SpeciesId, int ZoneId, string ZoneName, string Name, string? Emoji, string BonusKind, double MaxBonusPercent, int EggItemId, string EggName);

// Una mascota que un jugador YA tiene. El nivel no se guarda: sale de FeedPoints (GameData/PetRules.LevelFor). LastFedAtUtc manda el cooldown de una hora para alimentarla.
public sealed record OwnedPet(PetSpecies Species, int FeedPoints, DateTime? LastFedAtUtc);
