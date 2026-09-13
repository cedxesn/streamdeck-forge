using System.Xml.Linq;
using StreamDeckForge.Core.Keys;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Extraction.ConfigFile;

/// <summary>
/// Keymaps XML des IDE JetBrains (IntelliJ, Rider, PyCharm, WebStorm...) :
/// %APPDATA%\JetBrains\&lt;Produit&gt;\keymaps\*.xml.
/// Format : &lt;action id="SaveAll"&gt;&lt;keyboard-shortcut first-keystroke="ctrl s"/&gt;&lt;/action&gt;.
/// </summary>
public sealed class JetBrainsKeymapParser : IKeybindingFileParser
{
    private static readonly string[] Executables =
    [
        "idea64.exe", "idea.exe", "rider64.exe", "rider.exe", "pycharm64.exe", "pycharm.exe",
        "webstorm64.exe", "webstorm.exe", "phpstorm64.exe", "clion64.exe", "goland64.exe",
        "rubymine64.exe", "datagrip64.exe", "studio64.exe"
    ];

    public string ProductName => "JetBrains IDE";

    public IReadOnlyList<string> LocateFiles(AppInfo application)
    {
        if (!ConfigFileHelpers.ExecutableIs(application, Executables))
            return [];

        // Chaque produit et chaque version ont leur dossier :
        // %APPDATA%\JetBrains\IntelliJIdea2024.1\keymaps\*.xml
        var jetBrainsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JetBrains");

        if (!Directory.Exists(jetBrainsRoot))
            return [];

        var files = new List<string>();

        foreach (var product in Directory.EnumerateDirectories(jetBrainsRoot))
        {
            var keymaps = Path.Combine(product, "keymaps");
            if (Directory.Exists(keymaps))
                files.AddRange(Directory.EnumerateFiles(keymaps, "*.xml"));
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

        foreach (var action in document.Descendants("action"))
        {
            var id = action.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            foreach (var binding in action.Elements("keyboard-shortcut"))
            {
                var keystroke = binding.Attribute("first-keystroke")?.Value;

                // Un second keystroke signifie un accord a deux temps, hors de portee
                // d'une touche Stream Deck.
                if (binding.Attribute("second-keystroke") is not null)
                    continue;

                if (!KeyChordParser.TryParse(keystroke, out var chord))
                    continue;

                shortcuts.Add(new ShortcutDefinition
                {
                    Name = Humanize(id),
                    Chord = chord,
                    Method = ExtractionMethod.ConfigFile,
                    Origin = Path.GetFileName(path),
                    Command = id
                });
            }
        }

        return shortcuts;
    }

    /// <summary>"ReformatCode" -> "Reformat Code".</summary>
    private static string Humanize(string actionId)
    {
        var spaced = string.Concat(actionId.Select((c, i) =>
            i > 0 && char.IsUpper(c) && !char.IsUpper(actionId[i - 1]) ? $" {c}" : c.ToString()));

        return spaced.Replace('.', ' ').Trim();
    }
}
