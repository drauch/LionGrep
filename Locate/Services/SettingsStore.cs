using System.Text.Json;
using Locate.Models;

namespace Locate.Services;

public sealed class AppSettings
{
    public string EditorCommand { get; set; } = "";
    public bool DontWarnWhenReplacing { get; set; }
    public Preset? LastForm { get; set; }
}

public sealed class SettingsStore
{
    private const string SubPath = "Settings";
    private const string EditorCommandValue = "EditorCommand";
    private const string DontWarnValue = "DontWarnWhenReplacing";
    private const string LastFormValue = "LastForm";

    public AppSettings Load()
    {
        var settings = new AppSettings
        {
            EditorCommand = RegistryStore.ReadString(SubPath, EditorCommandValue) ?? "",
            DontWarnWhenReplacing = RegistryStore.ReadString(SubPath, DontWarnValue) == "1",
        };
        var lastForm = RegistryStore.ReadString(SubPath, LastFormValue);
        if (!string.IsNullOrEmpty(lastForm))
        {
            try { settings.LastForm = JsonSerializer.Deserialize<Preset>(lastForm); }
            catch { /* ignore corrupt blob */ }
        }
        return settings;
    }

    public void Save(AppSettings settings)
    {
        RegistryStore.WriteString(SubPath, EditorCommandValue, settings.EditorCommand);
        RegistryStore.WriteString(SubPath, DontWarnValue, settings.DontWarnWhenReplacing ? "1" : "0");
        if (settings.LastForm is not null)
            RegistryStore.WriteString(SubPath, LastFormValue, JsonSerializer.Serialize(settings.LastForm));
    }
}
