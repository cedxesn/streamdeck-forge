namespace StreamDeckForge.Core.StreamDeck;

/// <summary>
/// Un modele de Stream Deck : geometrie de la grille et code produit Elgato ecrit dans
/// le champ Device.Model du manifeste. Ce code identifie le materiel auquel le profil
/// se rattache ; il est modifiable dans l'interface si un boitier particulier attend
/// une autre valeur.
/// </summary>
public sealed record StreamDeckDevice(
    string Id,
    string DisplayName,
    int Columns,
    int Rows,
    string Model)
{
    public int KeyCount => Columns * Rows;

    public override string ToString() => $"{DisplayName} ({Columns}x{Rows})";
}

/// <summary>Modeles proposes dans l'interface.</summary>
public static class StreamDeckDevices
{
    public static readonly StreamDeckDevice Mini =
        new("mini", "Stream Deck Mini", 3, 2, "20GAI");

    public static readonly StreamDeckDevice Standard =
        new("standard", "Stream Deck (15 touches)", 5, 3, "20GAA");

    public static readonly StreamDeckDevice Mk2 =
        new("mk2", "Stream Deck MK.2", 5, 3, "20GBD");

    public static readonly StreamDeckDevice Xl =
        new("xl", "Stream Deck XL", 8, 4, "20GAT");

    public static IReadOnlyList<StreamDeckDevice> All { get; } = [Mini, Standard, Mk2, Xl];

    public static StreamDeckDevice ById(string id) =>
        All.FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? Standard;
}
