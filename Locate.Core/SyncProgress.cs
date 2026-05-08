namespace Locate.Core;

/// <summary>An IProgress&lt;T&gt; that invokes the handler synchronously on the calling thread (no SynchronizationContext capture). Useful for in-loop accumulators that must avoid the cost of dispatcher posts.</summary>
public sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
