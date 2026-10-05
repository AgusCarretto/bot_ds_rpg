using BotDsRpg.Models;

namespace BotDsRpg.GameData;

// Un campo de la tarjeta de subida de nivel (un dato corto en una columna, o una línea más larga a lo ancho).
public sealed record LevelUpField(string Name, string Value, bool Inline);

// El mensaje de "¡Subiste de nivel!": título, descripción, campos y una frase de despedida. Lo arma LevelUpCard.Compose y lo convierte en
// embed el servicio de avisos (Services/ProgressNotifier.cs).
public sealed record LevelUpCardContent(string Title, string Description, IReadOnlyList<LevelUpField> Fields, string Footer);

// La subida de nivel como un MENSAJE PROPIO y vistoso. Antes era una línea más (un campo chico, "Ahora sos nivel 7") dentro del embed de la
// victoria, que ya tiene oro, XP, HP, resumen del combate, drop... y se perdía entre todo eso. Ahora sale aparte, apenas termina el comando.
//
// PURO (sin base ni Discord): recibe lo que ya se sabe del evento — a qué nivel llegó y cuántos subió — y las zonas, para avisar si con este
// nivel ya alcanza el mínimo de alguna. No lleva la vida máxima total a propósito: no todos los caminos que suben de nivel la tienen a mano
// (misiones, logros), pero sí cuánto sube por nivel (vida máxima, ataque y defensa) y que se cura del todo (ver LevelingCalculator.ApplyXpGain y CombatStats).
public static class LevelUpCard
{
    private static readonly string[] Cheers =
    [
        "Se nota que le pusiste ganas.",
        "El asado y el acero te están haciendo bien.",
        "Los monstruos ya te empiezan a respetar.",
        "Un mate para festejar... y a seguir.",
        "Cada nivel te acerca un poco más al Soberano.",
        "¡Qué crack! Seguí así.",
        "Así se hace: paso firme y mano en el acero.",
    ];

    public static LevelUpCardContent Compose(ulong discordId, int newLevel, int levelsGained, IReadOnlyList<Zone> zones, Random? random = null)
    {
        random ??= Random.Shared;

        int oldLevel = Math.Max(1, newLevel - Math.Max(1, levelsGained));
        int gained = newLevel - oldLevel;

        string title = gained > 1 ? $"🎉 ¡SUBISTE {gained} NIVELES! 🎉" : "🎉 ¡SUBISTE DE NIVEL! 🎉";

        string description =
            $"🌟 **¡<@{discordId}> ahora es nivel {newLevel}!** 🌟\n\n" +
            $"⬆️ Nivel {oldLevel}  ➜  **Nivel {newLevel}**";

        // Lo que sube de verdad, sacado de las MISMAS fórmulas que usa el combate y /profile (CombatStats): ataque base y defensa base crecen con el nivel
        // (el arma y el amuleto suman aparte). Es la diferencia entre el nivel nuevo y el viejo, así que si la fórmula cambia el aviso no se desfasa.
        int attackGained = CombatStats.BaseAttack(newLevel) - CombatStats.BaseAttack(oldLevel);
        int defenseGained = CombatStats.BaseDefense(newLevel) - CombatStats.BaseDefense(oldLevel);

        var fields = new List<LevelUpField>
        {
            new("❤️ Vida máxima", $"**+{LevelingCalculator.HpGainedPerLevel * gained}**", true),
            new("⚔️ Ataque", $"**+{attackGained}**", true),
            new("🛡️ Defensa", $"**+{defenseGained}**", true),
            new("💚 Tu vida", "Curada al máximo", false),
        };

        // Las zonas cuyo nivel mínimo se cruzó con esta subida (la primera, con nivel 1, ya la tiene todo el mundo).
        var unlocked = zones
            .Where(z => z.MinLevel > 1 && z.MinLevel > oldLevel && z.MinLevel <= newLevel)
            .OrderBy(z => z.MinLevel)
            .ThenBy(z => z.ZoneId)
            .ToList();

        if (unlocked.Count > 0)
        {
            string lines = string.Join('\n', unlocked.Select(z =>
                $"{z.Emoji ?? "🗺️"} **Zona {z.ZoneId}: {z.Name}** (nivel {z.MinLevel})"));
            fields.Add(new LevelUpField(
                unlocked.Count > 1 ? "🗺️ ¡Nuevas zonas a tu alcance!" : "🗺️ ¡Una zona nueva a tu alcance!",
                $"{lines}\nMirá cómo entrar con `/zonas`.",
                false));
        }

        return new LevelUpCardContent(title, description, fields, Cheers[random.Next(Cheers.Length)]);
    }
}
