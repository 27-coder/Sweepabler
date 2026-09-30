namespace ProperAppUpdater.Models;

public sealed class CatalogEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = "github";
    public List<string> MatchNames { get; set; } = new();
    public string? Owner { get; set; }
    public string? Repo { get; set; }
    public string? PackageId { get; set; }
    public string? VersionUrl { get; set; }
    public string? DownloadBaseUrl { get; set; }
    public string? DownloadUrl { get; set; }
    public string? AssetRegex { get; set; }
    public string? VersionRegex { get; set; }
    public string? DirectVersionRegex { get; set; }
    public string? DownloadRegex { get; set; }
    public string? ChecksumUrl { get; set; }
    public string? ChecksumRegex { get; set; }
    public string? Sha512Regex { get; set; }
    public string InstallerType { get; set; } = "exe";
    public string SilentArgs { get; set; } = string.Empty;
    public bool RequiresElevation { get; set; } = true;
    public bool RequireHttps { get; set; } = true;
    public string? Homepage { get; set; }
    public string? Notes { get; set; }
    public string? SignaturePublisherContains { get; set; }
    public List<string> OfficialDomains { get; set; } = new();
    public bool IncludePrereleases { get; set; }
}
