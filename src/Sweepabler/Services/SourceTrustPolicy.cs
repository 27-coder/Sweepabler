using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

public static class SourceTrustPolicy
{
    private static readonly IReadOnlySet<string> GitHubReleaseAssetHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "release-assets.githubusercontent.com",
        "objects.githubusercontent.com"
    };

    public static void ValidateDownloadUrl(CatalogEntry catalog, string downloadUrl)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"{catalog.Name} produced a non-absolute download URL.");
        }

        if (catalog.RequireHttps && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"{catalog.Name} download URL is not HTTPS.");
        }

        if (catalog.OfficialDomains.Count == 0)
        {
            return;
        }

        if (!catalog.OfficialDomains.Any(domain => HostMatches(uri.Host, domain)))
        {
            throw new InvalidOperationException(
                $"{catalog.Name} download host '{uri.Host}' is not in officialDomains.");
        }
    }

    public static string Describe(CatalogEntry catalog, string downloadUrl)
    {
        var parts = new List<string>();

        if (catalog.RequireHttps)
        {
            parts.Add("HTTPS");
        }

        if (catalog.OfficialDomains.Count > 0 && Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
        {
            var matchedDomain = catalog.OfficialDomains.FirstOrDefault(domain => HostMatches(uri.Host, domain));
            if (!string.IsNullOrWhiteSpace(matchedDomain))
            {
                parts.Add($"domain:{matchedDomain}");
            }
        }

        if (!string.IsNullOrWhiteSpace(catalog.SignaturePublisherContains))
        {
            parts.Add($"signer:{catalog.SignaturePublisherContains}");
        }

        return parts.Count == 0 ? "catalog" : string.Join(", ", parts);
    }

    public static void ValidateRedirectTarget(CatalogEntry catalog, string originalUrl, string effectiveUrl)
    {
        ValidateDownloadUrl(catalog, originalUrl);

        if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var original) ||
            !Uri.TryCreate(effectiveUrl, UriKind.Absolute, out var effective))
        {
            throw new InvalidOperationException($"{catalog.Name} produced an invalid redirect URL.");
        }

        if (original.Scheme == effective.Scheme &&
            original.Host.Equals(effective.Host, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // GitHub release downloads legitimately redirect from github.com to GitHub's
        // dedicated asset CDN. Keep this exception exact; other cross-domain redirects
        // must still satisfy the catalog's explicit officialDomains allow-list.
        if (original.Scheme == Uri.UriSchemeHttps &&
            original.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            effective.Scheme == Uri.UriSchemeHttps &&
            GitHubReleaseAssetHosts.Contains(effective.Host))
        {
            return;
        }

        ValidateDownloadUrl(catalog, effectiveUrl);
    }

    private static bool HostMatches(string host, string domain)
    {
        var cleanHost = host.Trim().TrimEnd('.').ToLowerInvariant();
        var cleanDomain = domain.Trim().TrimEnd('.').ToLowerInvariant();

        return cleanHost == cleanDomain ||
               cleanHost.EndsWith($".{cleanDomain}", StringComparison.OrdinalIgnoreCase);
    }
}
