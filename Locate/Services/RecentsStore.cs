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
        try
        {
            var raw = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            // Cleanse legacy entries: trim trailing whitespace/newlines and dedupe (preserving first-seen order).
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var clean = new List<string>(raw.Count);
            foreach (var s in raw)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                var trimmed = s.TrimEnd();
                if (trimmed.Length == 0) continue;
                if (seen.Add(trimmed)) clean.Add(trimmed);
            }
            return clean;
        }
        catch { return []; }
    }

    public void Add(string fieldName, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        value = value.TrimEnd();
        if (value.Length == 0) return;

        var list = Get(fieldName).ToList();
        list.RemoveAll(s => string.Equals(s, value, StringComparison.Ordinal));
        list.Insert(0, value);
        if (list.Count > Capacity) list.RemoveRange(Capacity, list.Count - Capacity);
        RegistryStore.WriteString(SubPath, fieldName, JsonSerializer.Serialize(list));
    }
}
