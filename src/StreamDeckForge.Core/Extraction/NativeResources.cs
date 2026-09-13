using System.Runtime.InteropServices;
using System.Text;

namespace StreamDeckForge.Core.Extraction;

/// <summary>
/// Acces en lecture seule aux ressources Win32 d'un module PE. Le module est charge avec
/// LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE : aucun code de la cible
/// n'est execute et le chargement fonctionne quelle que soit son architecture.
/// </summary>
public sealed class NativeResourceModule : IDisposable
{
    public const int RtAccelerator = 9;
    public const int RtString = 6;

    private const uint LoadLibraryAsDatafile = 0x00000002;
    private const uint LoadLibraryAsImageResource = 0x00000020;

    private IntPtr _module;

    private NativeResourceModule(IntPtr module) => _module = module;

    public static NativeResourceModule? TryOpen(string path)
    {
        var module = LoadLibraryEx(
            path,
            IntPtr.Zero,
            LoadLibraryAsDatafile | LoadLibraryAsImageResource);

        return module == IntPtr.Zero ? null : new NativeResourceModule(module);
    }

    /// <summary>Identifiants (ou noms) des ressources d'un type donne.</summary>
    public List<IntPtr> EnumerateResourceNames(int resourceType)
    {
        var names = new List<IntPtr>();

        // Le delegue doit rester enracine pendant tout l'appel non gere, d'ou la variable
        // locale explicite et le KeepAlive qui suit.
        EnumResNameProc callback = (_, _, name, _) =>
        {
            names.Add(name);
            return true;
        };

        EnumResourceNames(_module, resourceType, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return names;
    }

    /// <summary>Contenu brut d'une ressource, ou null si elle est absente.</summary>
    public byte[]? Read(int resourceType, IntPtr resourceName)
    {
        var handle = FindResource(_module, resourceName, resourceType);
        if (handle == IntPtr.Zero)
            return null;

        var size = SizeofResource(_module, handle);
        if (size == 0)
            return null;

        var data = LoadResource(_module, handle);
        if (data == IntPtr.Zero)
            return null;

        var pointer = LockResource(data);
        if (pointer == IntPtr.Zero)
            return null;

        var buffer = new byte[size];
        Marshal.Copy(pointer, buffer, 0, (int)size);
        return buffer;
    }

    /// <summary>
    /// Equivalent gere de LoadString : les chaines sont regroupees par blocs de seize,
    /// le bloc portant l'identifiant (id / 16) + 1.
    /// </summary>
    public string? ReadString(int stringId)
    {
        var blockId = (stringId >> 4) + 1;
        var block = Read(RtString, new IntPtr(blockId));
        if (block is null)
            return null;

        var indexInBlock = stringId & 0x0F;
        var offset = 0;

        for (var i = 0; i < 16; i++)
        {
            if (offset + 2 > block.Length)
                return null;

            var length = BitConverter.ToUInt16(block, offset);
            offset += 2;

            if (i == indexInBlock)
            {
                if (length == 0 || offset + length * 2 > block.Length)
                    return null;

                return Encoding.Unicode.GetString(block, offset, length * 2);
            }

            offset += length * 2;
        }

        return null;
    }

    public void Dispose()
    {
        if (_module == IntPtr.Zero)
            return;

        FreeLibrary(_module);
        _module = IntPtr.Zero;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool EnumResNameProc(IntPtr module, IntPtr type, IntPtr name, IntPtr param);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr reserved, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumResourceNames(
        IntPtr module,
        int type,
        EnumResNameProc callback,
        IntPtr param);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, int type);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resourceHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resourceHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr resourceData);
}
