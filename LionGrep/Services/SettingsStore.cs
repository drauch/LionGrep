using System.Text.Json;
using LionGrep.Models;

namespace LionGrep.Services;

public sealed class AppSettings
{
    public string EditorCommand { get; set; } = "";
    public bool DontWarnWhenReplacing { get; set; }
    public bool RememberRecentValues { get; set; } = true;
    public Preset? LastForm { get; set; }
}

public sealed class SettingsStore
{
    private const string SubPath = "Settings";
    private const string EditorCommandValue = "EditorCommand";
    private const string DontWarnValue = "DontWarnWhenReplacing";
    private const string RememberRecentsValue = "RememberRecentValues";
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
        if (settings.LastForm is not null)
            RegistryStore.WriteString(SubPath, LastFormValue, JsonSerializer.Serialize(settings.LastForm));
    }
}
