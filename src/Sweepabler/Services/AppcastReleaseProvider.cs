using System.Net.Http;
using System.Xml.Linq;
using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

public sealed class AppcastReleaseProvider : IUpdateProvider
{
    private readonly HttpClient _httpClient;

    public AppcastReleaseProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ReleaseInfo> GetLatestAsync(CatalogEntry catalog, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(catalog.VersionUrl))
        {
            throw new InvalidOperationException($"{catalog.Name} is missing appcast versionUrl.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, catalog.VersionUrl);
        request.Headers.UserAgent.ParseAdd("Supurucu/1.0");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = XDocument.Parse(xml);
        var item = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "item")
            ?? throw new InvalidOperationException($"{catalog.Name} appcast has no item.");

        var enclosure = item.Descendants().FirstOrDefault(element => element.Name.LocalName == "enclosure");
        var downloadUrl = enclosure?.Attribute("url")?.Value
            ?? item.Descendants().FirstOrDefault(element => element.Name.LocalName == "link")?.Value
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new InvalidOperationException($"{catalog.Name} appcast has no download URL.");
        }

        downloadUrl = ResolveDownloadUrl(catalog, downloadUrl);
        SourceTrustPolicy.ValidateDownloadUrl(catalog, downloadUrl);

        var version = ResolveVersion(catalog, item, enclosure);
        return new ReleaseInfo
        {
            Version = VersionComparer.CleanVersion(version, catalog.VersionRegex),
            DownloadUrl = downloadUrl,
            AssetName = Path.GetFileName(new Uri(downloadUrl).AbsolutePath),
            SourceName = "Official appcast",
            TrustSummary = SourceTrustPolicy.Describe(catalog, downloadUrl)
        };
    }

    private static string ResolveVersion(CatalogEntry catalog, XElement item, XElement? enclosure)
    {
        var version = enclosure?.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.LocalName is "shortVersionString" or "version")?.Value;

        if (string.IsNullOrWhiteSpace(version))
        {
            version = item.Descendants()
                .FirstOrDefault(element =>
                    element.Name.LocalName is "shortVersionString" or "version")?.Value;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            version = item.Descendants().FirstOrDefault(element => element.Name.LocalName == "title")?.Value;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException($"{catalog.Name} appcast has no version.");
        }

        return version;
    }

    private static string ResolveDownloadUrl(CatalogEntry catalog, string downloadUrl)
    {
        if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        var baseUrl = catalog.DownloadBaseUrl ?? catalog.VersionUrl ?? string.Empty;
        return new Uri(new Uri(baseUrl), downloadUrl).ToString();
    }
}
