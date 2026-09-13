using StreamDeckForge.Core.Extraction.ConfigFile;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Extraction;

/// <summary>
/// Methode 3 - parseurs dedies. Detecte les fichiers de configuration locaux connus et
/// delegue leur lecture au parseur du produit correspondant.
/// </summary>
public sealed class ConfigFileExtractor : IShortcutExtractor
{
    private readonly IReadOnlyList<IKeybindingFileParser> _parsers;

    public ConfigFileExtractor()
        : this(
            new ArdourBindingsParser(),
            new VsCodeKeybindingParser(),
            new JetBrainsKeymapParser(),
            new SublimeKeymapParser(),
            new NotepadPlusPlusParser())
    {
    }

    public ConfigFileExtractor(params IKeybindingFileParser[] parsers) => _parsers = parsers;

    public ExtractionMethod Method => ExtractionMethod.ConfigFile;

    public string DisplayName => "Fichiers de configuration locaux";

    public bool CanRun(ExtractionContext context) =>
        _parsers.Any(parser => parser.LocateFiles(context.Application).Count > 0);

    public Task<ExtractionResult> ExtractAsync(
        ExtractionContext context,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Extract(context, cancellationToken), cancellationToken);

    private ExtractionResult Extract(ExtractionContext context, CancellationToken cancellationToken)
    {
        var shortcuts = new List<ShortcutDefinition>();
        var sources = new List<string>();

        foreach (var parser in _parsers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var file in parser.LocateFiles(context.Application))
            {
                try
                {
                    var parsed = parser.Parse(file);
                    if (parsed.Count == 0)
                        continue;

                    shortcuts.AddRange(parsed);
                    sources.Add($"{parser.ProductName} : {Path.GetFileName(file)} ({parsed.Count})");
                }
                catch (IOException)
                {
                    // Fichier verrouille par l'application : on passe au suivant.
                }
                catch (System.Text.Json.JsonException)
                {
                    sources.Add($"{parser.ProductName} : {Path.GetFileName(file)} illisible (JSON invalide)");
                }
            }
        }

        if (shortcuts.Count == 0)
        {
            return ExtractionResult.Failed(
                Method,
                "Aucun fichier de raccourcis connu pour cette application.");
        }

        var deduplicated = shortcuts
            .GroupBy(s => s.DedupKey)
            .Select(group => group.First())
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return ExtractionResult.Ok(
            Method,
            deduplicated,
            $"{deduplicated.Count} raccourci(s). Sources : {string.Join(" ; ", sources)}");
    }
}
