using System.Xml.Linq;
using StreamDeckForge.Core.Keys;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Extraction.ConfigFile;

/// <summary>
/// Fichiers de bindings d'Ardour et de ses derives commerciaux (Harrison Mixbus,
/// Mixbus 32C). Ces logiciels publient l'integralite de leurs raccourcis dans un
/// fichier XML, defauts livres compris - contrairement a la plupart des applications,
/// ou seules les personnalisations de l'utilisateur sont sur le disque.
///
/// Format produit par tools/fmt-bindings :
///
///   &lt;BindingSet name="Mixbus"&gt;
///    &lt;Bindings name="Global"&gt;
///     &lt;Press&gt;
///      &lt;Binding key="Control-s" action="Editor/save-session" group="File"/&gt;
///
/// La valeur de "key" enchaine des modificateurs separes par des tirets, puis un nom
/// de touche GDK ("space", "KP_Enter", "bracketleft").
/// </summary>
public sealed class ArdourBindingsParser : IKeybindingFileParser
{
    /// <summary>Profondeur d'exploration du dossier d'installation.</summary>
    private const int MaxInstallSearchDepth = 4;

    /// <summary>Garde-fou : au-dela, on n'est manifestement pas dans le bon dossier.</summary>
    private const int MaxFiles = 40;

    public string ProductName => "Ardour / Mixbus";

    /// <summary>
    /// Modificateurs. Ardour ecrit soit les noms logiques (Primary, Secondary...),
    /// soit les noms resolus pour la plateforme. Sous Windows :
    /// Primary = Ctrl, Secondary = Alt, Tertiary = Maj, Level4 = Windows.
    /// </summary>
    private static readonly Dictionary<string, string> Modifiers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["primary"] = "ctrl",
            ["control"] = "ctrl",
            ["ctrl"] = "ctrl",
            ["tertiary"] = "shift",
            ["shift"] = "shift",
            ["secondary"] = "alt",
            ["alt"] = "alt",
            ["mod1"] = "alt",
            ["meta"] = "alt",
            ["level4"] = "win",
            ["mod4"] = "win",
            ["windows"] = "win",
            ["super"] = "win",
            ["command"] = "ctrl"
        };

    /// <summary>Noms de touches GDK -> jetons de <see cref="KeyCatalog"/>.</summary>
    private static readonly Dictionary<string, string> GdkKeys =
        new(StringComparer.Ordinal)
        {
            ["space"] = "Space",
            ["Return"] = "Enter",
            ["Tab"] = "Tab",
            ["ISO_Left_Tab"] = "Tab",
            ["BackSpace"] = "Backspace",
            ["Escape"] = "Escape",
            ["Delete"] = "Delete",
            ["Insert"] = "Insert",
            ["Home"] = "Home",
            ["End"] = "End",
            ["Page_Up"] = "PageUp",
            ["Page_Down"] = "PageDown",
            ["Left"] = "Left",
            ["Right"] = "Right",
            ["Up"] = "Up",
            ["Down"] = "Down",
            ["KP_Left"] = "Left",
            ["KP_Right"] = "Right",
            ["KP_Up"] = "Up",
            ["KP_Down"] = "Down",
            ["KP_Enter"] = "Enter",
            ["KP_Add"] = "Add",
            ["KP_Subtract"] = "Subtract",
            ["KP_Multiply"] = "Multiply",
            ["KP_Divide"] = "Divide",
            ["KP_Decimal"] = "Decimal",
            ["KP_Separator"] = "Decimal",
            ["comma"] = "OemComma",
            ["period"] = "OemPeriod",
            ["slash"] = "OemQuestion",
            ["backslash"] = "OemPipe",
            ["semicolon"] = "OemSemicolon",
            ["apostrophe"] = "OemQuotes",
            ["bracketleft"] = "OemOpenBrackets",
            ["bracketright"] = "OemCloseBrackets",
            ["grave"] = "OemTilde",
            ["minus"] = "OemMinus",
            ["equal"] = "OemPlus"
        };

    /// <summary>
    /// Noms GDK qui designent le caractere obtenu touche Maj enfoncee. Sur une
    /// disposition US, "question" c'est Maj + "/" : on ajoute donc le modificateur,
    /// sans quoi le Stream Deck enverrait la mauvaise frappe.
    /// </summary>
    private static readonly Dictionary<string, string> ShiftedGdkKeys =
        new(StringComparer.Ordinal)
        {
            ["question"] = "OemQuestion",
            ["colon"] = "OemSemicolon",
            ["quotedbl"] = "OemQuotes",
            ["braceleft"] = "OemOpenBrackets",
            ["braceright"] = "OemCloseBrackets",
            ["bar"] = "OemPipe",
            ["asciitilde"] = "OemTilde",
            ["underscore"] = "OemMinus",
            ["plus"] = "OemPlus",
            ["less"] = "OemComma",
            ["greater"] = "OemPeriod",
            ["asterisk"] = "8",
            ["exclam"] = "1",
            ["at"] = "2",
            ["numbersign"] = "3",
            ["dollar"] = "4",
            ["percent"] = "5",
            ["asciicircum"] = "6",
            ["ampersand"] = "7",
            ["parenleft"] = "9",
            ["parenright"] = "0"
        };

    public IReadOnlyList<string> LocateFiles(AppInfo application)
    {
        if (!IsArdourFamily(application))
            return [];

        var files = new List<string>();

        // Dossiers de preferences : Ardour et Mixbus y ecrivent les bindings
        // personnalises, sous un nom qui porte le numero de version majeure.
        foreach (var root in PreferenceRoots())
        foreach (var directory in DirectoriesMatching(root, ["Mixbus*", "mixbus*", "Ardour*", "ardour*"]))
            files.AddRange(BindingFilesIn(directory, MaxInstallSearchDepth));

        // Dossier d'installation : il contient les bindings livres par defaut, soit
        // la liste complete des raccourcis du logiciel.
        var installDirectory = Path.GetDirectoryName(application.ExecutablePath ?? string.Empty);
        if (!string.IsNullOrEmpty(installDirectory) && Directory.Exists(installDirectory))
            files.AddRange(BindingFilesIn(installDirectory, MaxInstallSearchDepth));

        return files
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxFiles)
            .ToList();
    }

    private static bool IsArdourFamily(AppInfo application)
    {
        if (string.IsNullOrWhiteSpace(application.ExecutablePath))
            return false;

        var stem = Path.GetFileNameWithoutExtension(application.ExecutablePath);

        return stem.StartsWith("mixbus", StringComparison.OrdinalIgnoreCase) ||
               stem.StartsWith("ardour", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> PreferenceRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // Portage depuis Unix : certaines versions gardent la disposition ~/.config.
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
            yield return Path.Combine(profile, ".config");
    }

    private static IEnumerable<string> DirectoriesMatching(string? root, string[] patterns)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            yield break;

        foreach (var pattern in patterns)
        {
            List<string> matches;

            try
            {
                matches = Directory
                    .EnumerateDirectories(root, pattern, SearchOption.TopDirectoryOnly)
                    .ToList();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var match in matches)
                yield return match;
        }
    }

    private static List<string> BindingFilesIn(string directory, int maxDepth)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = maxDepth,
            IgnoreInaccessible = true
        };

        var files = new List<string>();

        foreach (var pattern in new[] { "*.keys", "*.bindings" })
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
        }

        return files;
    }

    public IReadOnlyList<ShortcutDefinition> Parse(string path)
    {
        var shortcuts = new List<ShortcutDefinition>();
        XDocument document;

        try
        {
            document = XDocument.Load(path);
        }
        catch (System.Xml.XmlException)
        {
            return shortcuts;
        }

        var fileName = Path.GetFileName(path);

        foreach (var binding in document.Descendants("Binding"))
        {
            var key = binding.Attribute("key")?.Value;
            var action = binding.Attribute("action")?.Value;

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(action))
                continue;

            if (!TryParseKey(key, out var chord))
                continue;

            var group = binding.Attribute("group")?.Value;
            var bindingSet = binding.Ancestors("Bindings").FirstOrDefault()?.Attribute("name")?.Value;

            shortcuts.Add(new ShortcutDefinition
            {
                Name = Humanize(action),
                Chord = chord,
                Method = ExtractionMethod.ConfigFile,
                Origin = string.IsNullOrWhiteSpace(group)
                    ? $"{fileName} / {bindingSet ?? "Bindings"}"
                    : $"{fileName} / {group}",
                Command = action
            });
        }

        return shortcuts;
    }

    /// <summary>Analyse une valeur "key" du format Ardour, par ex. "Primary-Tertiary-s".</summary>
    internal static bool TryParseKey(string key, out KeyChord chord)
    {
        chord = default!;

        // Certaines versions ecrivent le style accelerateur GTK : "&lt;Primary&gt;a".
        var normalized = key.Replace("<", string.Empty).Replace(">", "-").Trim();

        var tokens = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return false;

        bool ctrl = false, shift = false, alt = false, win = false;
        string? keyToken = null;

        foreach (var token in tokens)
        {
            if (Modifiers.TryGetValue(token, out var modifier))
            {
                switch (modifier)
                {
                    case "ctrl": ctrl = true; break;
                    case "shift": shift = true; break;
                    case "alt": alt = true; break;
                    case "win": win = true; break;
                }

                continue;
            }

            // Deux touches principales : ecriture inattendue, on passe.
            if (keyToken is not null)
                return false;

            keyToken = token;
        }

        if (keyToken is null)
            return false;

        if (GdkKeys.TryGetValue(keyToken, out var mapped))
        {
            keyToken = mapped;
        }
        else if (ShiftedGdkKeys.TryGetValue(keyToken, out var shifted))
        {
            keyToken = shifted;
            shift = true;
        }
        else if (keyToken.Length == 1)
        {
            // Une lettre minuscule ne signifie pas "sans Maj" : c'est simplement
            // la façon dont GDK nomme la touche physique.
            keyToken = keyToken.ToUpperInvariant();
        }
        else if (keyToken.StartsWith('F') && int.TryParse(keyToken[1..], out _))
        {
            // F1 a F24 : deja au bon format.
        }
        else
        {
            return false;
        }

        if (!KeyCatalog.TryGet(keyToken, out _))
            return false;

        chord = new KeyChord(ctrl, shift, alt, win, keyToken);
        return true;
    }

    /// <summary>"Editor/save-session" -> "Save session" ; "Transport/ToggleRoll" -> "Toggle Roll".</summary>
    internal static string Humanize(string action)
    {
        var leaf = action.Split('/').Last();

        // Coupe le camelCase, puis remplace tirets et soulignes par des espaces.
        var spaced = string.Concat(leaf.Select((c, i) =>
            i > 0 && char.IsUpper(c) && !char.IsUpper(leaf[i - 1]) ? $" {c}" : c.ToString()));

        spaced = spaced.Replace('-', ' ').Replace('_', ' ');

        var words = spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return action;

        var first = words[0];
        words[0] = char.ToUpperInvariant(first[0]) + first[1..];

        return string.Join(' ', words);
    }
}
