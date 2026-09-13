using StreamDeckForge.App.Infrastructure;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.App.ViewModels;

/// <summary>Une ligne de la liste des raccourcis extraits.</summary>
public sealed class ShortcutRowViewModel : ObservableObject
{
    private bool _isSelected = true;
    private string _title;

    public ShortcutRowViewModel(ShortcutDefinition definition)
    {
        Definition = definition;
        _title = definition.Name;
    }

    public ShortcutDefinition Definition { get; }

    /// <summary>Raccourcis coches : ceux que le remplissage automatique prendra.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Titre modifiable, repris tel quel sur la touche.</summary>
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Keys => Definition.Chord.Display;

    public string MethodLabel => Definition.Method switch
    {
        ExtractionMethod.AcceleratorTable => "Accelerateurs",
        ExtractionMethod.UiAutomation => "UI Automation",
        ExtractionMethod.ConfigFile => "Configuration",
        _ => Definition.Method.ToString()
    };

    public string Origin => Definition.Origin ?? string.Empty;
}
