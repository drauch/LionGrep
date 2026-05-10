using System.Runtime.InteropServices;

namespace LionGrep;

/// <summary>
/// Win32 / shell P/Invoke surface used by <see cref="MainWindow"/>.
/// Kept in one place so the main window code-behind stays focused on UI logic.
/// </summary>
internal static class NativeMethods
{
    // ---- Window messages ----
    public const uint WM_GETMINMAXINFO = 0x0024;
    public const uint WM_KEYDOWN       = 0x0100;

    public const int VK_RETURN  = 0x0D;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU    = 0x12;

    [DllImport("user32.dll")] public static extern short GetKeyState(int nVirtKey);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    // ---- Window subclassing (used to override WM_GETMINMAXINFO) ----
    public delegate nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint subclassId, nuint refData);

    [DllImport("comctl32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool SetWindowSubclass(nint hWnd, SubclassProc proc, nuint subclassId, nuint refData);

    [DllImport("comctl32.dll", CharSet = CharSet.Auto)]
    public static extern nint DefSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam);

    // ---- Shell: file properties dialog ----
    public const uint SHOP_FILEPATH = 0x2;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern bool SHObjectProperties(
        nint hwnd,
        uint shopObjectType,
        [MarshalAs(UnmanagedType.LPWStr)] string pszObjectName,
        [MarshalAs(UnmanagedType.LPWStr)] string? pszPropertyPage);

    // ---- Shell: "Open With" dialog ----
    public const uint OAIF_ALLOW_REGISTRATION = 0x01;
    public const uint OAIF_REGISTER_EXT       = 0x02;
    public const uint OAIF_EXEC               = 0x04;
    public const uint OAIF_HIDE_REGISTRATION  = 0x20;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OPENASINFO
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pcszFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pcszClass;
        public uint oaifInFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHOpenWithDialog(nint hwndParent, ref OPENASINFO oOAI);
}
