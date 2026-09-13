namespace StreamDeckForge.Core.Models;

/// <summary>Methode d'extraction ayant produit un raccourci.</summary>
public enum ExtractionMethod
{
    /// <summary>Methode 1 : tables d'accelerateurs Win32 (RT_ACCELERATOR) lues dans le PE.</summary>
    AcceleratorTable,

    /// <summary>Methode 2 : UI Automation sur la fenetre active (menus / ruban).</summary>
    UiAutomation,

    /// <summary>Methode 3 : parseur dedie d'un fichier de configuration local.</summary>
    ConfigFile
}

/// <summary>Un raccourci clavier extrait, avant placement sur la grille.</summary>
public sealed class ShortcutDefinition
{
    public required string Name { get; init; }
    public required KeyChord Chord { get; init; }
    public required ExtractionMethod Method { get; init; }

    /// <summary>Detail de provenance : "RT_ACCELERATOR #2", "Menu Fichier", "keybindings.json"...</summary>
    public string? Origin { get; init; }

    /// <summary>Commande interne quand elle est connue (id MFC, commande VS Code...).</summary>
    public string? Command { get; init; }

    /// <summary>Cle de deduplication : une meme combinaison n'est proposee qu'une fois.</summary>
    public string DedupKey => Chord.Display.ToLowerInvariant();

    public override string ToString() => $"{Name} = {Chord.Display}";
}
