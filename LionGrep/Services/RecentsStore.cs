using System.Text.Json;

namespace LionGrep.Services;

public sealed class RecentsStore
{
    private const int Capacity = 10;
    private const string SubPath = "Recents";

    private readonly SettingsStore _settingsStore;

    public RecentsStore() : this(new SettingsStore()) { }
    public RecentsStore(SettingsStore settingsStore) { _settingsStore = settingsStore; }

    public IReadOnlyList<string> Get(string fieldName)
    {
        if (!_settingsStore.Load().RememberRecentValues) return [];
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
        if (!_settingsStore.Load().RememberRecentValues) return;
        if (string.IsNullOrWhiteSpace(value)) return;
        value = value.TrimEnd();
        if (value.Length == 0) return;

        // Re-read raw (skip the persistence gate) so we don't drop entries when toggling settings.
        var raw = ReadRaw(fieldName);
        raw.RemoveAll(s => string.Equals(s, value, StringComparison.Ordinal));
        raw.Insert(0, value);
        if (raw.Count > Capacity) raw.RemoveRange(Capacity, raw.Count - Capacity);
        RegistryStore.WriteString(SubPath, fieldName, JsonSerializer.Serialize(raw));
    }

    public static void ClearAll()
    {
        RegistryStore.DeleteSubTree(SubPath);
    }

    private static List<string> ReadRaw(string fieldName)
    {
        var json = RegistryStore.ReadString(SubPath, fieldName);
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }
}
