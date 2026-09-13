using System.Text;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Extraction.ConfigFile;

/// <summary>
/// Parseur dedie au format de configuration d'un logiciel donne. Chaque parseur sait
/// reconnaitre l'application et localiser ses fichiers de raccourcis.
/// </summary>
public interface IKeybindingFileParser
{
    /// <summary>Nom du logiciel couvert, affiche dans le compte rendu.</summary>
    string ProductName { get; }

    /// <summary>Fichiers de configuration presents sur la machine pour cette application.</summary>
    IReadOnlyList<string> LocateFiles(AppInfo application);

    /// <summary>Extrait les raccourcis d'un fichier localise par <see cref="LocateFiles"/>.</summary>
    IReadOnlyList<ShortcutDefinition> Parse(string path);
}

/// <summary>Utilitaires partages par les parseurs.</summary>
public static class ConfigFileHelpers
{
    /// <summary>
    /// Retire les commentaires // et les virgules finales d'un JSON "avec commentaires",
    /// dialecte utilise par VS Code et Sublime Text que System.Text.Json accepte ensuite.
    /// </summary>
    public static string StripJsonComments(string content)
    {
        var output = new StringBuilder(content.Length);
        var inString = false;
        var escaped = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inString)
            {
                output.Append(c);

                if (escaped)
                    escaped = false;
                else if (c == '\\')
                    escaped = true;
                else if (c == '"')
                    inString = false;

                continue;
            }

            if (c == '"')
            {
                inString = true;
                output.Append(c);
                continue;
            }

            if (c == '/' && i + 1 < content.Length)
            {
                if (content[i + 1] == '/')
                {
                    while (i < content.Length && content[i] != '\n')
                        i++;

                    output.Append('\n');
                    continue;
                }

                if (content[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < content.Length && !(content[i] == '*' && content[i + 1] == '/'))
                        i++;

                    i++;
                    continue;
                }
            }

            output.Append(c);
        }

        return output.ToString();
    }

    /// <summary>Chemins %APPDATA% correspondant a un motif de dossier ("Sublime Text*").</summary>
    public static IEnumerable<string> RoamingDirectories(string pattern)
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(roaming) || !Directory.Exists(roaming))
            return [];

        try
        {
            return Directory.EnumerateDirectories(roaming, pattern, SearchOption.TopDirectoryOnly);
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

    /// <summary>Compare le nom de fichier de l'executable a une liste attendue.</summary>
    public static bool ExecutableIs(AppInfo application, params string[] fileNames)
    {
        if (string.IsNullOrWhiteSpace(application.ExecutablePath))
            return false;

        var name = Path.GetFileName(application.ExecutablePath);
        return fileNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}
