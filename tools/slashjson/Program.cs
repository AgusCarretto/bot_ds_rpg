// Arma el JSON de los comandos slash del bot, el mismo formato que Discord recibe al registrarlos, sin conectarse a
// Discord: lee los módulos con el InteractionService de verdad. Sirve para el "Import from Discord" del panel
// Commands de top.gg. Uso: dotnet run --project tools/slashjson -- [archivo de salida]
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Discord.Interactions;
using Discord.WebSocket;
using System.Reflection;
using Microsoft.Extensions.Configuration;

// El proveedor de servicios del bot de verdad (no se conecta a nada: los módulos solo se describen, no se crean).
var client = new DiscordSocketClient();
var interactions = new InteractionService(client.Rest);
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Postgres:ConnectionString"] = "Host=localhost", ["Discord:Token"] = "x" }).Build();
var sp = (IServiceProvider)typeof(BoxModule).Assembly.GetType("ServiceProviderBuilder")!
    .GetMethod("BuildServiceProvider", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [client, interactions, cfg])!;
await interactions.AddModulesAsync(typeof(BoxModule).Assembly, sp);

var commands = new JsonArray();
foreach (var module in interactions.Modules.Where(m => m.Parent is null))
    AddTopLevel(module, commands);

var ordered = new JsonArray(commands.OrderBy(c => (string)c!["name"]!).Select(c => c!.DeepClone()).ToArray());
foreach (var node in ordered) DropEmptyOptions(node!.AsObject());
string json = ordered.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
if (args.Length > 0) { File.WriteAllText(args[0], json); Console.WriteLine($"{ordered.Count} comandos → {args[0]}"); }
else Console.WriteLine(json);

void AddTopLevel(ModuleInfo module, JsonArray into)
{
    if (module.IsSlashGroup)
    {
        into.Add(new JsonObject
        {
            ["type"] = 1,
            ["name"] = module.SlashGroupName,
            ["description"] = Desc(module.Description, module.SlashGroupName),
            ["options"] = GroupOptions(module),
        });
        return;
    }
    foreach (var cmd in module.SlashCommands)
        into.Add(new JsonObject
        {
            ["type"] = 1,
            ["name"] = cmd.Name,
            ["description"] = Desc(cmd.Description, cmd.Name),
            ["options"] = Params(cmd),
        });
    foreach (var sub in module.SubModules)
        AddTopLevel(sub, into);
}

JsonArray GroupOptions(ModuleInfo group)
{
    var options = new JsonArray();
    foreach (var cmd in group.SlashCommands)
        options.Add(new JsonObject
        {
            ["type"] = 1,
            ["name"] = cmd.Name,
            ["description"] = Desc(cmd.Description, cmd.Name),
            ["options"] = Params(cmd),
        });
    foreach (var sub in group.SubModules)
    {
        if (sub.IsSlashGroup)
            options.Add(new JsonObject
            {
                ["type"] = 2,
                ["name"] = sub.SlashGroupName,
                ["description"] = Desc(sub.Description, sub.SlashGroupName),
                ["options"] = GroupOptions(sub),
            });
        else
            foreach (var node in GroupOptions(sub)) options.Add(node!.DeepClone());
    }
    return options;
}

JsonArray Params(SlashCommandInfo cmd)
{
    var options = new JsonArray();
    foreach (var p in cmd.FlattenedParameters)
    {
        var o = new JsonObject
        {
            ["type"] = (int)(p.DiscordOptionType ?? Discord.ApplicationCommandOptionType.String),
            ["name"] = p.Name,
            ["description"] = Desc(p.Description, p.Name),
            ["required"] = p.IsRequired,
        };
        if (p.IsAutocomplete) o["autocomplete"] = true;
        if (p.Choices.Count > 0)
            o["choices"] = new JsonArray(p.Choices.Select(c => (JsonNode)new JsonObject
            {
                ["name"] = c.Name,
                ["value"] = c.Value switch { string s => JsonValue.Create(s), int i => JsonValue.Create(i), long l => JsonValue.Create(l), double d => JsonValue.Create(d), var v => JsonValue.Create(v?.ToString()) },
            }).ToArray());
        if (p.MinValue is double min) o["min_value"] = min;
        if (p.MaxValue is double max) o["max_value"] = max;
        options.Add(o);
    }
    return options;
}

// Un comando sin parámetros va sin "options" (así lo devuelve Discord).
static void DropEmptyOptions(JsonObject o)
{
    if (o["options"] is not JsonArray options) return;
    if (options.Count == 0) { o.Remove("options"); return; }
    foreach (var child in options) DropEmptyOptions(child!.AsObject());
}

static string Desc(string? description, string name) =>
    string.IsNullOrWhiteSpace(description) ? name : description;
