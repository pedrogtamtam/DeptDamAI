using System.Threading.Channels;

namespace DeptDam.Services;

public interface IBackgroundTaskQueue
{
    void Enqueue(Func<IServiceProvider, CancellationToken, Task> workItem);
    ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct);
}

public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _queue;

    public BackgroundTaskQueue(int capacity = 100)
    {
        _queue = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(
            new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait });
    }

    public void Enqueue(Func<IServiceProvider, CancellationToken, Task> workItem) =>
        _queue.Writer.TryWrite(workItem);

    public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct) =>
        _queue.Reader.ReadAsync(ct);
}

/// <summary>Tracks which asset IDs currently have an AI re-analysis job in flight.</summary>
public interface IReAnalysisTracker
{
    void Start(string assetId);
    void Complete(string assetId);
    bool IsRunning(string assetId);
}

public sealed class ReAnalysisTracker : IReAnalysisTracker
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _running = new();

    public void Start(string assetId) => _running.TryAdd(assetId, 0);
    public void Complete(string assetId) => _running.TryRemove(assetId, out _);
    public bool IsRunning(string assetId) => _running.ContainsKey(assetId);
}
