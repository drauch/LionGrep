using System.Text.Json;
using LionGrep.Models;

namespace LionGrep.Services;

public sealed class PresetsStore
{
    private const string SubPath = "Presets";
    private const string PresetsListValue = "All";

#pragma warning disable S2325 // Instance method by API design — callers consume `_presetsStore.Load()` everywhere.
    public IReadOnlyList<Preset> Load()
    {
        var json = RegistryStore.ReadString(SubPath, PresetsListValue);
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize(json, JsonContext.Default.ListPreset) ?? []; }
        catch (JsonException) { return []; }
    }

    public void Save(IReadOnlyList<Preset> presets)
    {
        RegistryStore.WriteString(SubPath, PresetsListValue, JsonSerializer.Serialize([.. presets], JsonContext.Default.ListPreset));
    }
#pragma warning restore S2325
}
