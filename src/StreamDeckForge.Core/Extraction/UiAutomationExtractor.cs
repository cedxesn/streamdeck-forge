using System.Diagnostics;
using System.Windows.Automation;
using StreamDeckForge.Core.Keys;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Extraction;

/// <summary>
/// Methode 2 - dynamique. Interroge l'arborescence UI Automation des fenetres de
/// l'application : les menus, rubans et boutons publient leur raccourci dans la propriete
/// AcceleratorKey ("Ctrl+Shift+S"). Fonctionne la ou la lecture statique echoue (WPF,
/// WinForms, WinUI, Qt, et la plupart des applications a ruban).
/// </summary>
public sealed class UiAutomationExtractor : IShortcutExtractor
{
    /// <summary>
    /// Profondeur maximale d'exploration, garde-fou contre les arbres gigantesques.
    /// Les rubans Office placent leurs boutons a une dizaine de niveaux de la fenetre,
    /// d'ou une valeur nettement superieure a ce qu'exige un menu Win32 classique.
    /// </summary>
    private const int MaxDepth = 14;

    /// <summary>Delai laisse a un menu pour se peupler apres son ouverture.</summary>
    private static readonly TimeSpan MenuPopulationDelay = TimeSpan.FromMilliseconds(120);

    public ExtractionMethod Method => ExtractionMethod.UiAutomation;

    public string DisplayName => "UI Automation - menus de la fenetre (dynamique)";

    public bool CanRun(ExtractionContext context) =>
        FindProcesses(context.Application).Count > 0;

    public Task<ExtractionResult> ExtractAsync(
        ExtractionContext context,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Extract(context, cancellationToken), cancellationToken);

    private ExtractionResult Extract(ExtractionContext context, CancellationToken cancellationToken)
    {
        var processes = FindProcesses(context.Application);
        if (processes.Count == 0)
        {
            return ExtractionResult.Failed(
                Method,
                "Application non demarree. Lancez-la, ouvrez sa fenetre principale, puis relancez l'extraction.");
        }

        var windows = MainWindows(processes);
        if (windows.Count == 0)
            return ExtractionResult.Failed(Method, "Aucune fenetre exploitable pour ce processus.");

        var found = new Dictionary<string, ShortcutDefinition>(StringComparer.OrdinalIgnoreCase);
        var deadline = Stopwatch.StartNew();

        foreach (var window in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Walk(window, "Fenetre", 0, found, context, deadline, cancellationToken);
        }

        if (found.Count == 0)
        {
            return ExtractionResult.Failed(
                Method,
                "Arborescence lue, mais aucune propriete AcceleratorKey renseignee.");
        }

        var shortcuts = found.Values
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return ExtractionResult.Ok(
            Method,
            shortcuts,
            $"{shortcuts.Count} raccourci(s) lus en {deadline.Elapsed.TotalSeconds:F1} s.");
    }

    private static List<Process> FindProcesses(AppInfo app)
    {
        if (!app.HasExecutable)
            return [];

        var expected = Path.GetFileNameWithoutExtension(app.ExecutablePath!);

        try
        {
            return Process.GetProcessesByName(expected)
                .Where(p => p.MainWindowHandle != IntPtr.Zero)
                .ToList();
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    private static List<AutomationElement> MainWindows(IEnumerable<Process> processes)
    {
        var windows = new List<AutomationElement>();

        foreach (var process in processes)
        {
            try
            {
                var element = AutomationElement.FromHandle(process.MainWindowHandle);
                if (element is not null)
                    windows.Add(element);
            }
            catch (ElementNotAvailableException)
            {
            }
            catch (ArgumentException)
            {
            }
        }

        return windows;
    }

    private void Walk(
        AutomationElement element,
        string path,
        int depth,
        Dictionary<string, ShortcutDefinition> found,
        ExtractionContext context,
        Stopwatch elapsed,
        CancellationToken cancellationToken)
    {
        if (depth > MaxDepth || elapsed.Elapsed > context.UiAutomationTimeout)
            return;

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            Collect(element, path, found);

            var isMenu =
                Equals(element.Current.ControlType, ControlType.MenuItem) ||
                Equals(element.Current.ControlType, ControlType.Menu) ||
                Equals(element.Current.ControlType, ControlType.MenuBar);

            var expanded = false;
            if (context.ExpandMenus && isMenu)
                expanded = TryExpand(element);

            foreach (var child in Children(element))
            {
                var childPath = ChildPath(path, element);
                Walk(child, childPath, depth + 1, found, context, elapsed, cancellationToken);
            }

            if (expanded)
                TryCollapse(element);
        }
        catch (ElementNotAvailableException)
        {
            // La fenetre a bouge ou s'est fermee pendant le parcours : on abandonne ce noeud.
        }
    }

    private static string ChildPath(string path, AutomationElement element)
    {
        var name = SafeName(element).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return path;

        // Les rubans Office empilent plusieurs niveaux portant le meme nom ("Ribbon >
        // Ribbon > Ribbon") : on n'ajoute pas un segment identique au precedent.
        if (path.EndsWith($"> {name}", StringComparison.Ordinal) ||
            path.Equals(name, StringComparison.Ordinal))
            return path;

        return $"{path} > {name}";
    }

    private static void Collect(
        AutomationElement element,
        string path,
        Dictionary<string, ShortcutDefinition> found)
    {
        var accelerator = element.Current.AcceleratorKey;
        if (string.IsNullOrWhiteSpace(accelerator))
            return;

        if (!KeyChordParser.TryParse(accelerator, out var chord))
            return;

        var name = SafeName(element);
        if (string.IsNullOrWhiteSpace(name))
            name = chord.Display;

        var shortcut = new ShortcutDefinition
        {
            Name = name.Replace("&", string.Empty).Trim(),
            Chord = chord,
            Method = ExtractionMethod.UiAutomation,
            Origin = path,
            Command = element.Current.AutomationId
        };

        found.TryAdd(shortcut.DedupKey, shortcut);
    }

    private static string SafeName(AutomationElement element)
    {
        try
        {
            return element.Current.Name ?? string.Empty;
        }
        catch (ElementNotAvailableException)
        {
            return string.Empty;
        }
    }

    private static List<AutomationElement> Children(AutomationElement element)
    {
        try
        {
            var children = new List<AutomationElement>();
            var walker = TreeWalker.ControlViewWalker;

            for (var child = walker.GetFirstChild(element);
                 child is not null;
                 child = walker.GetNextSibling(child))
            {
                children.Add(child);
            }

            return children;
        }
        catch (ElementNotAvailableException)
        {
            return [];
        }
    }

    private static bool TryExpand(AutomationElement element)
    {
        try
        {
            if (!element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var pattern))
                return false;

            var expandCollapse = (ExpandCollapsePattern)pattern;
            if (expandCollapse.Current.ExpandCollapseState == ExpandCollapseState.LeafNode)
                return false;

            expandCollapse.Expand();

            // Les menus Win32 se peuplent lors de l'ouverture ; sans cette pause,
            // l'arborescence lue est vide.
            Thread.Sleep(MenuPopulationDelay);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static void TryCollapse(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var pattern))
                ((ExpandCollapsePattern)pattern).Collapse();
        }
        catch (InvalidOperationException)
        {
        }
        catch (ElementNotAvailableException)
        {
        }
    }
}
