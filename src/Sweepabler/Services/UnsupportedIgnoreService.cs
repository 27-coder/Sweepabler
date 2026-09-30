using System.Text.Json;

namespace ProperAppUpdater.Services;

public sealed class UnsupportedIgnoreService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(AppPaths.UnsupportedAppsIgnorePath))
        {
            return Array.Empty<string>();
        }

        try
        {
            await using var stream = File.OpenRead(AppPaths.UnsupportedAppsIgnorePath);
            return await JsonSerializer.DeserializeAsync<List<string>>(stream, JsonOptions, cancellationToken)
                ?? new List<string>();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Array.Empty<string>();
        }
    }

    public async Task<int> AddAsync(IEnumerable<string> displayNames, CancellationToken cancellationToken)
    {
        var existing = (await LoadAsync(cancellationToken))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToDictionary(AppMatcher.Normalize, name => name, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var name in displayNames.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            var key = AppMatcher.Normalize(name);
            if (string.IsNullOrWhiteSpace(key) || existing.ContainsKey(key))
            {
                continue;
            }

            existing[key] = name.Trim();
            added++;
        }

        await SaveAsync(existing.Values, cancellationToken);

        return added;
    }

    public Task<int> ReplaceAsync(IEnumerable<string> displayNames, CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();
        return ReplaceAsync(AppPaths.UnsupportedAppsIgnorePath, displayNames, cancellationToken);
    }

    internal static async Task<int> ReplaceAsync(
        string path,
        IEnumerable<string> displayNames,
        CancellationToken cancellationToken)
    {
        var selected = displayNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(AppMatcher.Normalize, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => group.First().Trim())
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await AtomicJsonFile.WriteAsync(path, selected, JsonOptions, cancellationToken);
        return selected.Count;
    }

    private static async Task SaveAsync(IEnumerable<string> displayNames, CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();
        await AtomicJsonFile.WriteAsync(
            AppPaths.UnsupportedAppsIgnorePath,
            displayNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList(),
            JsonOptions,
            cancellationToken);
    }
}
