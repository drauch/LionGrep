using Microsoft.Win32;

namespace LionGrep.Services;

internal static class RegistryStore
{
    /// <summary>
    /// HKCU subpath that holds all of LionGrep's persisted state (settings, presets, recents,
    /// last-form snapshot). Defaults to <c>Software\LionGrep</c> for end-user runs; can be
    /// overridden via the <c>--alternate-registry-key</c> command-line flag (parsed in
    /// <see cref="App"/>) so end-to-end UI tests can sandbox themselves without touching
    /// the developer's real settings.
    /// </summary>
    public static string RootPath { get; set; } = @"Software\LionGrep";

    public static string? ReadString(string subPath, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{RootPath}\{subPath}");
        return key?.GetValue(name) as string;
    }

    public static void WriteString(string subPath, string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{RootPath}\{subPath}");
        key.SetValue(name, value);
    }

    public static void DeleteSubTree(string subPath)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree($@"{RootPath}\{subPath}", throwOnMissingSubKey: false); }
        catch { /* best effort */ }
    }

    public static void DeleteAll()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(RootPath, throwOnMissingSubKey: false); }
        catch { /* best effort */ }
    }
}
