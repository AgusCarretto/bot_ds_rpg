using System.Text.Json;

// La herramienta del idioma (ver LocScan.cs y GameData/Loc.cs):
//   dotnet run --project tools/loc                       el informe: lo que falta en inglés, lo que sobra, los {n} que no coinciden y las llamadas que no son literales
//   dotnet run --project tools/loc -- --check            lo mismo, pero sale con 1 si hay algo (para correr antes de entregar)
//   dotnet run --project tools/loc -- --untranslated <archivo|carpeta>...   los textos para el jugador que todavía no están envueltos en Loc.T / Loc.F
string root = FindRoot();
if (args.Length > 0 && args[0] == "--untranslated")
{
    int count = 0;
    foreach (string path in args.Skip(1))
    {
        foreach (string line in LocScan.Untranslated(Path.IsPathRooted(path) ? path : Path.Combine(root, path)))
        {
            Console.WriteLine(line);
            count++;
        }
    }

    Console.WriteLine($"\n{count} texto(s) sin envolver.");
    return 0;
}

var english = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "Locales", "en.json"))) ?? [];
var uses = LocScan.Extract(root);

// --skeleton <archivo>: escribe las plantillas que faltan (la lista, en orden) para poder traducirlas por posición sin volver a escribir el español.
int skeletonAt = Array.IndexOf(args, "--skeleton");
if (skeletonAt >= 0 && skeletonAt + 1 < args.Length)
{
    var missingTemplates = LocScan.MissingTemplates(uses, english);
    File.WriteAllText(args[skeletonAt + 1], JsonSerializer.Serialize(missingTemplates, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    for (int i = 0; i < missingTemplates.Count; i++)
    {
        Console.WriteLine($"{i}: {missingTemplates[i].Replace("\n", "\\n")}");
    }

    return 0;
}
var report = LocScan.Compare(uses, english);

void Print(string title, IReadOnlyList<string> items)
{
    Console.WriteLine($"{title}: {items.Count}");
    foreach (string item in items.Take(40))
    {
        Console.WriteLine("  " + item);
    }

    if (items.Count > 40)
    {
        Console.WriteLine($"  … y {items.Count - 40} más");
    }
}

Console.WriteLine($"{uses.Count} usos de Loc.T / Loc.F / Loc.Mark y {english.Count} entradas en Locales/en.json.");
Print("Faltan en inglés", report.Missing);
Print("Sobran en el catálogo (el texto cambió)", report.Stale);
Print("Los {n} no coinciden", report.PlaceholderMismatch);
Print("No son literales", report.NonLiteral);
return args.Contains("--check") && !report.Ok ? 1 : 0;

static string FindRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "bot_ds_rpg.csproj")))
        {
            return dir.FullName;
        }
    }

    throw new InvalidOperationException("No encontré bot_ds_rpg.csproj subiendo desde " + Directory.GetCurrentDirectory());
}
