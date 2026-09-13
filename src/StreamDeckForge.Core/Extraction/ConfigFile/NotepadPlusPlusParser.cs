using System.Xml.Linq;
using StreamDeckForge.Core.Keys;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Extraction.ConfigFile;

/// <summary>
/// shortcuts.xml de Notepad++ (%APPDATA%\Notepad++ ou dossier d'installation en mode
/// portable). Les entrees portent directement le code de touche virtuel Windows :
/// &lt;Shortcut id="41006" Ctrl="yes" Alt="no" Shift="no" Key="83"/&gt;.
/// </summary>
public sealed class NotepadPlusPlusParser : IKeybindingFileParser
{
    public string ProductName => "Notepad++";

    public IReadOnlyList<string> LocateFiles(AppInfo application)
    {
        if (!ConfigFileHelpers.ExecutableIs(application, "notepad++.exe"))
            return [];

        var candidates = new List<string>
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Notepad++",
                "shortcuts.xml")
        };

        // Installation portable : le fichier est a cote de l'executable.
        var installDirectory = Path.GetDirectoryName(application.ExecutablePath!);
        if (installDirectory is not null)
            candidates.Add(Path.Combine(installDirectory, "shortcuts.xml"));

        return candidates.Where(File.Exists).ToList();
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

        foreach (var element in document.Descendants()
                     .Where(e => e.Attribute("Key") is not null))
        {
            if (!int.TryParse(element.Attribute("Key")?.Value, out var virtualKey))
                continue;

            var codes = KeyCatalog.FromVirtualKey(virtualKey);
            if (codes is null)
                continue;

            var chord = new KeyChord(
                Ctrl: IsYes(element, "Ctrl"),
                Shift: IsYes(element, "Shift"),
                Alt: IsYes(element, "Alt"),
                Win: false,
                Key: codes.Token);

            var name = element.Attribute("name")?.Value
                       ?? element.Attribute("id")?.Value
                       ?? element.Name.LocalName;

            shortcuts.Add(new ShortcutDefinition
            {
                Name = name.Replace("&", string.Empty).Trim(),
                Chord = chord,
                Method = ExtractionMethod.ConfigFile,
                Origin = Path.GetFileName(path),
                Command = element.Attribute("id")?.Value
            });
        }

        return shortcuts;
    }

    private static bool IsYes(XElement element, string attribute) =>
        string.Equals(element.Attribute(attribute)?.Value, "yes", StringComparison.OrdinalIgnoreCase);
}
