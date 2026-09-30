using System.Text.Json;
using ProperAppUpdater.Models;
using ProperAppUpdater.ViewModels;

namespace ProperAppUpdater.Services;

public sealed class UpdateFailureBlockService
{
    public const int FailureThreshold = 3;

    // A block is not permanent: after this long with no further failure it expires, so a
    // transiently-failing update gets retried automatically instead of being hidden forever.
    private static readonly TimeSpan BlockExpiry = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<IReadOnlySet<string>> LoadBlockedKeysAsync(CancellationToken cancellationToken)
    {
        var states = await LoadAsync(cancellationToken);
        var cutoff = DateTimeOffset.UtcNow - BlockExpiry;
        return states
            .Where(state => state.IsBlocked && state.LastFailureUtc >= cutoff)
            .Select(state => state.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Clears every blocked entry so the user can retry them. Returns how many were cleared.</summary>
    public async Task<int> ClearAllBlocksAsync(CancellationToken cancellationToken)
    {
        var states = (await LoadAsync(cancellationToken)).ToList();
        var cleared = states.RemoveAll(state => state.IsBlocked);
        if (cleared > 0)
        {
            await SaveAsync(states, cancellationToken);
        }

        return cleared;
    }

    public bool IsBlocked(UpdateCandidateViewModel candidate, IReadOnlySet<string> blockedKeys)
    {
        return blockedKeys.Contains(BuildKey(candidate));
    }

    public async Task<FailureUpdateResult> RecordFailureAsync(
        UpdateCandidateViewModel candidate,
        UpdateRecord record,
        CancellationToken cancellationToken)
    {
        var states = (await LoadAsync(cancellationToken)).ToList();
        var key = BuildKey(candidate);
        var state = states.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        if (state is null)
        {
            state = new UpdateFailureState
            {
                Key = key,
                MachineName = Environment.MachineName,
                AppName = candidate.Name,
                AppId = candidate.Catalog.PackageId ?? candidate.Catalog.Id,
                InstalledVersion = candidate.InstalledVersion,
                TargetVersion = candidate.LatestVersion,
                SourceName = candidate.SourceName,
                FirstFailureUtc = DateTimeOffset.UtcNow
            };
            states.Add(state);
        }

        state.FailureCount++;
        state.LastFailureUtc = DateTimeOffset.UtcNow;
        state.LastError = string.IsNullOrWhiteSpace(record.Error) ? record.Outcome : record.Error;
        state.IsBlocked = state.FailureCount >= FailureThreshold;

        await SaveAsync(states, cancellationToken);
        return new FailureUpdateResult(state.FailureCount, state.IsBlocked);
    }

    public async Task ClearFailureAsync(UpdateCandidateViewModel candidate, CancellationToken cancellationToken)
    {
        var states = (await LoadAsync(cancellationToken)).ToList();
        var key = BuildKey(candidate);
        var removed = states.RemoveAll(state => string.Equals(state.Key, key, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            await SaveAsync(states, cancellationToken);
        }
    }

    private static string BuildKey(UpdateCandidateViewModel candidate)
    {
        var appId = candidate.Catalog.PackageId ?? candidate.Catalog.Id;
        var parts = new[]
        {
            Environment.MachineName,
            candidate.Name,
            appId,
            candidate.InstalledVersion,
            candidate.LatestVersion,
            candidate.SourceName
        };

        return string.Join("|", parts.Select(part => AppMatcher.Normalize(part ?? string.Empty)));
    }

    private static async Task<IReadOnlyList<UpdateFailureState>> LoadAsync(CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(AppPaths.BlockedUpdatesPath))
        {
            return Array.Empty<UpdateFailureState>();
        }

        try
        {
            await using var stream = File.OpenRead(AppPaths.BlockedUpdatesPath);
            var states = await JsonSerializer.DeserializeAsync<List<UpdateFailureState>>(stream, JsonOptions, cancellationToken);
            return states is null ? Array.Empty<UpdateFailureState>() : states;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Array.Empty<UpdateFailureState>();
        }
    }

    private static async Task SaveAsync(IReadOnlyList<UpdateFailureState> states, CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();
        await AtomicJsonFile.WriteAsync(
            AppPaths.BlockedUpdatesPath,
            states.OrderBy(state => state.AppName),
            JsonOptions,
            cancellationToken);
    }

}

internal sealed class UpdateFailureState
{
    public string Key { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string InstalledVersion { get; set; } = string.Empty;
    public string TargetVersion { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public int FailureCount { get; set; }
    public bool IsBlocked { get; set; }
    public string LastError { get; set; } = string.Empty;
    public DateTimeOffset FirstFailureUtc { get; set; }
    public DateTimeOffset LastFailureUtc { get; set; }
}

public readonly record struct FailureUpdateResult(int FailureCount, bool IsBlocked);
