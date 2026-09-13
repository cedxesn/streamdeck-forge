using System.Diagnostics.CodeAnalysis;

namespace StreamDeckForge.Core.Keys;

/// <summary>Codes d'une touche pour les trois champs attendus par Elgato.</summary>
/// <param name="Token">Jeton canonique interne ("S", "F5", "OemComma").</param>
/// <param name="Display">Libelle affiche ("S", "F5", ",").</param>
/// <param name="VirtualKey">Virtual-Key code Windows (VK_*), champ VKeyCode.</param>
/// <param name="QtKey">Code Qt (enum Qt::Key), champ QTKeyCode.</param>
public sealed record KeyCodes(string Token, string Display, int VirtualKey, int QtKey)
{
    /// <summary>
    /// Champ NativeCode du manifeste. Sur Windows l'application Stream Deck rejoue le
    /// raccourci a partir de VKeyCode ; on aligne NativeCode dessus.
    /// </summary>
    public int NativeCode => VirtualKey;
}

/// <summary>
/// Table de correspondance jeton -> VK / Qt. Couvre les touches atteignables par une
/// table d'accelerateurs Win32 et par les fichiers de configuration usuels.
/// </summary>
public static class KeyCatalog
{
    private static readonly Dictionary<string, KeyCodes> ByToken =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<int, KeyCodes> ByVirtualKey = new();

    static KeyCatalog()
    {
        for (var c = 'A'; c <= 'Z'; c++)
            Add(new KeyCodes(c.ToString(), c.ToString(), c, c));

        for (var d = '0'; d <= '9'; d++)
            Add(new KeyCodes(d.ToString(), d.ToString(), d, d));

        // Qt::Key_F1 == 0x01000030, les suivantes se suivent.
        for (var i = 1; i <= 24; i++)
            Add(new KeyCodes($"F{i}", $"F{i}", 0x70 + i - 1, 0x01000030 + i - 1));

        // Pave numerique : Qt ne distingue pas les touches du pave (modificateur KeypadModifier).
        for (var i = 0; i <= 9; i++)
            Add(new KeyCodes($"NumPad{i}", $"Num {i}", 0x60 + i, '0' + i));

        Add(new KeyCodes("Space", "Space", 0x20, 0x20));
        Add(new KeyCodes("Enter", "Enter", 0x0D, 0x01000004));
        Add(new KeyCodes("Tab", "Tab", 0x09, 0x01000001));
        Add(new KeyCodes("Backspace", "Backspace", 0x08, 0x01000003));
        Add(new KeyCodes("Escape", "Esc", 0x1B, 0x01000000));
        Add(new KeyCodes("Insert", "Insert", 0x2D, 0x01000006));
        Add(new KeyCodes("Delete", "Delete", 0x2E, 0x01000007));
        Add(new KeyCodes("Home", "Home", 0x24, 0x01000010));
        Add(new KeyCodes("End", "End", 0x23, 0x01000011));
        Add(new KeyCodes("PageUp", "Page Up", 0x21, 0x01000016));
        Add(new KeyCodes("PageDown", "Page Down", 0x22, 0x01000017));
        Add(new KeyCodes("Left", "Left", 0x25, 0x01000012));
        Add(new KeyCodes("Up", "Up", 0x26, 0x01000013));
        Add(new KeyCodes("Right", "Right", 0x27, 0x01000014));
        Add(new KeyCodes("Down", "Down", 0x28, 0x01000015));
        Add(new KeyCodes("PrintScreen", "Print Screen", 0x2C, 0x01000009));
        Add(new KeyCodes("Pause", "Pause", 0x13, 0x01000008));
        Add(new KeyCodes("CapsLock", "Caps Lock", 0x14, 0x01000024));
        Add(new KeyCodes("NumLock", "Num Lock", 0x90, 0x01000025));
        Add(new KeyCodes("ScrollLock", "Scroll Lock", 0x91, 0x01000026));
        Add(new KeyCodes("Multiply", "Num *", 0x6A, 0x2A));
        Add(new KeyCodes("Add", "Num +", 0x6B, 0x2B));
        Add(new KeyCodes("Subtract", "Num -", 0x6D, 0x2D));
        Add(new KeyCodes("Decimal", "Num .", 0x6E, 0x2E));
        Add(new KeyCodes("Divide", "Num /", 0x6F, 0x2F));

        // Touches OEM (disposition US, la seule que l'application Stream Deck sait rejouer
        // de maniere deterministe a partir d'un VK).
        Add(new KeyCodes("OemSemicolon", ";", 0xBA, ';'));
        Add(new KeyCodes("OemPlus", "=", 0xBB, '='));
        Add(new KeyCodes("OemComma", ",", 0xBC, ','));
        Add(new KeyCodes("OemMinus", "-", 0xBD, '-'));
        Add(new KeyCodes("OemPeriod", ".", 0xBE, '.'));
        Add(new KeyCodes("OemQuestion", "/", 0xBF, '/'));
        Add(new KeyCodes("OemTilde", "`", 0xC0, '`'));
        Add(new KeyCodes("OemOpenBrackets", "[", 0xDB, '['));
        Add(new KeyCodes("OemPipe", "\\", 0xDC, '\\'));
        Add(new KeyCodes("OemCloseBrackets", "]", 0xDD, ']'));
        Add(new KeyCodes("OemQuotes", "'", 0xDE, '\''));
    }

    private static void Add(KeyCodes codes)
    {
        ByToken[codes.Token] = codes;
        ByVirtualKey.TryAdd(codes.VirtualKey, codes);
    }

    public static bool TryGet(string token, [NotNullWhen(true)] out KeyCodes? codes) =>
        ByToken.TryGetValue(Normalize(token), out codes);

    public static KeyCodes? FromVirtualKey(int vk) =>
        ByVirtualKey.TryGetValue(vk, out var codes) ? codes : null;

    public static string DisplayName(string token) =>
        TryGet(token, out var codes) ? codes.Display : token;

    /// <summary>Ramene les alias usuels (Esc, Del, Return, PgUp, ",", ...) au jeton canonique.</summary>
    public static string Normalize(string token)
    {
        var t = token.Trim();
        return t.ToLowerInvariant() switch
        {
            "esc" => "Escape",
            "del" => "Delete",
            "ins" => "Insert",
            "return" => "Enter",
            "cr" => "Enter",
            "back" or "bksp" or "bs" => "Backspace",
            "pgup" or "prior" or "page up" => "PageUp",
            "pgdn" or "pgdown" or "next" or "page down" => "PageDown",
            "spacebar" or "space" => "Space",
            "arrowleft" => "Left",
            "arrowright" => "Right",
            "arrowup" => "Up",
            "arrowdown" => "Down",
            "prtsc" or "printscrn" or "snapshot" => "PrintScreen",
            ";" => "OemSemicolon",
            "=" or "plus" => "OemPlus",
            "," => "OemComma",
            "-" or "minus" => "OemMinus",
            "." => "OemPeriod",
            "/" => "OemQuestion",
            "`" => "OemTilde",
            "[" => "OemOpenBrackets",
            "\\" => "OemPipe",
            "]" => "OemCloseBrackets",
            "'" => "OemQuotes",
            _ => t
        };
    }

    /// <summary>Toutes les touches connues, pour l'edition manuelle dans l'interface.</summary>
    public static IReadOnlyCollection<KeyCodes> All => ByToken.Values;
}
