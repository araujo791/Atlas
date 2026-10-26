using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Inventory.Core;

public static class WindowsReparse
{
    public static bool RedirectsName(uint tag) => (tag & 0x20000000) != 0;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle file, uint control, IntPtr input,
        uint inputLength, byte[] output, uint outputLength, out uint returned, IntPtr overlapped);

    [SupportedOSPlatform("windows")]
    public static bool IsNameRedirect(string path)
    {
        using var handle = CreateFileW(path, 0, 7, IntPtr.Zero, 3,
            0x00200000 | 0x02000000, IntPtr.Zero); // open reparse metadata, not its target
        if (handle.IsInvalid) return true;
        var data = new byte[16384];
        if (!DeviceIoControl(handle, 0x000900A8, IntPtr.Zero, 0, data, (uint)data.Length,
            out var returned, IntPtr.Zero) || returned < 4) return true;
        return RedirectsName(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data));
    }
}
