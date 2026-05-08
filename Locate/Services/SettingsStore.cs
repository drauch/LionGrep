namespace Locate.Services;

public sealed class AppSettings
{
    public string EditorCommand { get; set; } = "";
    public bool DontWarnWhenReplacing { get; set; }
}

public sealed class SettingsStore
{
    private const string SubPath = "Settings";
    private const string EditorCommandValue = "EditorCommand";
    private const string DontWarnValue = "DontWarnWhenReplacing";

    public AppSettings Load() => new()
    {
        EditorCommand = RegistryStore.ReadString(SubPath, EditorCommandValue) ?? "",
        DontWarnWhenReplacing = RegistryStore.ReadString(SubPath, DontWarnValue) == "1",
    };

    public void Save(AppSettings settings)
    {
        RegistryStore.WriteString(SubPath, EditorCommandValue, settings.EditorCommand);
        RegistryStore.WriteString(SubPath, DontWarnValue, settings.DontWarnWhenReplacing ? "1" : "0");
    }
}
