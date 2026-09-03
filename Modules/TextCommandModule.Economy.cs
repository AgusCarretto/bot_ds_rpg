using BotDsRpg.GameData;
using Discord.Commands;

// Cuarta parte de TextCommandModule (ver el comentario en TextCommandModule.cs): economía
// (tienda, forja, curación/consumibles, casino) y recompensas.
public partial class TextCommandModule
{
    // "aa daily" / "aa d" — misma lógica que DailyModule.HandleDailyAsync.
    [Command("daily")]
    [Alias("d")]
    [Summary("Reclamá tu recompensa diaria (la racha multiplica la recompensa hasta el día 10).")]
    public async Task DailyAsync()
    {
        try
        {
            // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
            await userRepository.GetOrCreateUserAsync(Context.User.Id);

            var outcome = await progressionRepository.ClaimDailyAsync(Context.User.Id);
            var calculation = outcome.Calculation;

            if (calculation.Status == DailyClaimStatus.TooSoon)
            {
                await ReplyAsync(embed: DailyModule.BuildTooSoonEmbed(calculation));
                return;
            }

            await ReplyAsync(embed: DailyModule.BuildResultEmbed(calculation, outcome.Result!));
        }
        catch (Exception)
        {
            await ReplyAsync("¡Upa! No pude procesar tu recompensa diaria, intentá de nuevo en un momento.");
        }
    }

    // "aa shop view" / "aa sv" — misma lógica que ShopModule.HandleViewAsync ("/shop view").
    [Command("shop view")]
    [Alias("sv")]
    [Summary("Mostrá el catálogo de consumibles en venta.")]
    public async Task ShopViewAsync()
    {
        try
        {
            var items = await itemRepository.GetAllByTypeAsync("Consumable");
            await ReplyAsync(embed: ShopModule.BuildViewEmbed(items));
        }
        catch (Exception)
        {
            await ReplyAsync("No pude cargar la tienda ahora mismo, intentá de nuevo en un momento.");
        }
    }

    // "aa shop buy <item>" o "aa shop buy <cantidad> <item...>" (alias "aa sb") — misma lógica que /shop buy.
    [Command("shop buy")]
    [Alias("sb")]
    [Summary("Comprá un consumible de la tienda: \"aa shop buy <item>\" o \"aa shop buy <cantidad> <item>\".")]
    public Task ShopBuyAsync([Remainder] string item) => ShopBuyAsync(1, item);

    [Command("shop buy")]
    [Alias("sb")]
    public async Task ShopBuyAsync(int cantidad, [Remainder] string item)
    {
        try
        {
            var result = await ShopModule.ExecuteBuyAsync(userRepository, itemRepository, shopRepository, Context.User.Id, item, cantidad);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception)
        {
            await ReplyAsync("¡Upa! No pude procesar la compra, intentá de nuevo en un momento.");
        }
    }

    // "aa shop sell <item>" o "aa shop sell <cantidad> <item...>" (alias "aa ss") — misma lógica que /shop sell.
    [Command("shop sell")]
    [Alias("ss")]
    [Summary("Vendé ítems de tu inventario: \"aa shop sell <item>\" o \"aa shop sell <cantidad> <item>\".")]
    public Task ShopSellAsync([Remainder] string item) => ShopSellAsync(1, item);

    [Command("shop sell")]
    [Alias("ss")]
    public async Task ShopSellAsync(int cantidad, [Remainder] string item)
    {
        try
        {
            var result = await ShopModule.ExecuteSellAsync(itemRepository, shopRepository, Context.User.Id, item, cantidad);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception)
        {
            await ReplyAsync("¡Upa! No pude procesar la venta, intentá de nuevo en un momento.");
        }
    }

    // "aa shop sellall" / "aa sa" — misma lógica que /shop sellall.
    [Command("shop sellall")]
    [Alias("sa")]
    [Summary("Vendé todo tu inventario de una sola vez a cambio de oro.")]
    public async Task ShopSellAllAsync()
    {
        try
        {
            var result = await ShopModule.ExecuteSellAllAsync(shopRepository, Context.User.Id);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception)
        {
            await ReplyAsync("¡Upa! No pude procesar la venta, intentá de nuevo en un momento.");
        }
    }

    // "aa forge recipes" / "aa fr" — misma lógica que /forge recipes.
    [Command("forge recipes")]
    [Alias("fr")]
    [Summary("Mostrá las recetas de forja disponibles.")]
    public async Task ForgeRecipesAsync()
    {
        try
        {
            // Si es la primera vez que este usuario ejecuta un comando, se crea acá con los valores por defecto.
            var player = await userRepository.GetOrCreateUserAsync(Context.User.Id);
            await ReplyAsync(embed: await ForgeModule.BuildRecipesEmbed(recipeRepository, player.Class));
        }
        catch (Exception)
        {
            await ReplyAsync("No pude cargar las recetas ahora mismo, intentá de nuevo en un momento.");
        }
    }

    // "aa forge make <item>" / "aa fm <item>" — misma lógica que /forge make.
    [Command("forge make")]
    [Alias("fm")]
    [Summary("Pagale al herrero para forjar un ítem de las recetas conocidas.")]
    public async Task ForgeMakeAsync([Remainder] string item)
    {
        try
        {
            var result = await ForgeModule.ExecuteMakeAsync(userRepository, recipeRepository, craftingRepository, Context.User.Id, item);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception)
        {
            await ReplyAsync("¡Upa! Algo falló en la herrería, intentá de nuevo en un momento.");
        }
    }

    // "aa equip <item>" / "aa eq <item>" — misma lógica que /equip.
    [Command("equip")]
    [Alias("eq")]
    [Summary("Equipate un arma o amuleto que tengas en tu inventario.")]
    public async Task EquipAsync([Remainder] string item)
    {
        try
        {
            var result = await EquipModule.ExecuteEquipAsync(userRepository, itemRepository, inventoryRepository, Context.User.Id, item);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception)
        {
            await ReplyAsync("¡Upa! No pude equipar ese ítem, intentá de nuevo en un momento.");
        }
    }

    // "aa heal" / "aa he" — misma lógica que /heal. No funciona en combate (gasta oro).
    [Command("heal")]
    [Alias("he")]
    [Summary("Pagá oro por algo de comer y recuperá HP (10 de oro = 30 HP). No funciona en combate.")]
    public async Task HealAsync()
    {
        try
        {
            var embed = await TavernModule.ExecuteHealAsync(userRepository, combatSessions, Context.User.Id);
            await ReplyAsync(embed: embed);
        }
        catch (Exception)
        {
            await ReplyAsync("¡Upa! No pude procesar la curación, intentá de nuevo en un momento.");
        }
    }

    // "aa use <item>" / "aa u <item>" — misma lógica que /use. A diferencia de "aa heal", sí
    // funciona en combate (no gasta oro, solo un consumible del inventario).
    [Command("use")]
    [Alias("u")]
    [Summary("Usá un consumible de tu inventario para curar HP (funciona incluso en combate, sin gastar oro).")]
    public async Task UseAsync([Remainder] string item)
    {
        try
        {
            var result = await UseModule.ExecuteUseAsync(userRepository, itemRepository, inventoryRepository, combatSessions, Context.User.Id, item);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception)
        {
            await ReplyAsync("¡Upa! No pude usar ese ítem, intentá de nuevo en un momento.");
        }
    }

    // "aa play <coinflip|slots> <apuesta> [heads|tails]" / "aa pl ..." — misma lógica que /play.
    [Command("play")]
    [Alias("pl")]
    [Summary("Apostá tu oro en el casino: \"aa play coinflip <apuesta> <heads|tails>\" o \"aa play slots <apuesta>\".")]
    public async Task PlayAsync(string game, int apuesta, string? lado = null)
    {
        try
        {
            var result = await CasinoModule.ExecutePlayAsync(userRepository, casinoRepository, casinoService, Context.User.Id, game, apuesta, lado);
            await ReplyAsync(result.PlainMessage, embed: result.Embed);
        }
        catch (Exception)
        {
            await ReplyAsync("¡Upa! Algo falló en el casino, intentá de nuevo en un momento.");
        }
    }
}
