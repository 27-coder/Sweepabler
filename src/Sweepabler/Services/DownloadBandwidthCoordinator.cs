using System.Diagnostics;

namespace ProperAppUpdater.Services;

// Only our own HTTP streams are throttled; package managers control their own downloads.
public sealed class DownloadBandwidthCoordinator
{
    public const long LargeDownloadBytes = 100L * 1024 * 1024;
    public const int LimitedBytesPerSecond = 1024 * 1024;
    private readonly object _sync = new();
    private readonly Dictionary<string, long?> _pending;

    public DownloadBandwidthCoordinator(IEnumerable<(string Id, long? Size)> downloads)
    {
        _pending = downloads.ToDictionary(item => item.Id, item => item.Size);
    }

    public void SetSize(string id, long? size)
    {
        lock (_sync) { if (_pending.ContainsKey(id) && size is > 0) _pending[id] = size; }
    }

    public void Complete(string id) { lock (_sync) { _pending.Remove(id); } }

    internal bool ShouldLimit(string id)
    {
        lock (_sync)
        {
            return _pending.TryGetValue(id, out var size) && size >= LargeDownloadBytes &&
                _pending.Any(other => other.Key != id && (other.Value is null || other.Value < LargeDownloadBytes));
        }
    }

    public async Task PaceAsync(string id, int bytes, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var minimum = TimeSpan.FromSeconds((double)bytes / LimitedBytesPerSecond);
        while (ShouldLimit(id) && stopwatch.Elapsed < minimum)
        {
            var remaining = minimum - stopwatch.Elapsed;
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(50) ? remaining : TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }
}
