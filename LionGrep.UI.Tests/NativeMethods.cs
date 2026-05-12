using System.Runtime.InteropServices;

namespace LionGrep.UI.Tests;

internal static class NativeMethods
{
    /// <summary>Brings a top-level window to the foreground. Subject to Windows' "can't steal
    /// focus" rules — only works directly when the caller already owns the foreground.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    /// <summary>Attaches/detaches one thread's input state to another's. Used to share input
    /// queues with the current foreground thread so <see cref="SetForegroundWindow"/> succeeds
    /// even when the caller isn't the foreground process — the workaround for headless CI
    /// runners where another window owns the foreground.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();
}
