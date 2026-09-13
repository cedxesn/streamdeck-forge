using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.StreamDeck;

/// <summary>Contenu d'une touche de la grille.</summary>
public sealed class KeyAssignment
{
    public required int Column { get; init; }

    public required int Row { get; init; }

    public required ShortcutDefinition Shortcut { get; set; }

    /// <summary>Titre affiche sur la touche ; par defaut le nom du raccourci.</summary>
    public string? Title { get; set; }

    /// <summary>Image PNG optionnelle embarquee dans le profil.</summary>
    public byte[]? ImagePng { get; set; }

    public string Position => $"{Column},{Row}";
}

/// <summary>Grille d'un profil : un modele de boitier et les touches renseignees.</summary>
public sealed class ProfileLayout
{
    private readonly Dictionary<string, KeyAssignment> _assignments = [];

    public ProfileLayout(StreamDeckDevice device, string profileName)
    {
        Device = device;
        ProfileName = profileName;
    }

    public StreamDeckDevice Device { get; set; }

    public string ProfileName { get; set; }

    public IReadOnlyCollection<KeyAssignment> Assignments => _assignments.Values;

    public int FreeKeyCount => Device.KeyCount - _assignments.Count;

    public KeyAssignment? At(int column, int row) =>
        _assignments.GetValueOrDefault($"{column},{row}");

    public void Assign(int column, int row, ShortcutDefinition shortcut, string? title = null)
    {
        if (column < 0 || column >= Device.Columns || row < 0 || row >= Device.Rows)
            throw new ArgumentOutOfRangeException(
                nameof(column),
                $"Position {column},{row} hors de la grille {Device.Columns}x{Device.Rows}.");

        _assignments[$"{column},{row}"] = new KeyAssignment
        {
            Column = column,
            Row = row,
            Shortcut = shortcut,
            Title = title ?? shortcut.Name
        };
    }

    public void Clear(int column, int row) => _assignments.Remove($"{column},{row}");

    public void ClearAll() => _assignments.Clear();

    /// <summary>
    /// Remplit la grille de gauche a droite puis de haut en bas et renvoie le nombre de
    /// raccourcis qui n'ont pas trouve de place.
    /// </summary>
    public int AutoFill(IEnumerable<ShortcutDefinition> shortcuts)
    {
        var queue = new Queue<ShortcutDefinition>(shortcuts);

        for (var row = 0; row < Device.Rows && queue.Count > 0; row++)
        {
            for (var column = 0; column < Device.Columns && queue.Count > 0; column++)
            {
                if (At(column, row) is not null)
                    continue;

                Assign(column, row, queue.Dequeue());
            }
        }

        return queue.Count;
    }
}
