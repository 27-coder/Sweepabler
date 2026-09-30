using ProperAppUpdater.Models;
using ProperAppUpdater.ViewModels;

namespace ProperAppUpdater.Services;

public sealed class UpdateDownloadQueue : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly SemaphoreSlim _slots = new(3);
    private readonly Dictionary<UpdateCandidateViewModel, Task<UpdateRecord>> _downloads = new();
    private readonly DownloadBandwidthCoordinator _bandwidth;
    private readonly UpdateExecutor _executor;
    private readonly Action<UpdateCandidateViewModel, string> _report;

    public UpdateDownloadQueue(IEnumerable<UpdateCandidateViewModel> candidates, UpdateExecutor executor,
        Action<UpdateCandidateViewModel, string> report, CancellationToken cancellationToken)
    {
        _executor = executor;
        _report = report;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var direct = OrderCandidates(candidates).Where(candidate => !IsManaged(candidate)).ToList();
        _bandwidth = new DownloadBandwidthCoordinator(direct.Select((candidate, index) => (index.ToString(), candidate.DownloadSizeBytes)));
        for (var index = 0; index < direct.Count; index++)
        {
            var candidate = direct[index];
            var id = index.ToString();
            _downloads.Add(candidate, DownloadAsync(candidate, id));
        }
    }

    internal static IEnumerable<UpdateCandidateViewModel> OrderCandidates(IEnumerable<UpdateCandidateViewModel> candidates) =>
        candidates.OrderBy(candidate => candidate.DownloadSizeBytes ?? long.MaxValue);

    public static bool IsManaged(UpdateCandidateViewModel candidate) =>
        candidate.Catalog.Provider.ToLowerInvariant() is "winget" or "choco" or "chocolatey" or "scoop";

    public Task<UpdateRecord>? GetPrepared(UpdateCandidateViewModel candidate) => _downloads.GetValueOrDefault(candidate);

    public async Task<UpdateCandidateViewModel> NextAsync(IReadOnlyList<UpdateCandidateViewModel> remaining)
    {
        // Prefer the smallest ready installer; keep package-manager work going during downloads.
        var ready = remaining.FirstOrDefault(candidate => _downloads.TryGetValue(candidate, out var task) && task.IsCompleted);
        if (ready is not null) return ready;
        var managed = remaining.FirstOrDefault(IsManaged);
        if (managed is not null) return managed;
        await Task.WhenAny(remaining.Select(candidate => _downloads[candidate]));
        _cts.Token.ThrowIfCancellationRequested();
        return remaining.First(candidate => _downloads[candidate].IsCompleted);
    }

    private async Task<UpdateRecord> DownloadAsync(UpdateCandidateViewModel candidate, string id)
    {
        var acquired = false;
        try
        {
            _report(candidate, LocalizationService.Current.Get("DownloadQueued"));
            await _slots.WaitAsync(_cts.Token);
            acquired = true;
            return await _executor.PrepareAsync(candidate.Catalog, new UpdateRecord
            {
                AppName = candidate.Name,
                FromVersion = candidate.InstalledVersion,
                ToVersion = candidate.LatestVersion,
                DownloadUrl = candidate.DownloadUrl,
                SourceName = candidate.SourceName,
                TrustSummary = candidate.TrustSummary
            }, candidate.Sha512, candidate.Sha256,
                new Progress<string>(message => _report(candidate, message)), _cts.Token, _bandwidth, id);
        }
        finally
        {
            _bandwidth.Complete(id);
            if (acquired) _slots.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try { await Task.WhenAll(_downloads.Values); }
        catch (OperationCanceledException) { }
        finally { _slots.Dispose(); _cts.Dispose(); }
    }
}
