using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace StreamDeckForge.Core.StreamDeck;

/// <summary>Ce qui a ete ecrit, pour l'affichage du compte rendu.</summary>
public sealed record ProfileExportResult(string FilePath, string ProfileFolderName, int ActionCount);

/// <summary>
/// Ecrit un fichier .streamDeckProfile. C'est une archive Zip contenant un dossier
/// "&lt;GUID&gt;.sdProfile" avec son manifest.json, arborescence attendue par la fonction
/// d'importation de l'application Elgato Stream Deck.
/// </summary>
public static class StreamDeckProfileWriter
{
    public const string FileExtension = ".streamDeckProfile";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,

        // Les titres contiennent des accents et des retours a la ligne ; sans cet
        // encodeur System.Text.Json les echapperait en \uXXXX, illisible a la relecture.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static ProfileExportResult Write(
        ProfileLayout layout,
        string targetPath,
        string? deviceSerial = null)
    {
        var manifest = StreamDeckProfileBuilder.Build(layout, deviceSerial);
        var folderName = $"{Guid.NewGuid().ToString().ToUpperInvariant()}.sdProfile";

        var directory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using (var stream = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteManifest(archive, folderName, manifest);
            WriteImages(archive, folderName, layout);
        }

        return new ProfileExportResult(
            Path.GetFullPath(targetPath),
            folderName,
            manifest.Actions.Count);
    }

    private static void WriteManifest(
        ZipArchive archive,
        string folderName,
        ProfileManifest manifest)
    {
        var entry = archive.CreateEntry($"{folderName}/manifest.json", CompressionLevel.Optimal);

        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));
        writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static void WriteImages(ZipArchive archive, string folderName, ProfileLayout layout)
    {
        foreach (var assignment in layout.Assignments.Where(a => a.ImagePng is not null))
        {
            var path = StreamDeckProfileBuilder.ImagePathFor(assignment);
            var entry = archive.CreateEntry($"{folderName}/{path}", CompressionLevel.Optimal);

            using var entryStream = entry.Open();
            entryStream.Write(assignment.ImagePng!, 0, assignment.ImagePng!.Length);
        }
    }

    /// <summary>Nom de fichier propose : nom du profil nettoye des caracteres interdits.</summary>
    public static string SuggestFileName(string profileName)
    {
        var cleaned = new string(profileName
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)
            .ToArray())
            .Trim();

        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "Profil";

        return cleaned + FileExtension;
    }

    /// <summary>
    /// Relit une archive produite ici et verifie qu'elle contient bien un manifeste
    /// deserialisable. Utilise par l'exportation pour ne jamais livrer un fichier
    /// que l'application Elgato refuserait.
    /// </summary>
    public static (bool Valid, string Message) Verify(string profilePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(profilePath);

            var manifestEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase));

            if (manifestEntry is null)
                return (false, "Archive sans manifest.json.");

            var folder = manifestEntry.FullName.Split('/')[0];
            if (!folder.EndsWith(".sdProfile", StringComparison.OrdinalIgnoreCase))
                return (false, $"Le dossier racine '{folder}' ne se termine pas par .sdProfile.");

            using var entryStream = manifestEntry.Open();
            var manifest = JsonSerializer.Deserialize<ProfileManifest>(entryStream);

            if (manifest is null)
                return (false, "manifest.json illisible.");

            if (manifest.Actions.Count == 0)
                return (false, "Le profil ne contient aucune action.");

            foreach (var (position, action) in manifest.Actions)
            {
                if (action.Settings.Hotkey.Count == 0)
                    return (false, $"La touche {position} n'a pas de combinaison.");

                if (action.Settings.Hotkey[0].VKeyCode == 0)
                    return (false, $"La touche {position} a un VKeyCode nul.");
            }

            return (true, $"Profil valide : {manifest.Actions.Count} action(s) dans {folder}.");
        }
        catch (InvalidDataException ex)
        {
            return (false, $"Archive illisible : {ex.Message}");
        }
        catch (JsonException ex)
        {
            return (false, $"Manifeste invalide : {ex.Message}");
        }
    }
}
