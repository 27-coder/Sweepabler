using System.Text.Json;
using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

public sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(AppPaths.SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(AppPaths.SettingsPath);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                ?? new AppSettings();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();
        await AtomicJsonFile.WriteAsync(AppPaths.SettingsPath, settings, JsonOptions, cancellationToken);
    }
}
