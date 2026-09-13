namespace StreamDeckForge.Core.Models;

/// <summary>
/// Une combinaison de touches normalisee : modificateurs + une touche principale
/// exprimee sous forme de jeton canonique ("S", "F5", "Delete", "OemComma"...).
/// </summary>
public sealed record KeyChord(bool Ctrl, bool Shift, bool Alt, bool Win, string Key)
{
    public bool HasModifier => Ctrl || Shift || Alt || Win;

    /// <summary>Libelle lisible, ex. "Ctrl + Shift + S".</summary>
    public string Display
    {
        get
        {
            var parts = new List<string>(4);
            if (Ctrl) parts.Add("Ctrl");
            if (Shift) parts.Add("Shift");
            if (Alt) parts.Add("Alt");
            if (Win) parts.Add("Win");
            parts.Add(Keys.KeyCatalog.DisplayName(Key));
            return string.Join(" + ", parts);
        }
    }

    public override string ToString() => Display;
}
