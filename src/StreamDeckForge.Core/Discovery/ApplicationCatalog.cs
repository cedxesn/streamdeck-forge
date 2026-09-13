using System.Diagnostics;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Discovery;

/// <summary>Resultat d'un scan machine, avec la mesure de duree exigee par le cahier des charges.</summary>
public sealed record CatalogScanResult(IReadOnlyList<AppInfo> Applications, TimeSpan Duration);

/// <summary>
/// Fusionne les sources de decouverte (registre + menu Demarrer) en une liste unique,
/// dedupliquee et triee. Les deux scans tournent en parallele : l'un est domine par
/// des acces registre, l'autre par des acces disque.
/// </summary>
public sealed class ApplicationCatalog
{
    private readonly RegistryApplicationScanner _registryScanner = new();
    private readonly StartMenuScanner _startMenuScanner = new();

    public async Task<CatalogScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var registryTask = Task.Run(() => _registryScanner.Scan(cancellationToken), cancellationToken);
        var startMenuTask = Task.Run(() => _startMenuScanner.Scan(cancellationToken), cancellationToken);

        await Task.WhenAll(registryTask, startMenuTask).ConfigureAwait(false);

        var merged = Merge(registryTask.Result, startMenuTask.Result);
        stopwatch.Stop();

        return new CatalogScanResult(merged, stopwatch.Elapsed);
    }

    /// <summary>
    /// Les entrees du menu Demarrer pointent sur un executable : elles servent a completer
    /// le chemin manquant d'une entree registre portant un nom proche, sinon elles sont
    /// ajoutees telles quelles.
    /// </summary>
    private static List<AppInfo> Merge(
        IReadOnlyList<AppInfo> fromRegistry,
        IReadOnlyList<AppInfo> fromStartMenu)
    {
        var byKey = new Dictionary<string, AppInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in fromRegistry)
            byKey.TryAdd(app.DedupKey, app);

        foreach (var app in fromStartMenu)
        {
            if (byKey.ContainsKey(app.DedupKey))
                continue;

            var completed = FindRegistryEntryMissingPath(byKey.Values, app);
            if (completed is not null)
            {
                completed.ExecutablePath = app.ExecutablePath;
                completed.IconPath ??= app.IconPath;
                continue;
            }

            byKey.TryAdd(app.DedupKey, app);
        }

        return byKey.Values
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static AppInfo? FindRegistryEntryMissingPath(
        IEnumerable<AppInfo> candidates,
        AppInfo startMenuEntry)
    {
        var linkName = Normalize(startMenuEntry.Name);
        if (linkName.Length < 4)
            return null;

        return candidates.FirstOrDefault(candidate =>
            candidate.ExecutablePath is null &&
            Normalize(candidate.Name).Contains(linkName, StringComparison.Ordinal));
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>Filtre de recherche par nom, editeur ou nom de fichier.</summary>
    public static IEnumerable<AppInfo> Search(IEnumerable<AppInfo> applications, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return applications;

        var needle = query.Trim();

        return applications.Where(app =>
            app.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            (app.Publisher?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (app.ExecutablePath?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    /// <summary>
    /// Cherche tardivement l'executable d'une entree registre qui n'en declare pas :
    /// on n'explore le dossier d'installation qu'au moment ou l'utilisateur selectionne
    /// l'application, pour ne pas peser sur la duree du scan.
    /// </summary>
    public static string? ResolveExecutable(AppInfo app)
    {
        if (app.HasExecutable)
            return app.ExecutablePath;

        if (string.IsNullOrWhiteSpace(app.InstallLocation) || !Directory.Exists(app.InstallLocation))
            return null;

        try
        {
            var candidates = Directory
                .EnumerateFiles(app.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly)
                .ToList();

            if (candidates.Count == 0)
                return null;

            // Le meilleur candidat est celui dont le nom ressemble le plus a celui du logiciel.
            var target = Normalize(app.Name);
            var best = candidates
                .OrderByDescending(path =>
                    target.Contains(Normalize(Path.GetFileNameWithoutExtension(path)),
                        StringComparison.Ordinal))
                .ThenByDescending(path => new FileInfo(path).Length)
                .First();

            app.ExecutablePath = best;
            app.IconPath ??= best;
            return best;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Construit une entree a partir d'un .exe designe manuellement par l'utilisateur.</summary>
    public static AppInfo FromExecutable(string executablePath)
    {
        var full = Path.GetFullPath(executablePath);

        return new AppInfo
        {
            Name = FileVersionInfoName(full) ?? Path.GetFileNameWithoutExtension(full),
            ExecutablePath = full,
            IconPath = full,
            InstallLocation = Path.GetDirectoryName(full),
            Source = AppSource.Manual
        };
    }

    private static string? FileVersionInfoName(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var name = info.FileDescription ?? info.ProductName;
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
