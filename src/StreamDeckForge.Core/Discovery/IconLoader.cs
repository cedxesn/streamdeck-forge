using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace StreamDeckForge.Core.Discovery;

/// <summary>
/// Extraction d'icones sans dependre de System.Drawing.Common (paquet NuGet depuis .NET 7).
/// On passe par user32!PrivateExtractIcons, qui sait rendre une taille arbitraire, puis
/// par Imaging.CreateBitmapSourceFromHIcon de PresentationCore.
/// </summary>
public static class IconLoader
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int PrivateExtractIcons(
        string szFileName,
        int nIconIndex,
        int cxIcon,
        int cyIcon,
        IntPtr[] phicon,
        int[] piconid,
        int nIcons,
        int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static readonly Dictionary<string, BitmapSource?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Icone d'un executable, mise en cache par chemin et par taille.</summary>
    public static BitmapSource? Load(string? path, int size = 32)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var cacheKey = $"{path}|{size}";

        lock (Cache)
        {
            if (Cache.TryGetValue(cacheKey, out var cached))
                return cached;
        }

        var icon = Extract(path, size);
        icon?.Freeze();

        lock (Cache)
        {
            Cache[cacheKey] = icon;
        }

        return icon;
    }

    private static BitmapSource? Extract(string path, int size)
    {
        if (!File.Exists(path))
            return null;

        var handles = new IntPtr[1];
        var ids = new int[1];

        try
        {
            var count = PrivateExtractIcons(path, 0, size, size, handles, ids, 1, 0);
            if (count <= 0 || handles[0] == IntPtr.Zero)
                return null;

            return Imaging.CreateBitmapSourceFromHIcon(
                handles[0],
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        catch (COMException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        finally
        {
            if (handles[0] != IntPtr.Zero)
                DestroyIcon(handles[0]);
        }
    }

    /// <summary>Encode une image en PNG, format attendu pour les visuels d'un profil.</summary>
    public static byte[]? EncodePng(BitmapSource? source)
    {
        if (source is null)
            return null;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
