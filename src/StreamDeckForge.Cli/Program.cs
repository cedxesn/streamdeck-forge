using StreamDeckForge.Core.Discovery;
using StreamDeckForge.Core.Extraction;
using StreamDeckForge.Core.Keys;
using StreamDeckForge.Core.Models;
using StreamDeckForge.Core.Rendering;
using StreamDeckForge.Core.StreamDeck;

namespace StreamDeckForge.Cli;

/// <summary>
/// Interface en ligne de commande de StreamDeckForge. Elle expose les memes briques que
/// l'application graphique et sert egalement d'outil de verification : la commande
/// "selftest" produit puis relit un profil complet sans aucune intervention.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "scan" => Scan(args),
                "extract" => Extract(args).GetAwaiter().GetResult(),
                "export" => Export(args).GetAwaiter().GetResult(),
                "verify" => Verify(args),
                "selftest" => SelfTest(),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Erreur : {ex.Message}");
            return 2;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            StreamDeckForge - generation de profils Stream Deck a partir des raccourcis
            clavier des applications installees.

            Commandes :
              scan [--query <texte>]              Liste les applications installees.
              extract --app <nom|chemin.exe>      Extrait les raccourcis d'une application.
                      [--methods accel,uia,config]
                      [--deep]                    Etend la lecture aux DLL voisines.
              export --app <nom|chemin.exe>       Produit un fichier .streamDeckProfile.
                     --out <fichier>
                     [--device mini|standard|mk2|xl]
                     [--name <nom du profil>]
                     [--methods accel,uia,config]
                     [--icons]                    Genere les visuels des touches.
              verify <fichier.streamDeckProfile>  Controle la validite d'un profil.
              selftest                            Produit et relit un profil de test.
            """);
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Commande inconnue : {command}");
        PrintUsage();
        return 1;
    }

    private static int Scan(string[] args)
    {
        var catalog = new ApplicationCatalog();
        var result = catalog.ScanAsync().GetAwaiter().GetResult();

        var applications = ApplicationCatalog
            .Search(result.Applications, Option(args, "--query"))
            .ToList();

        foreach (var app in applications)
            Console.WriteLine($"{app.Name,-50} {app.ExecutablePath ?? "(chemin inconnu)"}");

        Console.WriteLine();
        Console.WriteLine(
            $"{applications.Count} application(s) affichees sur {result.Applications.Count} " +
            $"detectees en {result.Duration.TotalMilliseconds:F0} ms.");

        return 0;
    }

    private static async Task<int> Extract(string[] args)
    {
        var app = ResolveApplication(args);
        if (app is null)
            return 1;

        var aggregated = await RunExtraction(app, args).ConfigureAwait(false);

        foreach (var result in aggregated.Results)
        {
            Console.WriteLine($"[{(result.Succeeded ? "OK " : "KO ")}] {result.Method} : {result.Message}");
        }

        Console.WriteLine();
        foreach (var shortcut in aggregated.Shortcuts)
            Console.WriteLine($"  {shortcut.Chord.Display,-28} {shortcut.Name}  ({shortcut.Origin})");

        Console.WriteLine();
        Console.WriteLine($"{aggregated.Shortcuts.Count} raccourci(s) retenus.");

        return aggregated.AnyMethodSucceeded ? 0 : 3;
    }

    private static async Task<int> Export(string[] args)
    {
        var app = ResolveApplication(args);
        if (app is null)
            return 1;

        var output = Option(args, "--out");
        if (string.IsNullOrWhiteSpace(output))
        {
            Console.Error.WriteLine("--out est obligatoire.");
            return 1;
        }

        var device = StreamDeckDevices.ById(Option(args, "--device") ?? "standard");
        var profileName = Option(args, "--name") ?? app.Name;

        var aggregated = await RunExtraction(app, args).ConfigureAwait(false);
        if (aggregated.Shortcuts.Count == 0)
        {
            Console.Error.WriteLine("Aucun raccourci extrait, rien a exporter.");
            return 3;
        }

        var layout = new ProfileLayout(device, profileName);
        var overflow = layout.AutoFill(aggregated.Shortcuts);

        if (args.Contains("--icons"))
            KeyImageRenderer.Apply(layout);

        var exported = StreamDeckProfileWriter.Write(layout, output);
        var (valid, message) = StreamDeckProfileWriter.Verify(exported.FilePath);

        Console.WriteLine($"Ecrit : {exported.FilePath}");
        Console.WriteLine($"Dossier interne : {exported.ProfileFolderName}");
        Console.WriteLine($"Controle : {message}");

        if (overflow > 0)
        {
            Console.WriteLine(
                $"Attention : {overflow} raccourci(s) au-dela des {device.KeyCount} touches " +
                $"du modele {device.DisplayName} n'ont pas ete places.");
        }

        return valid ? 0 : 4;
    }

    private static int Verify(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Indiquez le fichier a verifier.");
            return 1;
        }

        var (valid, message) = StreamDeckProfileWriter.Verify(args[1]);
        Console.WriteLine(message);
        return valid ? 0 : 4;
    }

    /// <summary>
    /// Produit un profil a partir d'un jeu de raccourcis connu, puis le relit. Permet de
    /// valider la chaine de generation sans dependre d'une application installee.
    /// </summary>
    private static int SelfTest()
    {
        var samples = new (string Name, string Keys)[]
        {
            ("Enregistrer", "Ctrl+S"),
            ("Enregistrer sous", "Ctrl+Shift+S"),
            ("Copier", "Ctrl+C"),
            ("Coller", "Ctrl+V"),
            ("Rechercher", "Ctrl+F"),
            ("Actualiser", "F5"),
            ("Supprimer", "Delete"),
            ("Capture d'ecran", "Win+Shift+S"),
            ("Commentaire", "Ctrl+OemQuestion")
        };

        var shortcuts = new List<ShortcutDefinition>();

        foreach (var (name, keys) in samples)
        {
            if (!KeyChordParser.TryParse(keys, out var chord))
            {
                Console.Error.WriteLine($"Echec d'analyse de la combinaison : {keys}");
                return 4;
            }

            shortcuts.Add(new ShortcutDefinition
            {
                Name = name,
                Chord = chord,
                Method = ExtractionMethod.ConfigFile,
                Origin = "selftest"
            });
        }

        var failures = 0;

        foreach (var device in StreamDeckDevices.All)
        {
            var layout = new ProfileLayout(device, $"Selftest {device.DisplayName}");
            var overflow = layout.AutoFill(shortcuts);

            // Le rendu des visuels fait partie de la chaine a valider.
            KeyImageRenderer.Apply(layout);

            var path = Path.Combine(
                Path.GetTempPath(),
                StreamDeckProfileWriter.SuggestFileName($"selftest-{device.Id}"));

            var exported = StreamDeckProfileWriter.Write(layout, path);
            var (valid, message) = StreamDeckProfileWriter.Verify(exported.FilePath);

            Console.WriteLine(
                $"[{(valid ? "OK " : "KO ")}] {device.DisplayName,-26} " +
                $"{exported.ActionCount} action(s), {overflow} en trop -> {message}");

            if (!valid)
                failures++;
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "Chaine de generation validee."
            : $"{failures} modele(s) en echec.");

        return failures == 0 ? 0 : 4;
    }

    private static async Task<AggregatedExtraction> RunExtraction(AppInfo app, string[] args)
    {
        var context = new ExtractionContext
        {
            Application = app,
            ScanCompanionModules = args.Contains("--deep"),
            ExpandMenus = !args.Contains("--no-expand")
        };

        var service = new ShortcutExtractionService();
        return await service.ExtractAsync(context, ParseMethods(Option(args, "--methods")))
            .ConfigureAwait(false);
    }

    private static IReadOnlyCollection<ExtractionMethod> ParseMethods(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Enum.GetValues<ExtractionMethod>();

        var methods = new List<ExtractionMethod>();

        foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token.Trim().ToLowerInvariant())
            {
                case "accel" or "accelerator":
                    methods.Add(ExtractionMethod.AcceleratorTable);
                    break;
                case "uia" or "automation":
                    methods.Add(ExtractionMethod.UiAutomation);
                    break;
                case "config" or "files":
                    methods.Add(ExtractionMethod.ConfigFile);
                    break;
            }
        }

        return methods.Count == 0 ? Enum.GetValues<ExtractionMethod>() : methods;
    }

    /// <summary>Accepte un chemin d'executable direct ou un nom a chercher dans le catalogue.</summary>
    private static AppInfo? ResolveApplication(string[] args)
    {
        var value = Option(args, "--app");
        if (string.IsNullOrWhiteSpace(value))
        {
            Console.Error.WriteLine("--app est obligatoire.");
            return null;
        }

        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(value))
            return ApplicationCatalog.FromExecutable(value);

        var catalog = new ApplicationCatalog();
        var scan = catalog.ScanAsync().GetAwaiter().GetResult();

        var matches = ApplicationCatalog.Search(scan.Applications, value).ToList();

        if (matches.Count == 0)
        {
            Console.Error.WriteLine($"Aucune application ne correspond a '{value}'.");
            return null;
        }

        var app = matches
            .OrderByDescending(a => a.HasExecutable)
            .ThenBy(a => a.Name.Length)
            .First();

        if (ApplicationCatalog.ResolveExecutable(app) is null)
        {
            Console.Error.WriteLine($"Executable introuvable pour '{app.Name}'.");
            return null;
        }

        Console.WriteLine($"Application : {app.Name} -> {app.ExecutablePath}");
        return app;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.FindIndex(args, a =>
            a.Equals(name, StringComparison.OrdinalIgnoreCase));

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
