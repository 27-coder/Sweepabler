using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

public sealed class GitHubReleaseProvider : IUpdateProvider
{
    private static readonly TimeSpan FreshCacheLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RateLimitFallbackLifetime = TimeSpan.FromDays(7);

    private readonly HttpClient _httpClient;
    private readonly string _cacheRoot;

    public GitHubReleaseProvider(HttpClient httpClient, string? cacheRoot = null)
    {
        _httpClient = httpClient;
        _cacheRoot = cacheRoot ?? AppPaths.GitHubCacheRoot;
    }

    public async Task<ReleaseInfo> GetLatestAsync(CatalogEntry catalog, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(catalog.Owner) || string.IsNullOrWhiteSpace(catalog.Repo))
        {
            throw new InvalidOperationException($"{catalog.Name} is missing GitHub owner/repo.");
        }

        var assetRegex = string.IsNullOrWhiteSpace(catalog.AssetRegex)
            ? null
            : new Regex(catalog.AssetRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var release = catalog.IncludePrereleases
            ? await FindFirstMatchingReleaseAsync(catalog, assetRegex, includePrereleases: true, cancellationToken)
            : await GetLatestStableReleaseAsync(catalog, assetRegex, cancellationToken);

        SourceTrustPolicy.ValidateDownloadUrl(catalog, release.DownloadUrl);
        return release;
    }

    private async Task<ReleaseInfo> GetLatestStableReleaseAsync(
        CatalogEntry catalog,
        Regex? assetRegex,
        CancellationToken cancellationToken)
    {
        // /releases/latest already excludes drafts and pre-releases.
        var url = $"https://api.github.com/repos/{catalog.Owner}/{catalog.Repo}/releases/latest";
        using var document = await GetJsonAsync(url, cancellationToken);
        var release = TryBuildRelease(document.RootElement, catalog, assetRegex);
        if (release is not null)
        {
            return release;
        }

        // Some projects publish a newer release without their normal Windows installer
        // (GitHub Desktop has done this temporarily). Walk recent stable releases so one
        // packaging omission does not break every scan or select an unrelated asset.
        return await FindFirstMatchingReleaseAsync(
            catalog,
            assetRegex,
            includePrereleases: false,
            cancellationToken);
    }

    private async Task<ReleaseInfo> FindFirstMatchingReleaseAsync(
        CatalogEntry catalog,
        Regex? assetRegex,
        bool includePrereleases,
        CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{catalog.Owner}/{catalog.Repo}/releases?per_page=30";
        using var document = await GetJsonAsync(url, cancellationToken);

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            if (!includePrereleases &&
                element.TryGetProperty("prerelease", out var prerelease) &&
                prerelease.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            var release = TryBuildRelease(element, catalog, assetRegex);
            if (release is not null)
            {
                return release;
            }
        }

        throw new InvalidOperationException($"{catalog.Name} has no recent release with an asset matching {catalog.AssetRegex}.");
    }

    private static ReleaseInfo? TryBuildRelease(JsonElement release, CatalogEntry catalog, Regex? assetRegex)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var tag = release.TryGetProperty("tag_name", out var tagElement)
            ? tagElement.GetString() ?? string.Empty
            : string.Empty;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (assetRegex is not null && !assetRegex.IsMatch(name))
            {
                continue;
            }

            var downloadUrl = asset.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                continue;
            }

            return new ReleaseInfo
            {
                Version = VersionComparer.CleanVersion(tag, catalog.VersionRegex),
                DownloadUrl = downloadUrl,
                AssetName = name,
                DownloadSizeBytes = asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) && bytes > 0 ? bytes : null,
                Sha256 = ExtractSha256Digest(asset),
                SourceName = "GitHub releases",
                TrustSummary = SourceTrustPolicy.Describe(catalog, downloadUrl)
            };
        }

        return null;
    }

    private static string ExtractSha256Digest(JsonElement asset)
    {
        // Newer GitHub releases expose an asset "digest" like "sha256:<hex>".
        if (!asset.TryGetProperty("digest", out var digestElement))
        {
            return string.Empty;
        }

        var digest = digestElement.GetString();
        if (string.IsNullOrWhiteSpace(digest))
        {
            return string.Empty;
        }

        const string prefix = "sha256:";
        return digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? digest[prefix.Length..].Trim()
            : string.Empty;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        var cached = await TryLoadCacheAsync(url, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (cached is not null)
        {
            var cachedDocument = TryParseCache(cached);
            if (cachedDocument is null)
            {
                cached = null;
            }
            else if (now - cached.StoredUtc <= FreshCacheLifetime)
            {
                return cachedDocument;
            }
            else
            {
                cachedDocument.Dispose();
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Supurucu/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(cached?.ETag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
        {
            cached.StoredUtc = now;
            await TrySaveCacheAsync(url, cached, cancellationToken);
            return TryParseCache(cached)
                ?? throw new InvalidOperationException("GitHub returned 304 but the cached response is invalid.");
        }

        if (IsRateLimited(response) &&
            cached is not null &&
            now - cached.StoredUtc <= RateLimitFallbackLifetime &&
            TryParseCache(cached) is { } fallbackDocument)
        {
            return fallbackDocument;
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        // Parse before caching so a proxy/error page can never poison future scans.
        var document = JsonDocument.Parse(json);
        await TrySaveCacheAsync(url, new GitHubApiCacheEntry
        {
            Url = url,
            ETag = response.Headers.ETag?.ToString() ?? string.Empty,
            StoredUtc = now,
            Json = json
        }, cancellationToken);
        return document;
    }

    private static bool IsRateLimited(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        if (response.StatusCode != HttpStatusCode.Forbidden)
        {
            return false;
        }

        var exhausted = response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) &&
                        values.Any(value => value.Trim() == "0");
        return exhausted || response.Headers.RetryAfter is not null;
    }

    private async Task<GitHubApiCacheEntry?> TryLoadCacheAsync(string url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_cacheRoot))
        {
            return null;
        }

        try
        {
            var path = GetCachePath(url);
            if (!File.Exists(path))
            {
                return null;
            }

            await using var stream = File.OpenRead(path);
            var entry = await JsonSerializer.DeserializeAsync<GitHubApiCacheEntry>(stream, cancellationToken: cancellationToken);
            return entry is not null && entry.Url.Equals(url, StringComparison.Ordinal)
                ? entry
                : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private async Task TrySaveCacheAsync(
        string url,
        GitHubApiCacheEntry entry,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_cacheRoot))
        {
            return;
        }

        try
        {
            var path = GetCachePath(url);
            Directory.CreateDirectory(_cacheRoot);
            await AtomicJsonFile.WriteAsync(path, entry, new JsonSerializerOptions(), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            CrashLogger.Log(exception);
        }
    }

    private string GetCachePath(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return SafePath.RequireInside(_cacheRoot, Path.Combine(_cacheRoot, $"{hash[..32].ToLowerInvariant()}.json"));
    }

    private static JsonDocument? TryParseCache(GitHubApiCacheEntry entry)
    {
        try
        {
            return JsonDocument.Parse(entry.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal sealed class GitHubApiCacheEntry
    {
        public string Url { get; set; } = string.Empty;
        public string ETag { get; set; } = string.Empty;
        public DateTimeOffset StoredUtc { get; set; }
        public string Json { get; set; } = string.Empty;
    }
}
