using System.Text.RegularExpressions;

// Ayudas compartidas de los arneses de tests/ y tools/ (cada proyecto la incluye con un Link desde tests/Directory.Build.props).
internal static class TestEnv
{
    // La carpeta del repo (donde está bot_ds_rpg.csproj), subiendo desde donde corre el arnés.
    public static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "bot_ds_rpg.csproj")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"No encontré bot_ds_rpg.csproj subiendo desde {AppContext.BaseDirectory}.");
    }

    // La cadena de conexión de la base LOCAL de desarrollo. De más a menos prioridad: la variable Postgres__ConnectionString, el .env de la variable HARNESS_ENV_FILE, el .env de la raíz del repo y,
    // si esto es un git worktree (sin .env propio porque está en .gitignore), el .env de la carpeta principal del repo.
    public static string Connection()
    {
        string? conn = Environment.GetEnvironmentVariable("Postgres__ConnectionString");
        if (string.IsNullOrWhiteSpace(conn))
        {
            conn = File.ReadAllLines(EnvFile())
                .Where(l => l.StartsWith("Postgres__ConnectionString=", StringComparison.Ordinal))
                .Select(l => l["Postgres__ConnectionString=".Length..].Trim().Trim('"'))
                .FirstOrDefault()
                ?? throw new InvalidOperationException($"El archivo {EnvFile()} no tiene Postgres__ConnectionString.");
        }

        GuardLocal(conn);
        return conn;
    }

    public static string EnvFile()
    {
        foreach (var candidate in Candidates())
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("No encontré el .env de la base local. Poné Postgres__ConnectionString como variable de entorno o apuntá HARNESS_ENV_FILE a un .env (ver tests/README.md).");
    }

    private static IEnumerable<string?> Candidates()
    {
        yield return Environment.GetEnvironmentVariable("HARNESS_ENV_FILE");
        yield return Path.Combine(RepoRoot, ".env");

        // En un git worktree, ".git" es un archivo que dice "gitdir: <principal>/.git/worktrees/<nombre>": el .env vive en la carpeta principal.
        string gitFile = Path.Combine(RepoRoot, ".git");
        if (File.Exists(gitFile))
        {
            string line = File.ReadAllLines(gitFile).FirstOrDefault(l => l.StartsWith("gitdir:", StringComparison.Ordinal)) ?? string.Empty;
            string gitDir = line["gitdir:".Length..].Trim();
            var main = Directory.GetParent(gitDir)?.Parent?.Parent; // .../worktrees/<nombre> -> .../.git/worktrees -> .../.git -> <principal>
            if (main is not null)
            {
                yield return Path.Combine(main.FullName, ".env");
            }
        }
    }

    // Los arneses crean y borran usuarios descartables (ids 999000000000xxxxx) y varios tocan el catálogo (items, zone_boxes...) un instante: SOLO contra una base LOCAL. Nunca contra Railway.
    private static void GuardLocal(string conn)
    {
        string host = Regex.Match(conn, @"(?i)(?:^|;)\s*(?:Server|Host)\s*=\s*([^;]+)").Groups[1].Value.Trim();
        bool local = host.Length == 0 || host is "localhost" or "127.0.0.1" or "::1";
        if (!local && Environment.GetEnvironmentVariable("HARNESS_ALLOW_REMOTE") != "1")
        {
            throw new InvalidOperationException(
                $"Me niego a correr contra la base «{host}»: los arneses escriben y borran datos. Usá una base LOCAL (ver tests/README.md), o HARNESS_ALLOW_REMOTE=1 si sabés lo que hacés.");
        }
    }
}
