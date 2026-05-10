using System.Text.Json;
using LionGrep.Models;

namespace LionGrep.Services;

public sealed class AppSettings
{
    public const string DefaultBackupExtension = "lgbak";

    public string EditorCommand { get; set; } = "";
    public bool DontWarnWhenReplacing { get; set; }
    public bool RememberRecentValues { get; set; } = true;
    public string BackupExtension { get; set; } = DefaultBackupExtension;
    public Preset? LastForm { get; set; }
}

public sealed class SettingsStore
{
    private const string SubPath = "Settings";
    private const string EditorCommandValue = "EditorCommand";
    private const string DontWarnValue = "DontWarnWhenReplacing";
    private const string RememberRecentsValue = "RememberRecentValues";
    private const string BackupExtensionValue = "BackupExtension";
    private const string LastFormValue = "LastForm";

    public AppSettings Load()
    {
        var rememberRaw = RegistryStore.ReadString(SubPath, RememberRecentsValue);
        var settings = new AppSettings
        {
            EditorCommand = RegistryStore.ReadString(SubPath, EditorCommandValue) ?? "",
            DontWarnWhenReplacing = string.Equals(RegistryStore.ReadString(SubPath, DontWarnValue), "1", StringComparison.Ordinal),
            // Default true: enabled unless explicitly disabled.
            RememberRecentValues = !string.Equals(rememberRaw, "0", StringComparison.Ordinal),
            BackupExtension = NormalizeBackupExtension(RegistryStore.ReadString(SubPath, BackupExtensionValue)),
        };
        var lastForm = RegistryStore.ReadString(SubPath, LastFormValue);
        if (!string.IsNullOrEmpty(lastForm))
        {
            try { settings.LastForm = JsonSerializer.Deserialize<Preset>(lastForm); }
            catch (JsonException) { /* ignore corrupt blob */ }
        }
        return settings;
    }

#pragma warning disable S2325 // Instance method by API design — every caller uses `_settingsStore.Save(...)`.
    public void Save(AppSettings settings)
#pragma warning restore S2325
    {
        RegistryStore.WriteString(SubPath, EditorCommandValue, settings.EditorCommand);
        RegistryStore.WriteString(SubPath, DontWarnValue, settings.DontWarnWhenReplacing ? "1" : "0");
        RegistryStore.WriteString(SubPath, RememberRecentsValue, settings.RememberRecentValues ? "1" : "0");
        RegistryStore.WriteString(SubPath, BackupExtensionValue, NormalizeBackupExtension(settings.BackupExtension));
        if (settings.LastForm is not null)
            RegistryStore.WriteString(SubPath, LastFormValue, JsonSerializer.Serialize(settings.LastForm));
    }

    /// <summary>Trim whitespace and surrounding dots; fall back to the default if the result is empty
    /// or contains characters that would break a Windows filename (basic sanity check, not exhaustive).</summary>
    public static string NormalizeBackupExtension(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return AppSettings.DefaultBackupExtension;
        var trimmed = raw.Trim().Trim('.');
        if (trimmed.Length == 0) return AppSettings.DefaultBackupExtension;
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return AppSettings.DefaultBackupExtension;
        return trimmed;
    }
}
