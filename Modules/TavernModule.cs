using BotDsRpg.GameData;
using BotDsRpg.Repositories;
using BotDsRpg.Services;
using Discord;
using Discord.Interactions;

// /heal ya no cuesta oro directo: rework a pedido para que curarse SIEMPRE dependa de tener algo
// comprado en la tienda (ver Modules/ShopModule.cs). Consume automáticamente el Consumable más
// barato que el jugador tenga en inventario (para no desperdiciar uno caro en una curación chica)
// — si no tiene ninguno, lo manda a comprar primero. Sigue bloqueado en combate por la misma razón
// de siempre: comer tranquilo no debería ser gratis en pleno combate, para eso está /use (cede el
// turno al monstruo, ver Modules/UseModule.cs).
public class TavernModule(IUserRepository userRepository, IInventoryRepository inventoryRepository, ICombatSessionService combatSessions)
    : InteractionModuleBase<SocketInteractionContext>
{
    // Comando barra: /heal
    [SlashCommand("heal", "Comé un consumible de tu inventario para recuperar HP (comprado antes en /shop). No funciona en combate.")]
    public async Task HandleHealAsync()
    {
        await DeferAsync();

        try
        {
            var embed = await ExecuteHealAsync(userRepository, inventoryRepository, combatSessions, Context.User.Id);
            await FollowupAsync(embed: embed);
        }
        catch (Exception ex)
        {
            // Si la base falla o algo inesperado ocurre, avisamos sin tirar abajo el bot — pero
            // logueamos la excepción real (antes se tragaba en silencio, imposible de diagnosticar).
            Console.WriteLine($"[EXCEPCIÓN /heal] {ex}");
            await FollowupAsync("¡Upa! No pude procesar la curación, intentá de nuevo en un momento.", ephemeral: true);
        }
    }

    // Estático (sin dependencia de Context) para que Modules/TextCommandModule.cs comparta
    // exactamente la misma lógica en "aa heal".
    public static async Task<Embed> ExecuteHealAsync(
        IUserRepository userRepository, IInventoryRepository inventoryRepository, ICombatSessionService combatSessions, ulong discordId)
    {
        if (combatSessions.Peek(discordId) is not null)
        {
            return new EmbedBuilder()
                .WithTitle("⚔️ No podés comer tranquilo en pleno combate")
                .WithDescription("Mientras estás peleando no podés parar a comer así nomás. Usá **/use <ítem>** para curarte con un consumible de tu inventario (te va a costar el turno).")
                .WithColor(Color.DarkGrey)
                .Build();
        }

        var player = await userRepository.GetOrCreateUserAsync(discordId);

        if (player.CurrentHp >= player.MaxHp)
        {
            return new EmbedBuilder()
                .WithTitle("❤️ Ya estás al máximo")
                .WithDescription($"Tenés {player.CurrentHp}/{player.MaxHp} HP, no necesitás curarte todavía.")
                .WithColor(Color.Green)
                .Build();
        }

        // Ordenado por buy_price ascendente (ver InventoryRepository.GetOwnedByTypeAsync): el
        // primero es el más barato que tiene, así una curación chica no gasta el consumible caro.
        var owned = await inventoryRepository.GetOwnedByTypeAsync(discordId, "Consumable");

        if (owned.Count == 0)
        {
            return new EmbedBuilder()
                .WithTitle("🍽️ No tenés nada para comer")
                .WithDescription("Para curarte primero tenés que comprar un consumible. Usá **/shop view** para ver el catálogo y **/shop buy <ítem>** para comprarlo.")
                .WithColor(Color.Red)
                .Build();
        }

        var chosen = owned[0].Item;

        if (!await inventoryRepository.TryConsumeAsync(discordId, chosen.ItemId, 1))
        {
            // Perdió la carrera contra otra acción que gastó el mismo ítem casi al mismo tiempo
            // (ej. /use o /shop sell disparados casi en simultáneo). Rarísimo, pero posible.
            return new EmbedBuilder()
                .WithTitle("😅 Justo se gastó")
                .WithDescription($"Justo se te fue **{ItemDisplay.Format(chosen.Emoji, chosen.Name)}** por otra acción, probá de nuevo en un toque.")
                .WithColor(Color.DarkGrey)
                .Build();
        }

        var healed = await userRepository.RestoreHpAsync(discordId, chosen.StatValue);

        return new EmbedBuilder()
            .WithTitle("🍖 ¡Buen provecho!")
            .WithDescription($"Comiste **{ItemDisplay.Format(chosen.Emoji, chosen.Name)}** y recuperaste HP. Ahora tenés **{healed.CurrentHp}/{healed.MaxHp}** HP.")
            .WithColor(Color.Green)
            .Build();
    }
}
