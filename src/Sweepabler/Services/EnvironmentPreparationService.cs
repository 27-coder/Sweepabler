namespace ProperAppUpdater.Services;

public sealed class EnvironmentPreparationService
{
    public async Task<EnvironmentPreparationResult> PrepareAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report(LocalizationService.Current.Get("SetupPreparingFolders"));
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppPaths.EnsureCreated();
            var probe = Path.Combine(AppPaths.DataRoot, $".write-check-{Guid.NewGuid():N}");
            SafePath.RequireInside(AppPaths.DataRoot, probe);
            try
            {
                using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.WriteByte(1);
            }
            finally { SafePath.RequireInside(AppPaths.DataRoot, probe); File.Delete(probe); }
        }, cancellationToken);

        await new RequiredToolSetupService(new WindowsSetupToolHost()).EnsureAsync(progress, cancellationToken);
        var maintenance = await new UpdaterMaintenanceService().SweepAsync(progress, cancellationToken);
        var failed = maintenance.Checks.Where(check => !check.IsSuccess).ToList();
        if (failed.Count > 0) throw new InvalidOperationException(maintenance.Summary);
        return new EnvironmentPreparationResult(RequiredToolSetupService.RequiredTools, maintenance);
    }
}

public sealed record EnvironmentPreparationResult(string[] AvailableTools, UpdaterMaintenanceResult Maintenance);
