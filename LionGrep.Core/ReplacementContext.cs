namespace LionGrep.Core;

public sealed record ReplacementContext(
    SearchOptions Search,
    string Replacement,
    bool PreserveCase = false,
    bool KeepFileDate = false,
    bool CreateBackup = false,
    string BackupExtension = "lgbak");

public sealed record ReplaceResult(string Path, int ReplacementCount, string? BackupPath = null);
