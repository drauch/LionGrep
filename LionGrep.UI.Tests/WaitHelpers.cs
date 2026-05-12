namespace LionGrep.UI.Tests;

/// <summary>
/// Test-side polling primitives. Replaces the <c>Thread.Sleep(N); find()</c> pattern with a
/// proper "poll until the condition is true, fail fast with a descriptive timeout otherwise"
/// helper. Two flavors:
/// <list type="bullet">
///   <item><description><see cref="WaitFor{T}"/> for "find me this element" — returns the result.</description></item>
///   <item><description><see cref="WaitUntil"/> for "wait until this predicate is true" — returns void.</description></item>
/// </list>
/// Both swallow transient exceptions during polling (UIA can throw mid-tree-update), so the
/// caller doesn't have to wrap. The exception of the final poll attempt is preserved as
/// <c>InnerException</c> on the timeout so diagnostics aren't lost.
/// </summary>
internal static class WaitHelpers
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(80);

    /// <summary>Polls <paramref name="finder"/> until it returns a non-null result, or the timeout
    /// expires. Returns the result on success; throws <see cref="TimeoutException"/> on expiry.</summary>
    public static T WaitFor<T>(Func<T?> finder, TimeSpan? timeout = null, string? description = null) where T : class
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        Exception? lastEx = null;
        while (true)
        {
            try
            {
                var result = finder();
                if (result is not null) return result;
            }
#pragma warning disable RCS1075
            catch (Exception ex) { lastEx = ex; /* transient — keep polling */ }
#pragma warning restore RCS1075

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"WaitFor timed out after {(timeout ?? DefaultTimeout).TotalSeconds:0.0}s: {description ?? typeof(T).Name}",
                    lastEx);
            Thread.Sleep((int)PollInterval.TotalMilliseconds);
        }
    }

    /// <summary>Like <see cref="WaitFor{T}"/> but returns null on timeout instead of throwing.
    /// Use when the absence of the element is a legitimate outcome the caller wants to handle.</summary>
    public static T? WaitForOrNull<T>(Func<T?> finder, TimeSpan? timeout = null) where T : class
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = finder();
                if (result is not null) return result;
            }
#pragma warning disable RCS1075
            catch (Exception) { /* transient — keep polling */ }
#pragma warning restore RCS1075
            Thread.Sleep((int)PollInterval.TotalMilliseconds);
        }
        return null;
    }

    // ---- File-system condition shortcuts ----

    /// <summary>Polls until the file exists and its full text equals <paramref name="expected"/>.
    /// Use to wait for a Replace operation to finish writing a target file.</summary>
    public static void WaitForFileContent(string path, string expected, TimeSpan? timeout = null) =>
        WaitUntil(
            () => File.Exists(path) && string.Equals(File.ReadAllText(path), expected, StringComparison.Ordinal),
            timeout,
            $"file '{Path.GetFileName(path)}' to contain expected content");

    /// <summary>Polls until the file exists. Use to wait for a backup (.lgbak) to be written.</summary>
    public static void WaitForFileExists(string path, TimeSpan? timeout = null) =>
        WaitUntil(
            () => File.Exists(path),
            timeout,
            $"file '{Path.GetFileName(path)}' to exist");

    /// <summary>Polls until the file no longer exists. Use to wait for Undo to delete a backup.</summary>
    public static void WaitForFileMissing(string path, TimeSpan? timeout = null) =>
        WaitUntil(
            () => !File.Exists(path),
            timeout,
            $"file '{Path.GetFileName(path)}' to be missing");

    /// <summary>Polls <paramref name="predicate"/> until it returns true or the timeout expires.</summary>
    public static void WaitUntil(Func<bool> predicate, TimeSpan? timeout = null, string? description = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        Exception? lastEx = null;
        while (true)
        {
            try { if (predicate()) return; }
#pragma warning disable RCS1075
            catch (Exception ex) { lastEx = ex; /* transient — keep polling */ }
#pragma warning restore RCS1075

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"WaitUntil timed out after {(timeout ?? DefaultTimeout).TotalSeconds:0.0}s: {description ?? "predicate"}",
                    lastEx);
            Thread.Sleep((int)PollInterval.TotalMilliseconds);
        }
    }
}
