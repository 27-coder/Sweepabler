namespace ProperAppUpdater.Models;

public sealed class ReleaseInfo
{
    public string Version { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public long? DownloadSizeBytes { get; init; }
    public string AssetName { get; init; } = string.Empty;
    public string Sha512 { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string TrustSummary { get; init; } = string.Empty;
}
