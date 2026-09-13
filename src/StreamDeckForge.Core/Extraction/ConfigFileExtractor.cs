using StreamDeckForge.Core.Extraction.ConfigFile;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Extraction;

/// <summary>
/// Methode 3 - parseurs dedies. Procede en deux temps :
///
/// 1. chaque parseur indique les fichiers qu'il reconnait pour cette application ;
/// 2. pour les logiciels qu'aucun parseur ne connait, une recherche generique ratisse
///    le dossier d'installation et les preferences a la recherche de fichiers de
///    raccourcis, puis soumet chaque fichier a tous les parseurs. C'est le contenu qui
///    decide : un fichier .keys inconnu sera lu par le parseur Ardour s'il en a la forme.
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
        _parsers.Any(parser => parser.LocateFiles(context.Application).Count > 0) ||
        BindingFileFinder.Find(context.Application).Count > 0;

    public Task<ExtractionResult> ExtractAsync(
        ExtractionContext context,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Extract(context, cancellationToken), cancellationToken);

    private ExtractionResult Extract(ExtractionContext context, CancellationToken cancellationToken)
    {
        var shortcuts = new List<ShortcutDefinition>();
        var sources = new List<string>();
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var parser in _parsers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var file in parser.LocateFiles(context.Application))
            {
                handled.Add(file);
                Consume(parser, file, shortcuts, sources);
            }
        }

        // Deuxieme passe : les fichiers reperes par leur forme, qu'aucun parseur n'avait
        // revendiques pour cette application.
        foreach (var file in BindingFileFinder.Find(context.Application))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!handled.Add(file))
                continue;

            foreach (var parser in _parsers)
            {
                if (Consume(parser, file, shortcuts, sources))
                    break;
            }
        }

        if (shortcuts.Count == 0)
        {
            return ExtractionResult.Failed(
                Method,
                "Aucun fichier de raccourcis exploitable trouve pour cette application.");
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

    /// <summary>Tente un parseur sur un fichier. Renvoie vrai s'il en a tire quelque chose.</summary>
    private static bool Consume(
        IKeybindingFileParser parser,
        string file,
        List<ShortcutDefinition> shortcuts,
        List<string> sources)
    {
        try
        {
            var parsed = parser.Parse(file);
            if (parsed.Count == 0)
                return false;

            shortcuts.AddRange(parsed);
            sources.Add($"{parser.ProductName} : {Path.GetFileName(file)} ({parsed.Count})");
            return true;
        }
        catch (IOException)
        {
            // Fichier verrouille par l'application : on passe au suivant.
            return false;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }
}
