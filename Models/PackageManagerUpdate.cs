namespace ProperAppUpdater.Models;

public sealed class PackageManagerUpdate
{
    public string Provider { get; init; } = string.Empty;
    public string PackageId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string InstalledVersion { get; init; } = string.Empty;
    public string AvailableVersion { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string TrustSummary { get; init; } = string.Empty;
    public bool RequiresElevation { get; init; }
}
