using System.Diagnostics;

namespace Locate.Services;

public sealed class EditorLauncher
{
    public bool TryLaunch(string commandTemplate, string filePath, int line, int column, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(commandTemplate))
        {
            error = "No editor command configured. Set one in Settings.";
            return false;
        }

        var expanded = commandTemplate
            .Replace("%path%", filePath, StringComparison.OrdinalIgnoreCase)
            .Replace("%line%", line.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("%column%", column.ToString(), StringComparison.OrdinalIgnoreCase);

        var (exe, args) = ParseCommand(expanded);
        if (string.IsNullOrEmpty(exe))
        {
            error = "Editor command appears empty after substitution.";
            return false;
        }

        try
        {
#pragma warning disable IDISP004 // Fire-and-forget editor launch — the Process handle isn't useful to us.
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
            });
#pragma warning restore IDISP004
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static (string Exe, string Args) ParseCommand(string command)
    {
        command = command.TrimStart();
        if (command.Length == 0) return ("", "");

        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            if (end < 0) return (command[1..], "");
            return (command[1..end], command[(end + 1)..].TrimStart());
        }

        var space = command.IndexOf(' ');
        if (space < 0) return (command, "");
        return (command[..space], command[(space + 1)..].TrimStart());
    }
}
