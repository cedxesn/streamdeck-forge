using StreamDeckForge.Core.Keys;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Extraction;

/// <summary>
/// Methode 1 - statique. Lit les ressources RT_ACCELERATOR du binaire : c'est la table
/// que TranslateAccelerator consulte a l'execution, donc la liste exacte des raccourcis
/// cables dans l'application. Le nom de chaque commande est recherche dans la table de
/// chaines (convention MFC : "texte de barre d'etat\nInfobulle").
/// </summary>
public sealed class AcceleratorTableExtractor : IShortcutExtractor
{
    private const int AccelEntrySize = 8;

    private const ushort FVirtKey = 0x01;
    private const ushort FShift = 0x04;
    private const ushort FControl = 0x08;
    private const ushort FAlt = 0x10;
    private const ushort FLast = 0x80;

    /// <summary>Garde-fou pour le scan etendu : on ne veut pas parcourir 400 DLL.</summary>
    private const int MaxCompanionModules = 24;

    public ExtractionMethod Method => ExtractionMethod.AcceleratorTable;

    public string DisplayName => "Tables d'accelerateurs Win32 (statique)";

    public bool CanRun(ExtractionContext context) => context.Application.HasExecutable;

    public Task<ExtractionResult> ExtractAsync(
        ExtractionContext context,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Extract(context, cancellationToken), cancellationToken);

    private ExtractionResult Extract(ExtractionContext context, CancellationToken cancellationToken)
    {
        var executable = context.Application.ExecutablePath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            return ExtractionResult.Failed(Method, "Chemin de l'executable introuvable.");

        var shortcuts = new List<ShortcutDefinition>();
        var modulesRead = 0;

        foreach (var module in ModulesToScan(context))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var found = ReadModule(module);
            if (found.Count > 0)
                modulesRead++;

            shortcuts.AddRange(found);
        }

        if (shortcuts.Count == 0)
        {
            return ExtractionResult.Failed(
                Method,
                "Aucune table d'accelerateurs. L'application n'est probablement pas une " +
                "application Win32 classique (Electron, Qt, WinUI et .NET n'en publient pas).");
        }

        var deduplicated = Deduplicate(shortcuts);

        return ExtractionResult.Ok(
            Method,
            deduplicated,
            $"{deduplicated.Count} raccourci(s) lus dans {modulesRead} module(s).");
    }

    private static IEnumerable<string> ModulesToScan(ExtractionContext context)
    {
        var executable = context.Application.ExecutablePath!;
        yield return executable;

        // Ressources localisees : app.exe.mui dans un sous-dossier de langue.
        var directory = Path.GetDirectoryName(executable);
        if (directory is not null)
        {
            var muiName = Path.GetFileName(executable) + ".mui";
            // La langue de l'utilisateur d'abord : ses libelles sont ceux qu'il verra.
            var cultures = new[] { System.Globalization.CultureInfo.CurrentUICulture.Name, "en-US" };

            foreach (var culture in cultures.Distinct())
            {
                var mui = Path.Combine(directory, culture, muiName);
                if (File.Exists(mui))
                    yield return mui;
            }
        }

        if (!context.ScanCompanionModules || directory is null)
            yield break;

        var companions = SafeEnumerate(directory, "*.dll")
            .OrderByDescending(path => new FileInfo(path).Length)
            .Take(MaxCompanionModules);

        foreach (var dll in companions)
            yield return dll;
    }

    private static IEnumerable<string> SafeEnumerate(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private List<ShortcutDefinition> ReadModule(string modulePath)
    {
        var shortcuts = new List<ShortcutDefinition>();

        using var module = NativeResourceModule.TryOpen(modulePath);
        if (module is null)
            return shortcuts;

        var moduleName = Path.GetFileName(modulePath);

        foreach (var resourceName in module.EnumerateResourceNames(NativeResourceModule.RtAccelerator))
        {
            var data = module.Read(NativeResourceModule.RtAccelerator, resourceName);
            if (data is null)
                continue;

            shortcuts.AddRange(ParseTable(data, module, moduleName));
        }

        return shortcuts;
    }

    /// <summary>
    /// Une table est une suite d'ACCELTABLEENTRY de huit octets :
    /// WORD fFlags, WORD wAnsi (code touche), WORD wId (commande), WORD de bourrage.
    /// Le bit 0x80 de fFlags marque la derniere entree.
    /// </summary>
    private static IEnumerable<ShortcutDefinition> ParseTable(
        byte[] data,
        NativeResourceModule module,
        string moduleName)
    {
        for (var offset = 0; offset + AccelEntrySize <= data.Length; offset += AccelEntrySize)
        {
            var flags = BitConverter.ToUInt16(data, offset);
            var key = BitConverter.ToUInt16(data, offset + 2);
            var commandId = BitConverter.ToUInt16(data, offset + 4);

            var shortcut = BuildShortcut(flags, key, commandId, module, moduleName);
            if (shortcut is not null)
                yield return shortcut;

            if ((flags & FLast) != 0)
                yield break;
        }
    }

    private static ShortcutDefinition? BuildShortcut(
        ushort flags,
        ushort key,
        ushort commandId,
        NativeResourceModule module,
        string moduleName)
    {
        // Sans FVIRTKEY l'entree designe un caractere ASCII et non un code de touche :
        // la conversion vers un VK depend de la disposition clavier, on l'ignore.
        if ((flags & FVirtKey) == 0)
            return null;

        var codes = KeyCatalog.FromVirtualKey(key);
        if (codes is null)
            return null;

        var chord = new KeyChord(
            Ctrl: (flags & FControl) != 0,
            Shift: (flags & FShift) != 0,
            Alt: (flags & FAlt) != 0,
            Win: false,
            Key: codes.Token);

        // Une touche seule (F5, Delete) reste un raccourci valable sur un Stream Deck.
        var name = ResolveCommandName(module, commandId) ?? $"Commande {commandId}";

        return new ShortcutDefinition
        {
            Name = name,
            Chord = chord,
            Method = ExtractionMethod.AcceleratorTable,
            Origin = $"{moduleName} / RT_ACCELERATOR",
            Command = commandId.ToString()
        };
    }

    /// <summary>Au-dela, la chaine trouvee est un message et non un libelle de commande.</summary>
    private const int MaxPlausibleLabelLength = 32;

    /// <summary>
    /// MFC enregistre pour chaque identifiant de commande une chaine
    /// "texte de la barre d'etat\nlibelle court" ; le libelle court est le meilleur titre.
    ///
    /// Hors MFC, rien ne garantit que la table de chaines utilise les memes identifiants
    /// que la table d'accelerateurs : on y lit alors une chaine sans rapport. Notepad, par
    /// exemple, renvoie ainsi ses messages d'erreur. Une chaine sans le separateur MFC
    /// n'est donc retenue que si elle ressemble vraiment a un libelle de commande.
    /// </summary>
    private static string? ResolveCommandName(NativeResourceModule module, ushort commandId)
    {
        var raw = module.ReadString(commandId);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var parts = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Separateur present : convention MFC, le dernier segment est le libelle court.
        // Le controle de plausibilite s'applique quand meme, car certaines ressources
        // systeme contiennent des messages multilignes sans rapport avec une commande.
        var candidate = Clean(parts.Length > 1 ? parts[^1] : parts[0]);

        return candidate is not null && LooksLikeLabel(candidate) ? candidate : null;
    }

    private static string? Clean(string value)
    {
        var cleaned = value.Replace("&", string.Empty).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static bool LooksLikeLabel(string candidate)
    {
        if (candidate.Length > MaxPlausibleLabelLength)
            return false;

        // Marqueurs de format (%s, %1, %%) et tabulations : c'est un gabarit de message.
        if (candidate.Contains('%') || candidate.Contains('\t'))
            return false;

        // Une phrase ponctuee en son milieu n'est pas un libelle de bouton.
        if (candidate.Contains(". ") || candidate.Contains(", ") || candidate.Contains(" : "))
            return false;

        return candidate.Any(char.IsLetter);
    }

    private static List<ShortcutDefinition> Deduplicate(IEnumerable<ShortcutDefinition> shortcuts) =>
        shortcuts
            .GroupBy(s => s.DedupKey)
            .Select(group => group.OrderByDescending(s => s.Name.StartsWith("Commande ") ? 0 : 1).First())
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
