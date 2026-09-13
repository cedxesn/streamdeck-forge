using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreamDeckForge.Core.Keys;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Extraction;

/// <summary>Une fiche du catalogue : les raccourcis connus d'un logiciel.</summary>
public sealed class CatalogEntry
{
    /// <summary>Nom du produit, affiche dans le compte rendu.</summary>
    public string Product { get; set; } = string.Empty;

    /// <summary>Noms d'executables couverts, par exemple "EXCEL.EXE".</summary>
    public List<string> Executables { get; set; } = [];

    /// <summary>
    /// Langue des raccourcis ("fr", "en"), quand ils en dependent. Vide = valable
    /// quelle que soit la langue. Word et Excel francisent leurs raccourcis :
    /// Gras est Ctrl+G en francais, Ctrl+B en anglais.
    /// </summary>
    public string? Culture { get; set; }

    /// <summary>Note affichee a l'utilisateur, par exemple une reserve sur une version.</summary>
    public string? Note { get; set; }

    public List<CatalogShortcut> Shortcuts { get; set; } = [];
}

/// <summary>Un raccourci du catalogue.</summary>
public sealed class CatalogShortcut
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Combinaison en ecriture usuelle : "Ctrl+Shift+S", "F12", "Win+E".</summary>
    public string Keys { get; set; } = string.Empty;
}

/// <summary>
/// Methode 4 - catalogue embarque. Dernier recours pour les logiciels qui ne publient
/// leurs raccourcis nulle part : ni table d'accelerateurs, ni fichier de configuration,
/// et rien via UI Automation tant qu'ils ne sont pas ouverts. C'est le cas d'Office,
/// des navigateurs et de la plupart des applications Electron.
///
/// Contrairement aux trois autres methodes, il ne s'agit pas d'une extraction mais
/// d'une liste etablie a la main. Elle est donc datee : un raccourci peut changer d'une
/// version a l'autre. Les fiches sont modifiables et completables sans recompiler,
/// dans %APPDATA%\StreamDeckForge\catalogue.
/// </summary>
public sealed class CatalogExtractor : IShortcutExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Lazy<IReadOnlyList<CatalogEntry>> _entries;

    public CatalogExtractor() => _entries = new Lazy<IReadOnlyList<CatalogEntry>>(LoadAll);

    public ExtractionMethod Method => ExtractionMethod.Catalog;

    public string DisplayName => "Catalogue embarque";

    /// <summary>Dossier ou l'utilisateur depose ses propres fiches.</summary>
    public static string UserCatalogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StreamDeckForge",
        "catalogue");

    public IReadOnlyList<CatalogEntry> Entries => _entries.Value;

    public bool CanRun(ExtractionContext context) => Match(context.Application).Count > 0;

    public Task<ExtractionResult> ExtractAsync(
        ExtractionContext context,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Extract(context), cancellationToken);

    private ExtractionResult Extract(ExtractionContext context)
    {
        var matches = Match(context.Application);
        if (matches.Count == 0)
        {
            return ExtractionResult.Failed(
                Method,
                "Aucune fiche au catalogue pour cette application. " +
                $"Vous pouvez en ajouter une dans {UserCatalogDirectory}.");
        }

        var shortcuts = new List<ShortcutDefinition>();
        var rejected = 0;

        foreach (var entry in matches)
        {
            foreach (var item in entry.Shortcuts)
            {
                if (!KeyChordParser.TryParse(item.Keys, out var chord))
                {
                    rejected++;
                    continue;
                }

                shortcuts.Add(new ShortcutDefinition
                {
                    Name = item.Name,
                    Chord = chord,
                    Method = ExtractionMethod.Catalog,
                    Origin = $"Catalogue : {entry.Product}",
                    Command = item.Keys
                });
            }
        }

        if (shortcuts.Count == 0)
            return ExtractionResult.Failed(Method, "Fiche trouvee, mais aucune combinaison lisible.");

        var message =
            $"{shortcuts.Count} raccourci(s) depuis {matches.Count} fiche(s) : " +
            string.Join(", ", matches.Select(m => m.Product));

        if (rejected > 0)
            message += $". {rejected} entree(s) illisibles ignorees";

        var note = matches.Select(m => m.Note).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        if (note is not null)
            message += $". {note}";

        return ExtractionResult.Ok(Method, shortcuts, message);
    }

    /// <summary>
    /// Fiches applicables : celles qui citent l'executable, en gardant d'abord celles
    /// dont la langue correspond a celle de Windows.
    /// </summary>
    private List<CatalogEntry> Match(AppInfo application)
    {
        if (string.IsNullOrWhiteSpace(application.ExecutablePath))
            return [];

        var fileName = Path.GetFileName(application.ExecutablePath);

        var candidates = Entries
            .Where(entry => entry.Executables.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count <= 1)
            return candidates;

        var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        var localized = candidates
            .Where(entry => string.Equals(entry.Culture, culture, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (localized.Count > 0)
            return localized;

        // Pas de fiche dans la langue de Windows : on retombe sur les fiches neutres.
        var neutral = candidates.Where(entry => string.IsNullOrWhiteSpace(entry.Culture)).ToList();
        return neutral.Count > 0 ? neutral : candidates;
    }

    private static List<CatalogEntry> LoadAll()
    {
        var entries = new List<CatalogEntry>();

        entries.AddRange(LoadEmbedded());
        entries.AddRange(LoadFromUserDirectory());

        return entries;
    }

    private static IEnumerable<CatalogEntry> LoadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(n => n.Contains(".Catalog.", StringComparison.Ordinal) &&
                                 n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
                continue;

            var entry = Deserialize(stream);
            if (entry is not null)
                yield return entry;
        }
    }

    private static IEnumerable<CatalogEntry> LoadFromUserDirectory()
    {
        if (!Directory.Exists(UserCatalogDirectory))
            yield break;

        List<string> files;

        try
        {
            files = Directory.EnumerateFiles(UserCatalogDirectory, "*.json").ToList();
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            CatalogEntry? entry = null;

            try
            {
                using var stream = File.OpenRead(file);
                entry = Deserialize(stream);
            }
            catch (IOException)
            {
            }

            if (entry is not null)
                yield return entry;
        }
    }

    private static CatalogEntry? Deserialize(Stream stream)
    {
        try
        {
            var entry = JsonSerializer.Deserialize<CatalogEntry>(stream, JsonOptions);
            return entry is null || entry.Shortcuts.Count == 0 ? null : entry;
        }
        catch (JsonException)
        {
            // Fiche malformee : on l'ignore plutot que de faire echouer tout le catalogue.
            return null;
        }
    }

    /// <summary>
    /// Ecrit une fiche d'exemple dans le dossier utilisateur, pour servir de modele.
    /// </summary>
    public static string WriteSampleEntry()
    {
        Directory.CreateDirectory(UserCatalogDirectory);
        var path = Path.Combine(UserCatalogDirectory, "exemple.json");

        var sample = new CatalogEntry
        {
            Product = "Mon logiciel",
            Executables = ["monlogiciel.exe"],
            Note = "Fiche d'exemple, a adapter puis a renommer.",
            Shortcuts =
            [
                new CatalogShortcut { Name = "Enregistrer", Keys = "Ctrl+S" },
                new CatalogShortcut { Name = "Rechercher", Keys = "Ctrl+F" }
            ]
        };

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true }));

        return path;
    }
}
