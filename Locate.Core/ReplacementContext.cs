namespace Locate.Core;

public sealed record ReplacementContext(
    SearchOptions Search,
    string Replacement,
    bool PreserveCase = false,
    bool KeepFileDate = false,
    bool CreateBackup = false);

public sealed record ReplaceResult(string Path, int ReplacementCount, string? BackupPath = null);
