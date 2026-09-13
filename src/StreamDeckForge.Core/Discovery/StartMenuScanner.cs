using System.Collections.Concurrent;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Discovery;

/// <summary>
/// Enumere les raccourcis .lnk des deux menus Demarrer (machine et utilisateur) et en
/// deduit les executables cibles. Complete le registre, qui ignore les applications
/// portables et celles qui ne s'enregistrent pas dans la table de desinstallation.
/// </summary>
public sealed class StartMenuScanner
{
    /// <summary>Cibles a ecarter : ce sont des documents ou des lanceurs sans interet.</summary>
    private static readonly string[] IgnoredTargets =
    [
        "rundll32.exe", "msiexec.exe", "control.exe", "cmd.exe", "wscript.exe", "cscript.exe"
    ];

    public IReadOnlyList<AppInfo> Scan(CancellationToken cancellationToken = default)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
        };

        var shortcuts = new List<string>(1024);
        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)))
            shortcuts.AddRange(EnumerateShortcuts(root));

        var found = new ConcurrentBag<AppInfo>();

        Parallel.ForEach(
            shortcuts,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            },
            lnk =>
            {
                var app = ReadShortcut(lnk);
                if (app is not null)
                    found.Add(app);
            });

        return found.ToList();
    }

    private static IEnumerable<string> EnumerateShortcuts(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System
        };

        try
        {
            return Directory.EnumerateFiles(root, "*.lnk", options).ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static AppInfo? ReadShortcut(string lnkPath)
    {
        var link = ShellLinkReader.TryRead(lnkPath);
        var target = link?.TargetPath;

        if (string.IsNullOrWhiteSpace(target) ||
            !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return null;

        var fileName = Path.GetFileName(target);
        if (IgnoredTargets.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            return null;

        if (!File.Exists(target))
            return null;

        return new AppInfo
        {
            Name = Path.GetFileNameWithoutExtension(lnkPath),
            ExecutablePath = target,
            IconPath = target,
            InstallLocation = Path.GetDirectoryName(target),
            Source = AppSource.StartMenu
        };
    }
}
