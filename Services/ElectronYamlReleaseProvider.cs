using System.Net.Http;
using System.Text.RegularExpressions;
using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

public sealed partial class ElectronYamlReleaseProvider : IUpdateProvider
{
    private readonly HttpClient _httpClient;

    public ElectronYamlReleaseProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ReleaseInfo> GetLatestAsync(CatalogEntry catalog, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(catalog.VersionUrl))
        {
            throw new InvalidOperationException($"{catalog.Name} is missing versionUrl.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, catalog.VersionUrl);
        request.Headers.UserAgent.ParseAdd("Supurucu/1.0");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var yaml = await response.Content.ReadAsStringAsync(cancellationToken);
        var version = VersionLine().Match(yaml).Groups["version"].Value.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException($"{catalog.Name} update feed did not include a version.");
        }

        var asset = ResolveAsset(yaml, catalog);
        var sha512 = ResolveSha512(yaml, asset);
        var downloadUrl = ResolveDownloadUrl(catalog, asset);
        SourceTrustPolicy.ValidateDownloadUrl(catalog, downloadUrl);

        return new ReleaseInfo
        {
            Version = VersionComparer.CleanVersion(version, catalog.VersionRegex),
            DownloadUrl = downloadUrl,
            AssetName = Path.GetFileName(asset),
            Sha512 = sha512,
            SourceName = "Electron update feed",
            TrustSummary = SourceTrustPolicy.Describe(catalog, downloadUrl)
        };
    }

    private static string ResolveAsset(string yaml, CatalogEntry catalog)
    {
        var candidates = AssetLine()
            .Matches(yaml)
            .Select(match => match.Groups["asset"].Value.Trim().Trim('"', '\''))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException($"{catalog.Name} update feed did not include a downloadable asset.");
        }

        if (!string.IsNullOrWhiteSpace(catalog.AssetRegex))
        {
            var regex = new Regex(catalog.AssetRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var match = candidates.FirstOrDefault(candidate => regex.IsMatch(Path.GetFileName(candidate)));
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }

            throw new InvalidOperationException(
                $"{catalog.Name} update feed has no asset matching {catalog.AssetRegex}.");
        }

        return candidates[0];
    }

    private static string ResolveSha512(string yaml, string selectedAsset)
    {
        string? activeAsset = null;
        foreach (var rawLine in yaml.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var assetMatch = AssetLine().Match(rawLine);
            if (assetMatch.Success)
            {
                activeAsset = assetMatch.Groups["asset"].Value.Trim().Trim('"', '\'');
                continue;
            }

            var shaMatch = Sha512Line().Match(rawLine);
            if (shaMatch.Success &&
                activeAsset is not null &&
                string.Equals(Path.GetFileName(activeAsset), Path.GetFileName(selectedAsset), StringComparison.OrdinalIgnoreCase))
            {
                return shaMatch.Groups["sha512"].Value.Trim().Trim('"', '\'');
            }
        }

        return string.Empty;
    }

    private static string ResolveDownloadUrl(CatalogEntry catalog, string asset)
    {
        if (Uri.TryCreate(asset, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        var baseUrl = catalog.DownloadBaseUrl ?? catalog.VersionUrl ?? string.Empty;
        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            baseUrl += "/";
        }

        return new Uri(new Uri(baseUrl), asset).ToString();
    }

    [GeneratedRegex(@"(?im)^\s*version:\s*(?<version>.+?)\s*$")]
    private static partial Regex VersionLine();

    [GeneratedRegex(@"(?im)^\s*(?:url|path):\s*(?<asset>.+?)\s*$")]
    private static partial Regex AssetLine();

    [GeneratedRegex(@"(?im)^\s*sha512:\s*(?<sha512>.+?)\s*$")]
    private static partial Regex Sha512Line();
}
