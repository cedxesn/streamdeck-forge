using System.Text;

namespace StreamDeckForge.Core.Discovery;

/// <summary>Contenu utile d'un raccourci .lnk.</summary>
public sealed record ShellLinkInfo(
    string? TargetPath,
    string? Arguments,
    string? WorkingDirectory,
    string? IconLocation,
    string? Description);

/// <summary>
/// Lecteur binaire du format Shell Link (MS-SHLLINK). On evite volontairement
/// l'interop COM IShellLink : elle impose un appartement STA et coute environ
/// 1 ms par fichier, ce qui ferait exploser le budget de trois secondes du scan.
/// </summary>
public static class ShellLinkReader
{
    private const uint HasLinkTargetIdList = 1 << 0;
    private const uint HasLinkInfo = 1 << 1;
    private const uint HasName = 1 << 2;
    private const uint HasRelativePath = 1 << 3;
    private const uint HasWorkingDir = 1 << 4;
    private const uint HasArguments = 1 << 5;
    private const uint HasIconLocation = 1 << 6;
    private const uint IsUnicode = 1 << 7;

    public static ShellLinkInfo? TryRead(string lnkPath)
    {
        try
        {
            var bytes = File.ReadAllBytes(lnkPath);
            return Parse(bytes, lnkPath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static ShellLinkInfo? Parse(byte[] b, string lnkPath)
    {
        if (b.Length < 0x4C || BitConverter.ToUInt32(b, 0) != 0x4C)
            return null;

        var flags = BitConverter.ToUInt32(b, 0x14);
        var unicode = (flags & IsUnicode) != 0;
        var offset = 0x4C;

        if ((flags & HasLinkTargetIdList) != 0)
        {
            if (offset + 2 > b.Length)
                return null;

            var idListSize = BitConverter.ToUInt16(b, offset);
            offset += 2 + idListSize;
        }

        string? localBasePath = null;

        if ((flags & HasLinkInfo) != 0)
        {
            if (offset + 4 > b.Length)
                return null;

            var linkInfoStart = offset;
            var linkInfoSize = (int)BitConverter.ToUInt32(b, linkInfoStart);
            if (linkInfoSize <= 0 || linkInfoStart + linkInfoSize > b.Length)
                return null;

            localBasePath = ReadLinkInfoPath(b, linkInfoStart);
            offset = linkInfoStart + linkInfoSize;
        }

        var name = ReadStringData(b, ref offset, (flags & HasName) != 0, unicode);
        var relativePath = ReadStringData(b, ref offset, (flags & HasRelativePath) != 0, unicode);
        var workingDir = ReadStringData(b, ref offset, (flags & HasWorkingDir) != 0, unicode);
        var arguments = ReadStringData(b, ref offset, (flags & HasArguments) != 0, unicode);
        var iconLocation = ReadStringData(b, ref offset, (flags & HasIconLocation) != 0, unicode);

        var target = localBasePath;
        if (string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(relativePath))
            target = ResolveRelative(lnkPath, relativePath);

        target = ExpandIfNeeded(target);

        return new ShellLinkInfo(
            target,
            arguments,
            ExpandIfNeeded(workingDir),
            ExpandIfNeeded(iconLocation),
            name);
    }

    /// <summary>Recompose LocalBasePath + CommonPathSuffix de la structure LinkInfo.</summary>
    private static string? ReadLinkInfoPath(byte[] b, int start)
    {
        var headerSize = (int)BitConverter.ToUInt32(b, start + 4);
        var linkInfoFlags = BitConverter.ToUInt32(b, start + 8);
        const uint volumeIdAndLocalBasePath = 1 << 0;

        if ((linkInfoFlags & volumeIdAndLocalBasePath) == 0)
            return null;

        // L'en-tete etendu (>= 0x24) expose des variantes Unicode des deux chemins.
        if (headerSize >= 0x24)
        {
            var basePathUnicodeOffset = (int)BitConverter.ToUInt32(b, start + 28);
            var suffixUnicodeOffset = (int)BitConverter.ToUInt32(b, start + 32);
            var basePath = ReadNullTerminated(b, start + basePathUnicodeOffset, unicode: true);
            var suffix = ReadNullTerminated(b, start + suffixUnicodeOffset, unicode: true);
            if (!string.IsNullOrEmpty(basePath))
                return basePath + suffix;
        }

        var localBasePathOffset = (int)BitConverter.ToUInt32(b, start + 16);
        var commonPathSuffixOffset = (int)BitConverter.ToUInt32(b, start + 24);
        var ansiBase = ReadNullTerminated(b, start + localBasePathOffset, unicode: false);
        var ansiSuffix = ReadNullTerminated(b, start + commonPathSuffixOffset, unicode: false);

        return string.IsNullOrEmpty(ansiBase) ? null : ansiBase + ansiSuffix;
    }

    private static string? ReadStringData(byte[] b, ref int offset, bool present, bool unicode)
    {
        if (!present || offset + 2 > b.Length)
            return null;

        var count = BitConverter.ToUInt16(b, offset);
        offset += 2;

        var byteCount = unicode ? count * 2 : count;
        if (offset + byteCount > b.Length)
        {
            offset = b.Length;
            return null;
        }

        var value = unicode
            ? Encoding.Unicode.GetString(b, offset, byteCount)
            : Encoding.Latin1.GetString(b, offset, byteCount);

        offset += byteCount;
        return value;
    }

    private static string ReadNullTerminated(byte[] b, int offset, bool unicode)
    {
        if (offset < 0 || offset >= b.Length)
            return string.Empty;

        if (unicode)
        {
            var end = offset;
            while (end + 1 < b.Length && (b[end] != 0 || b[end + 1] != 0))
                end += 2;

            return Encoding.Unicode.GetString(b, offset, end - offset);
        }

        var stop = offset;
        while (stop < b.Length && b[stop] != 0)
            stop++;

        return Encoding.Latin1.GetString(b, offset, stop - offset);
    }

    private static string? ResolveRelative(string lnkPath, string relativePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(lnkPath));
            if (directory is null)
                return null;

            return Path.GetFullPath(Path.Combine(directory, relativePath));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string? ExpandIfNeeded(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? value
            : Environment.ExpandEnvironmentVariables(value.Trim());
}
