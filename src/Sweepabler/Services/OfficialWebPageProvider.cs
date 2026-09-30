using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

public sealed class OfficialWebPageProvider : IUpdateProvider
{
    private readonly HttpClient _httpClient;

    public OfficialWebPageProvider(HttpClient httpClient)
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

        var page = await response.Content.ReadAsStringAsync(cancellationToken);
        var version = ExtractRequiredValue(
            page,
            catalog.DirectVersionRegex ?? catalog.VersionRegex,
            "version",
            $"{catalog.Name} is missing directVersionRegex/versionRegex.");

        var downloadUrl = ResolveDownloadUrl(catalog, page, version);
        SourceTrustPolicy.ValidateDownloadUrl(catalog, downloadUrl);

        var sha512 = await ResolveSha512Async(catalog, page, version, cancellationToken);

        return new ReleaseInfo
        {
            Version = VersionComparer.CleanVersion(version, catalog.VersionRegex),
            DownloadUrl = downloadUrl,
            AssetName = Path.GetFileName(new Uri(downloadUrl).AbsolutePath),
            Sha512 = sha512,
            SourceName = "Official web",
            TrustSummary = SourceTrustPolicy.Describe(catalog, downloadUrl)
        };
    }

    private static string ResolveDownloadUrl(CatalogEntry catalog, string page, string version)
    {
        string candidate;
        if (!string.IsNullOrWhiteSpace(catalog.DownloadUrl))
        {
            candidate = catalog.DownloadUrl.Replace("{version}", version, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            candidate = ExtractRequiredValue(
                page,
                catalog.DownloadRegex,
                "url",
                $"{catalog.Name} is missing downloadUrl/downloadRegex.");
        }

        candidate = WebUtility.HtmlDecode(candidate.Trim().Trim('"', '\''));
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        var baseUrl = catalog.DownloadBaseUrl ?? catalog.VersionUrl ?? string.Empty;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException($"{catalog.Name} cannot resolve a relative download URL.");
        }

        return new Uri(new Uri(baseUrl), candidate).ToString();
    }

    private async Task<string> ResolveSha512Async(
        CatalogEntry catalog,
        string page,
        string version,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(catalog.ChecksumUrl))
        {
            var checksumUrl = catalog.ChecksumUrl.Replace("{version}", version, StringComparison.OrdinalIgnoreCase);
            using var request = new HttpRequestMessage(HttpMethod.Get, checksumUrl);
            request.Headers.UserAgent.ParseAdd("Supurucu/1.0");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var checksumPage = await response.Content.ReadAsStringAsync(cancellationToken);

            return ExtractOptionalValue(checksumPage, catalog.ChecksumRegex ?? catalog.Sha512Regex, "sha512");
        }

        return ExtractOptionalValue(page, catalog.Sha512Regex, "sha512");
    }

    private static string ExtractRequiredValue(string input, string? regex, string groupName, string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(regex))
        {
            throw new InvalidOperationException(missingMessage);
        }

        var value = ExtractOptionalValue(input, regex, groupName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configured regex did not match {groupName}.");
        }

        return value;
    }

    private static string ExtractOptionalValue(string input, string? regex, string groupName)
    {
        if (string.IsNullOrWhiteSpace(regex))
        {
            return string.Empty;
        }

        var match = Regex.Match(
            input,
            regex,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

        if (!match.Success)
        {
            return string.Empty;
        }

        if (match.Groups[groupName].Success)
        {
            return match.Groups[groupName].Value.Trim();
        }

        return match.Groups.Count > 1
            ? match.Groups[1].Value.Trim()
            : match.Value.Trim();
    }
}
