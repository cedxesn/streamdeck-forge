using Microsoft.Win32;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Discovery;

/// <summary>
/// Parcourt les cles de desinstallation (HKLM 64 bits, HKLM 32 bits, HKCU) et la table
/// "App Paths" pour dresser la liste des logiciels installes.
/// </summary>
public sealed class RegistryApplicationScanner
{
    private const string UninstallKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private const string AppPathsKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    /// <summary>Chemins d'executables indexes par nom de fichier ("code.exe" -> chemin complet).</summary>
    public IReadOnlyDictionary<string, string> AppPaths { get; private set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AppInfo> Scan(CancellationToken cancellationToken = default)
    {
        AppPaths = ReadAppPaths();

        var results = new List<AppInfo>(512);

        foreach (var (hive, view) in Hives())
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.AddRange(ReadUninstallEntries(hive, view, cancellationToken));
        }

        return results;
    }

    private static IEnumerable<(RegistryHive Hive, RegistryView View)> Hives()
    {
        yield return (RegistryHive.LocalMachine, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, RegistryView.Registry32);
        yield return (RegistryHive.CurrentUser, RegistryView.Registry64);
    }

    private List<AppInfo> ReadUninstallEntries(
        RegistryHive hive,
        RegistryView view,
        CancellationToken cancellationToken)
    {
        var results = new List<AppInfo>();

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(UninstallKey);
            if (uninstall is null)
                return results;

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var entry = uninstall.OpenSubKey(subKeyName);
                if (entry is null)
                    continue;

                var app = ReadEntry(entry);
                if (app is not null)
                    results.Add(app);
            }
        }
        catch (System.Security.SecurityException)
        {
            // Ruche inaccessible : on ignore, les autres sources prennent le relais.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return results;
    }

    private AppInfo? ReadEntry(RegistryKey entry)
    {
        var name = entry.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(name))
            return null;

        // Mises a jour, correctifs et composants systeme : masques par l'applet
        // "Applications installees", on applique le meme filtre.
        if (entry.GetValue("SystemComponent") is int systemComponent && systemComponent == 1)
            return null;

        if (entry.GetValue("ParentKeyName") is string parent && !string.IsNullOrWhiteSpace(parent))
            return null;

        if (entry.GetValue("ReleaseType") is string releaseType &&
            releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase))
            return null;

        var installLocation = Clean(entry.GetValue("InstallLocation") as string);
        var displayIcon = Clean(entry.GetValue("DisplayIcon") as string);
        var executable = ExecutableFromDisplayIcon(displayIcon) ?? ExecutableFromAppPaths(name);

        return new AppInfo
        {
            Name = name.Trim(),
            ExecutablePath = executable,
            IconPath = executable ?? StripIconIndex(displayIcon),
            Publisher = Clean(entry.GetValue("Publisher") as string),
            Version = Clean(entry.GetValue("DisplayVersion") as string),
            InstallLocation = installLocation,
            Source = AppSource.Registry
        };
    }

    /// <summary>DisplayIcon vaut souvent "C:\Program Files\App\app.exe,0".</summary>
    private static string? ExecutableFromDisplayIcon(string? displayIcon)
    {
        var path = StripIconIndex(displayIcon);
        if (path is null)
            return null;

        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path)
            ? path
            : null;
    }

    private static string? StripIconIndex(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
            return null;

        var value = displayIcon.Trim().Trim('"');
        var comma = value.LastIndexOf(',');
        if (comma > 2 && int.TryParse(value[(comma + 1)..].Trim(), out _))
            value = value[..comma].Trim().Trim('"');

        return Environment.ExpandEnvironmentVariables(value);
    }

    private string? ExecutableFromAppPaths(string displayName)
    {
        // "Visual Studio Code" -> visualstudiocode / code : on tente une correspondance
        // simple sur le nom de fichier enregistre dans App Paths.
        var compact = new string(displayName.Where(char.IsLetterOrDigit).ToArray());
        if (compact.Length == 0)
            return null;

        foreach (var (fileName, fullPath) in AppPaths)
        {
            var stem = new string(
                Path.GetFileNameWithoutExtension(fileName).Where(char.IsLetterOrDigit).ToArray());

            if (stem.Length >= 4 &&
                compact.Contains(stem, StringComparison.OrdinalIgnoreCase))
                return fullPath;
        }

        return null;
    }

    private static Dictionary<string, string> ReadAppPaths()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (hive, view) in Hives())
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var appPaths = baseKey.OpenSubKey(AppPathsKey);
                if (appPaths is null)
                    continue;

                foreach (var exeName in appPaths.GetSubKeyNames())
                {
                    using var exeKey = appPaths.OpenSubKey(exeName);
                    var path = Clean(exeKey?.GetValue(null) as string)?.Trim('"');
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    path = Environment.ExpandEnvironmentVariables(path);
                    if (File.Exists(path))
                        map.TryAdd(exeName, path);
                }
            }
            catch (System.Security.SecurityException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return map;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
