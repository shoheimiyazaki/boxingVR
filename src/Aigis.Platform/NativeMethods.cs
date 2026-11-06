using System.Runtime.InteropServices;

namespace Aigis.Platform;

/// <summary>The few Win32 calls the windowing backend does not wrap — see their call sites.</summary>
internal static partial class NativeMethods
{
    /// <summary>The DPI Windows calls 100% scale.</summary>
    public const float DefaultDpi = 96f;

    /// <summary>The DPI of the monitor <paramref name="hwnd"/> is on (Windows 10 1607+); 0 on an
    /// invalid handle.</summary>
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial uint GetDpiForWindow(nint hwnd);
}
