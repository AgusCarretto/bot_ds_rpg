namespace BotDsRpg.GameData;

// Fuente única de verdad para las 4 clases jugables y su arma exclusiva.
// La usan tanto el comando /class (embed + botones) como, más adelante,
// la lógica de combate/crafteo que necesite conocer la afinidad de armas.
public sealed record ClassDefinition(string Name, string WeaponType, string Emoji, string Description);

public static class ClassCatalog
{
    public static readonly IReadOnlyList<ClassDefinition> All =
    [
        new ClassDefinition("Guerrero", "Espadas", "⚔️",
            "Combate cuerpo a cuerpo directo: resistente y equilibrado entre ataque y defensa."),
        new ClassDefinition("Ninja", "Dagas", "🗡️",
            "Golpes rápidos y críticos: prioriza velocidad por sobre la resistencia."),
        new ClassDefinition("Arquero", "Arcos", "🏹",
            "Daño a distancia: mantiene al enemigo lejos antes de que llegue al cuerpo a cuerpo."),
        new ClassDefinition("Hechicero", "Grimorios", "📖",
            "Magia ofensiva de gran alcance: frágil, pero con el mayor daño mágico."),
    ];
}
