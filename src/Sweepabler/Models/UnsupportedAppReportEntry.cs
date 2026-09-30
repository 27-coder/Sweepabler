namespace ProperAppUpdater.Models;

public sealed class UnsupportedAppReportEntry
{
    public string DisplayName { get; init; } = string.Empty;
    public string DisplayVersion { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string SignedPublisher { get; init; } = string.Empty;
    public string CandidateOfficialUrl { get; init; } = string.Empty;
    public string UrlUpdateInfo { get; init; } = string.Empty;
    public string UrlInfoAbout { get; init; } = string.Empty;
    public string HelpLink { get; init; } = string.Empty;
    public string InstallLocation { get; init; } = string.Empty;
    public string DisplayIcon { get; init; } = string.Empty;
    public string RegistryPath { get; init; } = string.Empty;
    public string NextStep { get; init; } = string.Empty;
}
