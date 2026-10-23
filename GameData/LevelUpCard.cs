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
        Loc.Mark("Se nota que le pusiste ganas."),
        Loc.Mark("El asado y el acero te están haciendo bien."),
        Loc.Mark("Los monstruos ya te empiezan a respetar."),
        Loc.Mark("Un mate para festejar... y a seguir."),
        Loc.Mark("Cada nivel te acerca un poco más al Soberano."),
        Loc.Mark("¡Qué crack! Seguí así."),
        Loc.Mark("Así se hace: paso firme y mano en el acero."),
    ];

    // hpMultiplier: el factor con el que SE VE la vida del jugador (GameData/HpDisplay.cs): la vida máxima que sube por nivel se dice en esas unidades, así el «+N» coincide con lo que se mueve la barra (un Ninja
    // ve +14 y no +15). Sin dato (1,0) queda la vida real, como antes.
    public static LevelUpCardContent Compose(ulong discordId, int newLevel, int levelsGained, IReadOnlyList<Zone> zones, Random? random = null, double hpMultiplier = 1.0)
    {
        random ??= Random.Shared;

        int oldLevel = Math.Max(1, newLevel - Math.Max(1, levelsGained));
        int gained = newLevel - oldLevel;

        string title = gained > 1 ? Loc.F($"🎉 ¡SUBISTE {gained} NIVELES! 🎉") : Loc.T("🎉 ¡SUBISTE DE NIVEL! 🎉");

        string description =
            Loc.F($"🌟 **¡<@{discordId}> ahora es nivel {newLevel}!** 🌟\n\n") +
            (UiEmojis.Lv is { } lv ? $"⬆️ {lv} {oldLevel}  ➜  {lv} **{newLevel}**" : Loc.F($"⬆️ Nivel {oldLevel}  ➜  **Nivel {newLevel}**"));

        // Lo que sube de verdad, sacado de las MISMAS fórmulas que usa el combate y /profile (CombatStats): ataque base y defensa base crecen con el nivel
        // (el arma y el amuleto suman aparte). Es la diferencia entre el nivel nuevo y el viejo, así que si la fórmula cambia el aviso no se desfasa.
        int attackGained = CombatStats.BaseAttack(newLevel) - CombatStats.BaseAttack(oldLevel);
        int defenseGained = CombatStats.BaseDefense(newLevel) - CombatStats.BaseDefense(oldLevel);

        var fields = new List<LevelUpField>
        {
            new(Loc.T("❤️ Vida máxima"), $"**+{HpDisplay.Scale(LevelingCalculator.HpGainedPerLevel * gained, hpMultiplier)}**", true),
            new(Loc.T("⚔️ Ataque"), $"**+{attackGained}**", true),
            new(Loc.T("🛡️ Defensa"), $"**+{defenseGained}**", true),
            new(Loc.T("💚 Tu vida"), Loc.T("Curada al máximo"), false),
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
                Loc.F($"{z.Emoji ?? "🗺️"} **Zona {z.ZoneId}: {z.Name}** (nivel {z.MinLevel})")));
            fields.Add(new LevelUpField(
                unlocked.Count > 1 ? Loc.T("🗺️ ¡Nuevas zonas a tu alcance!") : Loc.T("🗺️ ¡Una zona nueva a tu alcance!"),
                Loc.F($"{lines}\nMirá cómo entrar con `/zonas`."),
                false));
        }

        return new LevelUpCardContent(title, description, fields, Loc.Tr(Cheers[random.Next(Cheers.Length)]));
    }
}
