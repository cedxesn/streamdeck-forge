namespace StreamDeckForge.Core.Models;

/// <summary>Origine d'une application decouverte sur la machine.</summary>
public enum AppSource
{
    Registry,
    StartMenu,
    Manual
}

/// <summary>Une application installee (ou ajoutee manuellement) candidate a l'extraction.</summary>
public sealed class AppInfo
{
    public required string Name { get; init; }

    /// <summary>Chemin complet vers l'executable. Peut etre null si le registre ne l'expose pas.</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Chemin du fichier qui porte l'icone (exe ou .lnk / ressource "app.exe,3").</summary>
    public string? IconPath { get; set; }

    public string? Publisher { get; init; }
    public string? Version { get; init; }
    public string? InstallLocation { get; init; }

    public AppSource Source { get; init; } = AppSource.Registry;

    /// <summary>Cle utilisee pour dedupliquer les resultats registre / menu Demarrer.</summary>
    public string DedupKey =>
        !string.IsNullOrWhiteSpace(ExecutablePath)
            ? Path.GetFullPath(ExecutablePath).ToLowerInvariant()
            : Name.Trim().ToLowerInvariant();

    public bool HasExecutable =>
        !string.IsNullOrWhiteSpace(ExecutablePath) && File.Exists(ExecutablePath);

    public override string ToString() => $"{Name} ({ExecutablePath ?? "chemin inconnu"})";
}
