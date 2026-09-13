using System.Text.Json.Serialization;

namespace StreamDeckForge.Core.StreamDeck;

/// <summary>
/// Modele objet de manifest.json, tel que l'application Elgato Stream Deck l'attend.
/// Les noms de proprietes sont en PascalCase, comme dans les profils produits par
/// le logiciel lui-meme.
/// </summary>
public sealed class ProfileManifest
{
    public string Name { get; set; } = "Profil";

    public DeviceDescriptor Device { get; set; } = new();

    public string Version { get; set; } = "1.0";

    /// <summary>Actions indexees par position "colonne,ligne", origine en haut a gauche.</summary>
    public Dictionary<string, ProfileAction> Actions { get; set; } = [];
}

public sealed class DeviceDescriptor
{
    /// <summary>Numero de serie du boitier. Vide = le profil accepte tout boitier du modele.</summary>
    [JsonPropertyName("UUID")]
    public string Uuid { get; set; } = string.Empty;

    /// <summary>Code produit Elgato, voir <see cref="StreamDeckDevices"/>.</summary>
    public string Model { get; set; } = StreamDeckDevices.Standard.Model;
}

public sealed class ProfileAction
{
    /// <summary>Nom d'action affiche dans le logiciel.</summary>
    public string Name { get; set; } = "Hotkey";

    /// <summary>Type de controle. "Keypad" = une touche carree du boitier.</summary>
    public string Controller { get; set; } = "Keypad";

    public HotkeySettings Settings { get; set; } = new();

    public int State { get; set; }

    public List<ActionState> States { get; set; } = [];

    /// <summary>Identifiant de l'action integree Elgato ; ici le raccourci clavier.</summary>
    [JsonPropertyName("UUID")]
    public string Uuid { get; set; } = HotkeyActionUuid;

    /// <summary>Action "Raccourci clavier" fournie par l'application Stream Deck.</summary>
    public const string HotkeyActionUuid = "com.elgato.streamdeck.system.hotkey";
}

public sealed class HotkeySettings
{
    public List<HotkeyEntry> Hotkey { get; set; } = [];

    public bool IsMultiAction { get; set; }
}

/// <summary>Une combinaison de touches au format Elgato.</summary>
public sealed class HotkeyEntry
{
    public bool KeyCmd { get; set; }

    public bool KeyCtrl { get; set; }

    /// <summary>Masque de modificateurs : Shift 1, Ctrl 2, Alt 4, Win 8.</summary>
    public int KeyModifiers { get; set; }

    /// <summary>Touche Alt (nommee Option, l'application partage son format avec macOS).</summary>
    public bool KeyOption { get; set; }

    public bool KeyShift { get; set; }

    /// <summary>Code natif de la plateforme ; sur Windows on aligne sur VKeyCode.</summary>
    public int NativeCode { get; set; }

    /// <summary>Code de touche Qt, le socle graphique de l'application Stream Deck.</summary>
    [JsonPropertyName("QTKeyCode")]
    public int QtKeyCode { get; set; }

    /// <summary>Virtual-Key code Windows, celui qui est reellement rejoue.</summary>
    [JsonPropertyName("VKeyCode")]
    public int VKeyCode { get; set; }
}

/// <summary>Apparence d'un etat de touche.</summary>
public sealed class ActionState
{
    [JsonPropertyName("FFamily")]
    public string FontFamily { get; set; } = string.Empty;

    [JsonPropertyName("FSize")]
    public string FontSize { get; set; } = "10";

    [JsonPropertyName("FStyle")]
    public string FontStyle { get; set; } = string.Empty;

    [JsonPropertyName("FUnderline")]
    public string FontUnderline { get; set; } = "off";

    /// <summary>
    /// Chemin relatif d'une image embarquee dans le profil. Omis quand aucune image
    /// n'est fournie : l'application affiche alors l'icone par defaut de l'action.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Image { get; set; }

    public string Title { get; set; } = string.Empty;

    public string TitleAlignment { get; set; } = "middle";

    public string TitleColor { get; set; } = "#ffffff";

    public string TitleShow { get; set; } = string.Empty;
}
