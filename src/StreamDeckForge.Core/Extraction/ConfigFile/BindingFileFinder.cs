using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Extraction.ConfigFile;

/// <summary>
/// Recherche generique de fichiers de raccourcis, pour les logiciels qu'aucun parseur
/// ne reconnait par son nom. On ratisse le dossier d'installation et les dossiers de
/// preferences dont le nom evoque l'application, puis on laisse chaque parseur tenter
/// sa chance sur les fichiers trouves : c'est le contenu qui tranche, pas le nom.
/// </summary>
public static class BindingFileFinder
{
    /// <summary>Noms de fichiers qui, dans l'ecosysteme Windows, portent des raccourcis.</summary>
    private static readonly string[] Patterns =
    [
        "*.keys",
        "*.bindings",
        "*.keymap",
        "*.sublime-keymap",
        "keybindings.json",
        "shortcuts.xml",
        "keymap*.xml"
    ];

    private const int MaxInstallDepth = 4;
    private const int MaxPreferencesDepth = 3;
    private const int MaxFiles = 60;

    /// <summary>Longueur minimale d'un nom pour servir de filtre, sous peine de tout matcher.</summary>
    private const int MinNameLength = 4;

    public static List<string> Find(AppInfo application)
    {
        var files = new List<string>();

        var installDirectory = Path.GetDirectoryName(application.ExecutablePath ?? string.Empty);
        if (!string.IsNullOrEmpty(installDirectory) && Directory.Exists(installDirectory))
            files.AddRange(FilesIn(installDirectory, MaxInstallDepth));

        foreach (var directory in PreferenceDirectories(application))
            files.AddRange(FilesIn(directory, MaxPreferencesDepth));

        return files
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxFiles)
            .ToList();
    }

    /// <summary>
    /// Dossiers de preferences dont le nom ressemble a celui de l'application :
    /// "Mixbus12" pour mixbus12.exe, "Code" pour Code.exe, etc.
    /// </summary>
    private static IEnumerable<string> PreferenceDirectories(AppInfo application)
    {
        var needles = NameCandidates(application);
        if (needles.Count == 0)
            yield break;

        foreach (var root in PreferenceRoots())
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                continue;

            List<string> children;

            try
            {
                children = Directory.EnumerateDirectories(root).ToList();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                var name = Normalize(Path.GetFileName(child));

                if (needles.Any(needle =>
                        name.Contains(needle, StringComparison.Ordinal) ||
                        needle.Contains(name, StringComparison.Ordinal) && name.Length >= MinNameLength))
                    yield return child;
            }
        }
    }

    private static IEnumerable<string> PreferenceRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
            yield return Path.Combine(profile, ".config");
    }

    /// <summary>
    /// Deux pistes pour reconnaitre le dossier : le nom de l'executable et le nom
    /// affiche du logiciel, tous deux reduits aux lettres et chiffres.
    /// </summary>
    private static List<string> NameCandidates(AppInfo application)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(application.ExecutablePath))
        {
            var stem = Normalize(Path.GetFileNameWithoutExtension(application.ExecutablePath));

            if (stem.Length >= MinNameLength)
                candidates.Add(stem);

            // "mixbus12" -> "mixbus" : le dossier de preferences porte parfois la
            // version, parfois non.
            var withoutDigits = new string(stem.Where(char.IsLetter).ToArray());
            if (withoutDigits.Length >= MinNameLength && withoutDigits != stem)
                candidates.Add(withoutDigits);
        }

        var displayName = Normalize(application.Name);
        if (displayName.Length >= MinNameLength)
            candidates.Add(displayName);

        return candidates.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static List<string> FilesIn(string directory, int maxDepth)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = maxDepth,
            IgnoreInaccessible = true
        };

        var files = new List<string>();

        foreach (var pattern in Patterns)
        {
            try
            {
                files.AddRange(Directory.EnumerateFiles(directory, pattern, options).Take(MaxFiles));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            if (files.Count >= MaxFiles)
                break;
        }

        return files;
    }
}
