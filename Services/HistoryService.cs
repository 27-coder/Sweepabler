using System.Text.Json;
using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

public sealed class HistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task AppendAsync(UpdateRecord record, CancellationToken cancellationToken)
    {
        var records = (await LoadExistingAsync(cancellationToken)).ToList();
        records.Insert(0, record);

        await AtomicJsonFile.WriteAsync(AppPaths.HistoryPath, records, JsonOptions, cancellationToken);
    }

    private static async Task<IReadOnlyList<UpdateRecord>> LoadExistingAsync(CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(AppPaths.HistoryPath))
        {
            return Array.Empty<UpdateRecord>();
        }

        await using var stream = File.OpenRead(AppPaths.HistoryPath);
        var records = await JsonSerializer.DeserializeAsync<List<UpdateRecord>>(stream, JsonOptions, cancellationToken);
        return records is null
            ? Array.Empty<UpdateRecord>()
            : records.OrderByDescending(record => record.TimestampUtc).ToList();
    }
}
