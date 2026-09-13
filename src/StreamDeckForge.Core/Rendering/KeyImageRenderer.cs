using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StreamDeckForge.Core.Models;
using StreamDeckForge.Core.StreamDeck;

namespace StreamDeckForge.Core.Rendering;

/// <summary>Famille de commandes : determine la couleur et le pictogramme de la touche.</summary>
/// <param name="Name">Libelle de la famille, affiche dans l'interface.</param>
/// <param name="Glyph">Point de code dans Segoe Fluent Icons.</param>
/// <param name="Accent">Couleur d'accent de la famille.</param>
/// <param name="Keywords">Mots declencheurs, en francais et en anglais.</param>
public sealed record KeyCategory(string Name, string Glyph, Color Accent, string[] Keywords);

/// <summary>
/// Fabrique les visuels des touches : un fond sombre, un pictogramme choisi d'apres le
/// nom de la commande, le titre et la combinaison. Les commandes d'une meme famille
/// partagent couleur et pictogramme, ce qui rend la grille lisible d'un coup d'oeil.
///
/// Le rendu passe par WPF (DrawingVisual + RenderTargetBitmap) : pas de dependance
/// graphique supplementaire, et les polices d'icones livrees avec Windows suffisent.
/// </summary>
public static class KeyImageRenderer
{
    /// <summary>Resolution des visuels de touche du Stream Deck XL.</summary>
    public const int ImageSize = 144;

    private static readonly FontFamily IconFont =
        new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private static readonly FontFamily TextFont = new("Segoe UI");

    private static readonly Color Background = Color.FromRgb(0x18, 0x18, 0x1B);

    /// <summary>
    /// Familles testees dans l'ordre : la premiere dont un mot declencheur apparait
    /// dans le libelle ou dans la commande l'emporte. Les familles les plus specifiques
    /// viennent donc avant les plus generales.
    /// </summary>
    private static readonly KeyCategory[] Categories =
    [
        new("Enregistrement", "\uE7C8", Color.FromRgb(0xE0, 0x4F, 0x5F),
            ["record", "arm", "punch"]),

        new("Reperes", "\uE7C1", Color.FromRgb(0xE0, 0xA8, 0x4C),
            ["marker", "repere", "location", "playhead", "cue"]),

        new("Boucle", "\uE8EE", Color.FromRgb(0x4C, 0xC3, 0x8A),
            ["loop", "boucle", "repeat", "cycle"]),

        new("Pause", "\uE769", Color.FromRgb(0x4C, 0xC3, 0x8A),
            ["pause", "stop"]),

        new("Lecture", "\uE768", Color.FromRgb(0x4C, 0xC3, 0x8A),
            ["play", "roll", "transport", "lecture", "start"]),

        new("Fichier", "\uE74E", Color.FromRgb(0x4C, 0x9A, 0xE0),
            ["save", "enregistrer", "sauver"]),

        new("Fichier", "\uE8E5", Color.FromRgb(0x4C, 0x9A, 0xE0),
            ["open", "ouvrir", "import", "load"]),

        new("Fichier", "\uE72D", Color.FromRgb(0x4C, 0x9A, 0xE0),
            ["export", "exporter", "bounce", "render", "share"]),

        new("Fichier", "\uE749", Color.FromRgb(0x4C, 0x9A, 0xE0),
            ["print", "imprimer"]),

        new("Fichier", "\uE710", Color.FromRgb(0x4C, 0x9A, 0xE0),
            ["new", "nouveau", "create", "add", "ajouter"]),

        new("Edition", "\uE8C8", Color.FromRgb(0xA9, 0x7C, 0xE8),
            ["copy", "copier", "duplicate", "dupliquer"]),

        new("Edition", "\uE8C6", Color.FromRgb(0xA9, 0x7C, 0xE8),
            ["cut", "couper", "trim", "split"]),

        new("Edition", "\uE77F", Color.FromRgb(0xA9, 0x7C, 0xE8),
            ["paste", "coller"]),

        new("Edition", "\uE7A7", Color.FromRgb(0xA9, 0x7C, 0xE8),
            ["undo", "annuler"]),

        new("Edition", "\uE7A6", Color.FromRgb(0xA9, 0x7C, 0xE8),
            ["redo", "retablir", "repeter"]),

        new("Edition", "\uE74D", Color.FromRgb(0xD8, 0x6A, 0x6A),
            ["delete", "supprimer", "remove", "effacer", "clear"]),

        new("Selection", "\uE8B3", Color.FromRgb(0xA9, 0x7C, 0xE8),
            ["select", "selection", "selectionner"]),

        new("Recherche", "\uE721", Color.FromRgb(0x3F, 0xB9, 0xB9),
            ["find", "search", "rechercher", "chercher", "replace", "remplacer"]),

        new("Navigation", "\uE8A3", Color.FromRgb(0x3F, 0xB9, 0xB9),
            ["zoom in", "zoom avant", "agrandir"]),

        new("Navigation", "\uE71F", Color.FromRgb(0x3F, 0xB9, 0xB9),
            ["zoom out", "zoom arriere", "reduire", "zoom"]),

        new("Navigation", "\uE893", Color.FromRgb(0x3F, 0xB9, 0xB9),
            ["next", "suivant", "forward", "forwards", "avancer", "nudge"]),

        new("Navigation", "\uE892", Color.FromRgb(0x3F, 0xB9, 0xB9),
            ["previous", "precedent", "back", "backward", "backwards", "rewind", "reculer"]),

        new("Mise en forme", "\uE8DD", Color.FromRgb(0xE8, 0x7C, 0xB0),
            ["bold", "gras"]),

        new("Mise en forme", "\uE8DB", Color.FromRgb(0xE8, 0x7C, 0xB0),
            ["italic", "italique"]),

        new("Mise en forme", "\uE8DC", Color.FromRgb(0xE8, 0x7C, 0xB0),
            ["underline", "souligner", "souligne"]),

        new("Son", "\uE74F", Color.FromRgb(0xE0, 0xA8, 0x4C),
            ["mute", "muet", "solo", "silence"]),

        new("Son", "\uE767", Color.FromRgb(0xE0, 0xA8, 0x4C),
            ["volume", "gain", "fader", "level"]),

        new("Fermer", "\uE8BB", Color.FromRgb(0xD8, 0x6A, 0x6A),
            ["fermer", "close", "quitter", "quit", "exit"]),

        new("Feuille", "\uE7C3", Color.FromRgb(0x4C, 0x9A, 0xE0),
            ["feuille", "sheet", "classeur", "workbook", "onglet", "tab", "document", "diapositive", "slide", "page", "presentation"]),

        new("Lien", "\uE71B", Color.FromRgb(0x3F, 0xB9, 0xB9),
            ["lien", "link", "hypertexte", "hyperlink", "url"]),

        new("Filtre", "\uE71C", Color.FromRgb(0xE0, 0xA8, 0x4C),
            ["filtre", "filtrer", "filter", "tri", "trier", "sort"]),

        new("Modifier", "\uE70F", Color.FromRgb(0xA9, 0x7C, 0xE8),
            ["modifier", "edit", "editer", "renommer", "rename", "saisie"]),

        new("Calcul", "\uE8EF", Color.FromRgb(0x3F, 0xB9, 0xB9),
            ["calculer", "calcul", "calculate", "somme", "formule", "formula"]),

        new("Debut", "\uE80F", Color.FromRgb(0x8A, 0x93, 0xA6),
            ["debut", "home", "accueil", "fin", "end", "atteindre", "goto"]),

        new("Police", "\uE8D2", Color.FromRgb(0xE8, 0x7C, 0xB0),
            ["police", "font", "caractere", "taille"]),

        new("Orthographe", "\uE8C1", Color.FromRgb(0x4C, 0xC3, 0x8A),
            ["orthographe", "spelling", "grammaire", "dictionnaire", "langue"]),

        new("Plein ecran", "\uE740", Color.FromRgb(0x8A, 0x93, 0xA6),
            ["plein", "fullscreen", "ecran", "screen", "maximiser", "diaporama"]),

        new("Message", "\uE715", Color.FromRgb(0x4C, 0x9A, 0xE0),
            ["message", "mail", "courrier", "envoyer", "send", "repondre", "reply", "transferer", "forward"]),

        new("Agenda", "\uE787", Color.FromRgb(0xE0, 0xA8, 0x4C),
            ["calendrier", "calendar", "rendez", "reunion", "meeting", "tache", "task", "contact"]),

        new("Fenetre", "\uE737", Color.FromRgb(0x8A, 0x93, 0xA6),
            ["window", "fenetre", "mixer", "editor", "panel", "view", "affichage", "toggle"]),

        new("Reglages", "\uE713", Color.FromRgb(0x8A, 0x93, 0xA6),
            ["settings", "preferences", "options", "config"]),

        new("Actualiser", "\uE72C", Color.FromRgb(0x8A, 0x93, 0xA6),
            ["refresh", "actualiser", "reload", "recharger"]),

        new("Aide", "\uE897", Color.FromRgb(0x8A, 0x93, 0xA6),
            ["help", "aide", "cheat", "manual", "about"])
    ];

    /// <summary>Famille retenue quand aucun mot declencheur ne correspond.</summary>
    private static readonly KeyCategory Fallback =
        new("Raccourci", "\uE765", Color.FromRgb(0x6E, 0x7B, 0x8F), []);

    /// <summary>Choisit la famille d'une commande d'apres son libelle et son identifiant.</summary>
    public static KeyCategory Categorize(ShortcutDefinition shortcut, string? title = null)
    {
        var haystack = Tokenize($"{title ?? shortcut.Name} {shortcut.Command}");

        return Categories.FirstOrDefault(category =>
            category.Keywords.Any(keyword =>
                haystack.Contains($" {keyword} ", StringComparison.Ordinal)))
            ?? Fallback;
    }

    /// <summary>
    /// Reduit un libelle a une suite de mots encadree d'espaces, pour comparer mot a mot.
    /// Indispensable : en simple sous-chaine, "playhead" declenchait la famille Lecture
    /// a cause de "play", et "Loop" heritait du pictogramme de lecture via "transport".
    /// </summary>
    private static string Tokenize(string text)
    {
        var cleaned = new string(text
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ')
            .ToArray());

        return $" {string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries))} ";
    }

    /// <summary>Genere et attache les visuels de toutes les touches d'une grille.</summary>
    public static void Apply(ProfileLayout layout)
    {
        foreach (var assignment in layout.Assignments)
        {
            var category = Categorize(assignment.Shortcut, assignment.Title);

            assignment.ImagePng = Render(
                assignment.Title ?? assignment.Shortcut.Name,
                assignment.Shortcut.Chord.Display,
                category);
        }
    }

    public static byte[] Render(string title, string keys, KeyCategory category)
    {
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            Draw(context, title, keys, category);
        }

        var bitmap = new RenderTargetBitmap(
            ImageSize, ImageSize, 96, 96, PixelFormats.Pbgra32);

        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Pictogrammes reellement presents dans la police installee. Les jeux d'icones de
    /// Windows varient d'une version a l'autre : un point de code absent s'afficherait
    /// en rectangle vide, ce qui est pire que le pictogramme generique.
    /// </summary>
    private static readonly Lazy<GlyphTypeface?> ResolvedIconFont = new(() =>
    {
        var typeface = new Typeface(
            IconFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        return typeface.TryGetGlyphTypeface(out var glyphTypeface) ? glyphTypeface : null;
    });

    private static bool IsGlyphAvailable(string glyph)
    {
        if (string.IsNullOrEmpty(glyph))
            return false;

        var font = ResolvedIconFont.Value;

        // Police introuvable : on laisse passer, le rendu fera au mieux.
        return font is null || font.CharacterToGlyphMap.ContainsKey(glyph[0]);
    }

    private static void Draw(DrawingContext context, string title, string keys, KeyCategory category)
    {
        var accent = new SolidColorBrush(category.Accent);
        accent.Freeze();

        var background = new SolidColorBrush(Background);
        background.Freeze();

        context.DrawRectangle(background, null, new Rect(0, 0, ImageSize, ImageSize));

        // Voile de la couleur de famille en haut : identifie le groupe sans masquer le texte.
        var wash = new LinearGradientBrush(
            Color.FromArgb(0x40, category.Accent.R, category.Accent.G, category.Accent.B),
            Color.FromArgb(0x00, category.Accent.R, category.Accent.G, category.Accent.B),
            new Point(0.5, 0),
            new Point(0.5, 1));
        wash.Freeze();
        context.DrawRectangle(wash, null, new Rect(0, 0, ImageSize, ImageSize));

        var glyph = IsGlyphAvailable(category.Glyph) ? category.Glyph : Fallback.Glyph;
        DrawCentered(context, glyph, IconFont, 44, accent, 20);
        DrawWrapped(context, title, 15, Brushes.White, 74, maxLines: 2);
        DrawCentered(context, keys, TextFont, 12, accent, ImageSize - 26);

        // Liseré bas : rappel discret de la couleur de famille.
        context.DrawRectangle(accent, null, new Rect(0, ImageSize - 4, ImageSize, 4));
    }

    private static void DrawCentered(
        DrawingContext context,
        string text,
        FontFamily family,
        double size,
        Brush brush,
        double top)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var formatted = Build(text, family, size, brush);
        formatted.MaxTextWidth = ImageSize - 8;
        formatted.MaxLineCount = 1;
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        formatted.TextAlignment = TextAlignment.Center;

        context.DrawText(formatted, new Point(4, top));
    }

    private static void DrawWrapped(
        DrawingContext context,
        string text,
        double size,
        Brush brush,
        double top,
        int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var formatted = Build(text, TextFont, size, brush);
        formatted.MaxTextWidth = ImageSize - 12;
        formatted.MaxLineCount = maxLines;
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        formatted.TextAlignment = TextAlignment.Center;

        context.DrawText(formatted, new Point(6, top));
    }

    private static FormattedText Build(string text, FontFamily family, double size, Brush brush) =>
        new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            size,
            brush,
            pixelsPerDip: 1.0);
}
