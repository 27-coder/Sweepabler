using System.Reflection;
using System.Text.Json;
using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

public sealed class CatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<IReadOnlyList<CatalogEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();

        var entries = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in await ReadBundledCatalogAsync(cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(entry.Id))
            {
                entries[entry.Id] = entry;
            }
        }

        foreach (var entry in await ReadCatalogFileAsync(AppPaths.UserCatalogPath, cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(entry.Id))
            {
                entries[entry.Id] = entry;
            }
        }

        return entries.Values
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<IReadOnlyList<CatalogEntry>> ReadBundledCatalogAsync(CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();
        await using var stream = assembly.GetManifestResourceStream("catalog.json");
        if (stream is not null)
        {
            var entries = await JsonSerializer.DeserializeAsync<List<CatalogEntry>>(stream, JsonOptions, cancellationToken);
            return entries ?? new List<CatalogEntry>();
        }

        return await ReadCatalogFileAsync(ResolveBundledCatalogPath(), cancellationToken);
    }

    private static string ResolveBundledCatalogPath()
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "catalog.json");
        return File.Exists(outputPath)
            ? outputPath
            : Path.Combine(Environment.CurrentDirectory, "catalog.json");
    }

    private static async Task<IReadOnlyList<CatalogEntry>> ReadCatalogFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<CatalogEntry>();
        }

        await using var stream = File.OpenRead(path);
        var entries = await JsonSerializer.DeserializeAsync<List<CatalogEntry>>(stream, JsonOptions, cancellationToken);
        return entries ?? new List<CatalogEntry>();
    }
}
