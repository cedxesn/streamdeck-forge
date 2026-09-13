using System.Text;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.Core.Keys;

/// <summary>
/// Analyse les ecritures textuelles de raccourcis rencontrees dans les menus Windows
/// et les fichiers de configuration ("Ctrl+Shift+S", "ctrl+shift+s", "Ctrl Maj S").
/// </summary>
public static class KeyChordParser
{
    private static readonly char[] Separators = ['+', '-', ' ', '\t'];

    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default!;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Accords a deux temps facon VS Code ("Ctrl+K Ctrl+S") : une touche Stream Deck
        // n'emet qu'une seule combinaison, on les ecarte.
        if (IsChordSequence(text))
            return false;

        bool ctrl = false, shift = false, alt = false, win = false;
        string? key = null;

        foreach (var raw in Split(text))
        {
            var token = raw.Trim();
            if (token.Length == 0)
                continue;

            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control" or "ctl" or "strg":
                    ctrl = true;
                    continue;
                case "shift" or "maj" or "shft":
                    shift = true;
                    continue;
                case "alt" or "menu" or "option":
                    alt = true;
                    continue;
                case "win" or "windows" or "meta" or "super" or "cmd":
                    win = true;
                    continue;
                case "altgr":
                    ctrl = true;
                    alt = true;
                    continue;
            }

            // Deux touches principales : ecriture non supportee.
            if (key is not null)
                return false;

            key = KeyCatalog.Normalize(token);
        }

        if (key is null || !KeyCatalog.TryGet(key, out _))
            return false;

        chord = new KeyChord(ctrl, shift, alt, win, key);
        return true;
    }

    private static bool IsChordSequence(string text)
    {
        // Deux groupes contenant chacun un "+" separes par une espace.
        // "Ctrl+Shift+S" -> non ; "Ctrl+K Ctrl+S" -> oui.
        var groups = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return groups.Length > 1 && groups.Count(g => g.Contains('+')) > 1;
    }

    private static List<string> Split(string text)
    {
        // Decoupage sur +, - et espace. La touche principale peut elle-meme etre "+" ou
        // "-" ("Ctrl++") : le dernier caractere n'est donc jamais traite en separateur.
        var trimmed = text.Trim();
        var parts = new List<string>();
        var current = new StringBuilder();

        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            var isSeparator = Array.IndexOf(Separators, c) >= 0;
            var isLast = i == trimmed.Length - 1;

            if (isSeparator && !isLast)
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            parts.Add(current.ToString());

        return parts;
    }
}
