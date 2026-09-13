using StreamDeckForge.App.Infrastructure;

namespace StreamDeckForge.App.ViewModels;

/// <summary>Une touche de la grille de previsualisation.</summary>
public sealed class KeyCellViewModel : ObservableObject
{
    private string? _title;
    private string? _keys;

    public KeyCellViewModel(int column, int row)
    {
        Column = column;
        Row = row;
    }

    public int Column { get; }

    public int Row { get; }

    public string Position => $"{Column},{Row}";

    public string? Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value))
                OnPropertyChanged(nameof(IsAssigned));
        }
    }

    public string? Keys
    {
        get => _keys;
        set => SetProperty(ref _keys, value);
    }

    public bool IsAssigned => !string.IsNullOrEmpty(_title);

    public void Clear()
    {
        Title = null;
        Keys = null;
    }
}
