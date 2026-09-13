using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Extraction;

/// <summary>Ce que l'extracteur recoit : l'application ciblee et les options de l'utilisateur.</summary>
public sealed class ExtractionContext
{
    public required AppInfo Application { get; init; }

    /// <summary>
    /// Etend la lecture des tables d'accelerateurs aux DLL du dossier d'installation
    /// (certaines applications MFC placent leurs ressources dans un module satellite).
    /// </summary>
    public bool ScanCompanionModules { get; init; }

    /// <summary>
    /// UI Automation : deplier reellement les menus de la fenetre. Sans cela, seuls les
    /// raccourcis deja exposes par l'arborescence sont lus, sans interagir avec l'application.
    /// </summary>
    public bool ExpandMenus { get; init; } = true;

    /// <summary>Budget de temps accorde a l'exploration UI Automation.</summary>
    public TimeSpan UiAutomationTimeout { get; init; } = TimeSpan.FromSeconds(20);
}

/// <summary>Compte rendu d'une methode d'extraction, succes comme echec.</summary>
public sealed record ExtractionResult(
    ExtractionMethod Method,
    IReadOnlyList<ShortcutDefinition> Shortcuts,
    bool Succeeded,
    string Message)
{
    public static ExtractionResult Ok(
        ExtractionMethod method,
        IReadOnlyList<ShortcutDefinition> shortcuts,
        string message) =>
        new(method, shortcuts, true, message);

    public static ExtractionResult Failed(ExtractionMethod method, string message) =>
        new(method, [], false, message);
}

/// <summary>Une des trois methodes d'extraction de raccourcis.</summary>
public interface IShortcutExtractor
{
    ExtractionMethod Method { get; }

    /// <summary>Libelle affiche dans l'interface.</summary>
    string DisplayName { get; }

    /// <summary>Indique si la methode a quelque chose a exploiter pour cette application.</summary>
    bool CanRun(ExtractionContext context);

    Task<ExtractionResult> ExtractAsync(
        ExtractionContext context,
        CancellationToken cancellationToken = default);
}
