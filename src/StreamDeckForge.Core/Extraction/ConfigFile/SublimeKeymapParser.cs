using System.Text.Json;
using StreamDeckForge.Core.Keys;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Extraction.ConfigFile;

/// <summary>
/// Fichiers .sublime-keymap de Sublime Text / Sublime Merge :
/// %APPDATA%\Sublime Text\Packages\User\Default (Windows).sublime-keymap.
/// Format : [{ "keys": ["ctrl+shift+s"], "command": "save_all" }].
/// </summary>
public sealed class SublimeKeymapParser : IKeybindingFileParser
{
    public string ProductName => "Sublime Text";

    public IReadOnlyList<string> LocateFiles(AppInfo application)
    {
        if (!ConfigFileHelpers.ExecutableIs(application, "sublime_text.exe", "sublime_merge.exe"))
            return [];

        var files = new List<string>();

        foreach (var root in ConfigFileHelpers.RoamingDirectories("Sublime*"))
        {
            var userPackages = Path.Combine(root, "Packages", "User");
            if (Directory.Exists(userPackages))
                files.AddRange(Directory.EnumerateFiles(userPackages, "*.sublime-keymap"));
        }

        return files;
    }

    public IReadOnlyList<ShortcutDefinition> Parse(string path)
    {
        var shortcuts = new List<ShortcutDefinition>();
        var json = ConfigFileHelpers.StripJsonComments(File.ReadAllText(path));

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            return shortcuts;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return shortcuts;

            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                    continue;

                if (!entry.TryGetProperty("keys", out var keys) ||
                    keys.ValueKind != JsonValueKind.Array)
                    continue;

                // Plusieurs entrees dans "keys" decrivent un accord a deux temps.
                var keyList = keys.EnumerateArray().ToList();
                if (keyList.Count != 1 || keyList[0].ValueKind != JsonValueKind.String)
                    continue;

                if (!KeyChordParser.TryParse(keyList[0].GetString(), out var chord))
                    continue;

                var command = entry.TryGetProperty("command", out var commandValue) &&
                              commandValue.ValueKind == JsonValueKind.String
                    ? commandValue.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(command))
                    continue;

                shortcuts.Add(new ShortcutDefinition
                {
                    Name = Humanize(command),
                    Chord = chord,
                    Method = ExtractionMethod.ConfigFile,
                    Origin = Path.GetFileName(path),
                    Command = command
                });
            }
        }

        return shortcuts;
    }

    /// <summary>"save_all" -> "Save all".</summary>
    private static string Humanize(string command)
    {
        var words = command.Replace('_', ' ').Trim();
        return words.Length == 0
            ? command
            : char.ToUpperInvariant(words[0]) + words[1..];
    }
}
