using System.Text.Json;

namespace Locate.Services;

public sealed class RecentsStore
{
    private const int Capacity = 10;
    private const string SubPath = "Recents";

    public IReadOnlyList<string> Get(string fieldName)
    {
        var json = RegistryStore.ReadString(SubPath, fieldName);
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }

    public void Add(string fieldName, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var list = Get(fieldName).ToList();
        list.RemoveAll(s => string.Equals(s, value, StringComparison.Ordinal));
        list.Insert(0, value);
        if (list.Count > Capacity) list.RemoveRange(Capacity, list.Count - Capacity);
        RegistryStore.WriteString(SubPath, fieldName, JsonSerializer.Serialize(list));
    }
}
