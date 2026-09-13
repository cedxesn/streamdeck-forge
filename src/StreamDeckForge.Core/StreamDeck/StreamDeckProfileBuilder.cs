using System.Text;
using StreamDeckForge.Core.Keys;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.StreamDeck;

/// <summary>Traduit une grille en manifeste Elgato.</summary>
public static class StreamDeckProfileBuilder
{
    private const int ShiftModifier = 1;
    private const int CtrlModifier = 2;
    private const int AltModifier = 4;
    private const int WinModifier = 8;

    /// <summary>Largeur de ligne utilisee pour couper les titres longs.</summary>
    private const int TitleLineLength = 9;

    private const int MaxTitleLines = 3;

    public static ProfileManifest Build(ProfileLayout layout, string? deviceSerial = null)
    {
        var manifest = new ProfileManifest
        {
            Name = layout.ProfileName,
            Device = new DeviceDescriptor
            {
                Model = layout.Device.Model,
                Uuid = deviceSerial ?? string.Empty
            }
        };

        foreach (var assignment in layout.Assignments)
            manifest.Actions[assignment.Position] = BuildAction(assignment);

        return manifest;
    }

    private static ProfileAction BuildAction(KeyAssignment assignment)
    {
        var title = WrapTitle(assignment.Title ?? assignment.Shortcut.Name);

        return new ProfileAction
        {
            Name = "Hotkey",
            Controller = "Keypad",
            Settings = new HotkeySettings
            {
                Hotkey = [ToHotkeyEntry(assignment.Shortcut.Chord)],
                IsMultiAction = false
            },
            State = 0,
            States =
            [
                new ActionState
                {
                    Title = title,
                    Image = assignment.ImagePng is null ? null : ImagePathFor(assignment)
                }
            ]
        };
    }

    /// <summary>Chemin relatif, dans le profil, de l'image d'une touche.</summary>
    public static string ImagePathFor(KeyAssignment assignment) =>
        $"Images/key_{assignment.Column}_{assignment.Row}.png";

    public static HotkeyEntry ToHotkeyEntry(KeyChord chord)
    {
        if (!KeyCatalog.TryGet(chord.Key, out var codes))
            throw new InvalidOperationException($"Touche inconnue : {chord.Key}");

        var modifiers = 0;
        if (chord.Shift) modifiers |= ShiftModifier;
        if (chord.Ctrl) modifiers |= CtrlModifier;
        if (chord.Alt) modifiers |= AltModifier;
        if (chord.Win) modifiers |= WinModifier;

        return new HotkeyEntry
        {
            KeyCtrl = chord.Ctrl,
            KeyShift = chord.Shift,
            KeyOption = chord.Alt,
            KeyCmd = chord.Win,
            KeyModifiers = modifiers,
            VKeyCode = codes.VirtualKey,
            QtKeyCode = codes.QtKey,
            NativeCode = codes.NativeCode
        };
    }

    /// <summary>
    /// Une touche affiche environ neuf caracteres par ligne sur trois lignes.
    /// Au-dela le texte est tronque plutot que rogne par le rendu du boitier.
    /// </summary>
    public static string WrapTitle(string title)
    {
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return string.Empty;

        var lines = new List<string>();
        var current = new StringBuilder();

        foreach (var word in words)
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > TitleLineLength)
            {
                lines.Add(current.ToString());
                current.Clear();

                if (lines.Count == MaxTitleLines)
                    return string.Join('\n', lines);
            }

            if (current.Length > 0)
                current.Append(' ');

            // Un mot plus long qu'une ligne est coupe net.
            current.Append(word.Length > TitleLineLength ? word[..TitleLineLength] : word);
        }

        if (current.Length > 0 && lines.Count < MaxTitleLines)
            lines.Add(current.ToString());

        return string.Join('\n', lines);
    }
}
