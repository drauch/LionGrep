using System.Text.Json;
using Locate.Models;

namespace Locate.Services;

public sealed class PresetsStore
{
    private const string SubPath = "Presets";
    private const string PresetsListValue = "All";

    public IReadOnlyList<Preset> Load()
    {
        var json = RegistryStore.ReadString(SubPath, PresetsListValue);
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<Preset>>(json) ?? []; }
        catch { return []; }
    }

    public void Save(IReadOnlyList<Preset> presets)
    {
        RegistryStore.WriteString(SubPath, PresetsListValue, JsonSerializer.Serialize(presets));
    }
}
