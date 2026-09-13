using System.Text.Json;
using StreamDeckForge.Core.Keys;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Extraction.ConfigFile;

/// <summary>
/// keybindings.json de VS Code et de ses derives. Le fichier ne contient que les
/// personnalisations de l'utilisateur : ce sont justement les raccourcis que les
/// tables d'accelerateurs et UI Automation ne peuvent pas voir.
/// </summary>
public sealed class VsCodeKeybindingParser : IKeybindingFileParser
{
    /// <summary>Nom du dossier %APPDATA% par nom d'executable.</summary>
    private static readonly Dictionary<string, string> KnownProducts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Code.exe"] = "Code",
            ["Code - Insiders.exe"] = "Code - Insiders",
            ["VSCodium.exe"] = "VSCodium",
            ["Cursor.exe"] = "Cursor",
            ["Windsurf.exe"] = "Windsurf"
        };

    public string ProductName => "Visual Studio Code";

    public IReadOnlyList<string> LocateFiles(AppInfo application)
    {
        if (string.IsNullOrWhiteSpace(application.ExecutablePath))
            return [];

        var exeName = Path.GetFileName(application.ExecutablePath);
        if (!KnownProducts.TryGetValue(exeName, out var folder))
            return [];

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            folder,
            "User",
            "keybindings.json");

        return File.Exists(path) ? [path] : [];
    }

    public IReadOnlyList<ShortcutDefinition> Parse(string path)
    {
        var shortcuts = new List<ShortcutDefinition>();

        var json = ConfigFileHelpers.StripJsonComments(File.ReadAllText(path));

        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return shortcuts;

        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;

            var key = GetString(entry, "key");
            var command = GetString(entry, "command");

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(command))
                continue;

            // Un "-" en tete supprime un raccourci par defaut : rien a placer sur une touche.
            if (command.StartsWith('-'))
                continue;

            if (!KeyChordParser.TryParse(key, out var chord))
                continue;

            shortcuts.Add(new ShortcutDefinition
            {
                Name = FriendlyName(command),
                Chord = chord,
                Method = ExtractionMethod.ConfigFile,
                Origin = Path.GetFileName(path),
                Command = command
            });
        }

        return shortcuts;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>"workbench.action.files.save" -> "Files save".</summary>
    private static string FriendlyName(string command)
    {
        var last = command.Split('.').LastOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? command;
        var segments = command.Split('.');
        var label = segments.Length >= 2 ? $"{segments[^2]} {last}" : last;

        label = string.Join(
            ' ',
            label.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => string.Concat(
                    word.Take(1).Select(char.ToUpperInvariant).Concat(word.Skip(1)))));

        return label;
    }
}
