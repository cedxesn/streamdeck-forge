using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Extraction;

/// <summary>Resultat agrege des methodes lancees.</summary>
public sealed record AggregatedExtraction(
    IReadOnlyList<ShortcutDefinition> Shortcuts,
    IReadOnlyList<ExtractionResult> Results)
{
    public bool AnyMethodSucceeded => Results.Any(r => r.Succeeded);
}

/// <summary>
/// Orchestre les trois methodes d'extraction et fusionne leurs resultats. Une meme
/// combinaison trouvee par plusieurs methodes n'est conservee qu'une fois, en gardant
/// la version la mieux nommee.
/// </summary>
public sealed class ShortcutExtractionService
{
    private readonly IReadOnlyList<IShortcutExtractor> _extractors;

    public ShortcutExtractionService()
        : this(
            new AcceleratorTableExtractor(),
            new UiAutomationExtractor(),
            new ConfigFileExtractor())
    {
    }

    public ShortcutExtractionService(params IShortcutExtractor[] extractors) =>
        _extractors = extractors;

    public IReadOnlyList<IShortcutExtractor> Extractors => _extractors;

    public async Task<AggregatedExtraction> ExtractAsync(
        ExtractionContext context,
        IReadOnlyCollection<ExtractionMethod> methods,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ExtractionResult>();

        foreach (var extractor in _extractors.Where(e => methods.Contains(e.Method)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                results.Add(await extractor.ExtractAsync(context, cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Une methode qui echoue ne doit pas empecher les deux autres d'aboutir :
                // le critere d'acceptation demande qu'au moins une methode reponde.
                results.Add(ExtractionResult.Failed(extractor.Method, $"Echec : {ex.Message}"));
            }
        }

        return new AggregatedExtraction(Merge(results), results);
    }

    private static List<ShortcutDefinition> Merge(IEnumerable<ExtractionResult> results) =>
        results
            .SelectMany(result => result.Shortcuts)
            .GroupBy(shortcut => shortcut.DedupKey)
            .Select(group => group.OrderByDescending(NameQuality).First())
            .OrderBy(shortcut => shortcut.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Un libelle reel vaut mieux qu'un identifiant de commande brut.</summary>
    private static int NameQuality(ShortcutDefinition shortcut)
    {
        if (shortcut.Name.StartsWith("Commande ", StringComparison.Ordinal))
            return 0;

        return shortcut.Method == ExtractionMethod.UiAutomation ? 2 : 1;
    }
}
