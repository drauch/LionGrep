using Microsoft.Win32;

namespace Locate.Services;

internal static class RegistryStore
{
    private const string RootPath = @"Software\Locate";

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
}
