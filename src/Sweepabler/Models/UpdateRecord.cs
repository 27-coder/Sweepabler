namespace ProperAppUpdater.Models;

public sealed class UpdateRecord
{
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string AppName { get; set; } = string.Empty;
    public string FromVersion { get; set; } = string.Empty;
    public string ToVersion { get; set; } = string.Empty;
    public string InstallerPath { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string TrustSummary { get; set; } = string.Empty;
    public int? ExitCode { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string Signer { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;

    /// <summary>How the installer's authenticity was established: "sha512", "sha256", "signature", or "" when nothing verified.</summary>
    public string VerifiedBy { get; set; } = string.Empty;

    /// <summary>Human-readable trust warning when the installer could not be verified but was still allowed to run. Empty when verified.</summary>
    public string TrustWarning { get; set; } = string.Empty;
}
