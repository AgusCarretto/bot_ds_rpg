using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /professions (aa professions|oficios|profesiones|prof): los oficios (v0.13.0, GameData/ProfessionRules.cs). Leñador, Minero y Encantador suben usando /chop, /mine y /enchant; cada nivel
// da un poco más y al 100 se desbloquea la versión avanzada del comando. Esta pantalla solo MUESTRA: la XP sale de los contadores (PlayerBonusService), no se guarda en ningún lado.
public class ProfessionModule(IUserRepository userRepository, IPlayerBonusService bonusService) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("professions", "Tus oficios (Leñador, Minero, Encantador): nivel, XP y lo que dan.")]
    public async Task HandleProfessionsAsync()
    {
        // Efímero: es tu avance, no hace falta mostrárselo al canal.
        await DeferAsync(ephemeral: true);

        try
        {
            var result = await ExecuteProfessionsAsync(userRepository, bonusService, Context.User.Id);
            await FollowupAsync(result.PlainMessage, embed: result.Embed, ephemeral: true);
        }
        catch (Exception ex)
        {
            BotLog.Error(ex);
            await FollowupAsync("¡Upa! No pude abrir tus oficios, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Exactamente uno de los dos campos viene con valor (mismo patrón que BankModule.BankResult).
    public sealed record ProfessionResult(string? PlainMessage, Embed? Embed);

    // Estático (sin Context) para que "aa professions" haga exactamente lo mismo.
    public static async Task<ProfessionResult> ExecuteProfessionsAsync(IUserRepository userRepository, IPlayerBonusService bonusService, ulong discordId)
    {
        var player = await userRepository.GetByDiscordIdAsync(discordId);
        if (player is null)
        {
            return new ProfessionResult("Todavía no tenés cuenta: empezá con **/start**.", null);
        }

        var bonuses = await bonusService.GetAsync(discordId, player.FuegoNuevo);
        return new ProfessionResult(null, BuildEmbed(bonuses));
    }

    // Público y puro: se prueba sin Discord. Un campo por oficio, apilados (nada en columnas).
    public static Embed BuildEmbed(PlayerBonuses bonuses)
    {
        var embed = new EmbedBuilder()
            .WithTitle("🛠️ Tus oficios")
            .WithColor(Color.Teal)
            .WithDescription(
                "Cada vez que usás el comando de un oficio, ese oficio sube de nivel. Cada nivel da un poco más, " +
                $"y al **nivel {ProfessionRules.MaxLevel}** se desbloquea la versión avanzada del comando. Los oficios **se quedan con cada Fuego Nuevo**.");

        foreach (var profession in ProfessionCatalog.All)
        {
            embed.AddField(FieldTitle(profession, bonuses), FieldText(profession, bonuses), false);
        }

        return embed
            .WithFooter($"Llegar al {ProfessionRules.MaxLevel} lleva ~{GameHistory.Number(ProfessionRules.TotalXpToMax)} de XP. Lo que ya hiciste antes de que existieran los oficios también cuenta.")
            .Build();
    }

    private static string FieldTitle(ProfessionDefinition profession, PlayerBonuses bonuses)
    {
        var progress = ProfessionRules.ProgressFor(bonuses.ProfessionXpOf(profession.Key));
        return $"{profession.Emoji} {profession.Name} · nivel {progress.Level}/{ProfessionRules.MaxLevel}";
    }

    private static string FieldText(ProfessionDefinition profession, PlayerBonuses bonuses)
    {
        var progress = ProfessionRules.ProgressFor(bonuses.ProfessionXpOf(profession.Key));
        string bar = progress.IsMax
            ? "🏆 **¡Nivel máximo!**"
            : $"{ProgressBar.Render((int)progress.IntoLevel, (int)progress.ToNext)} {progress.IntoLevel}/{progress.ToNext} XP";

        string now = progress.Level == 0
            ? $"Cada nivel da {ProfessionRules.DescribePerLevel(profession)}."
            : $"Hoy da {ProfessionRules.DescribeEffect(profession, progress.Level)}.";

        string advanced = ProfessionRules.AdvancedUnlocked(progress.Level)
            ? $"🔓 {ProfessionRules.DescribeAdvanced(profession)}"
            : $"🔒 Al {ProfessionRules.MaxLevel}: {ProfessionRules.DescribeAdvanced(profession)}";

        return $"{bar}\n{now}\n{advanced}\n_Sube con {profession.Command} ({profession.XpPerAction} XP por uso)._";
    }
}
