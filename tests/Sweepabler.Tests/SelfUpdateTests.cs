using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ProperAppUpdater.Models;
using ProperAppUpdater.Services;

namespace ProperAppUpdater.Tests;

internal static class SelfUpdateTests
{
    public static async Task DefaultChannelAsync()
    {
        foreach (var json in new[] { "{}", "{\"UpdateRepository\":\"\"}", "{\"UpdateRepository\":null}", "{\"UpdateRepository\":\"  \"}" })
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
            Check(settings.UpdateRepository == "27-coder/Sweepabler", "old or empty settings did not use the official update channel");
        }
        var custom = JsonSerializer.Deserialize<AppSettings>("{\"UpdateRepository\":\" owner/custom \"}")!;
        Check(custom.UpdateRepository == "owner/custom", "an explicit release-channel override was lost");

        var root = Scratch();
        try
        {
            var catalog = SelfMaintenanceService.CreateCatalog("");
            Check(catalog.Owner == "27-coder" && catalog.Repo == "Sweepabler" && catalog.IncludePrereleases,
                "self-update did not select the official development release feed");
            using var handler = new ReleaseHandler();
            using var client = new HttpClient(handler);
            var release = await new GitHubReleaseProvider(client, root, refreshCache: true).GetLatestAsync(catalog, CancellationToken.None);
            Check(handler.LastPath == "/repos/27-coder/Sweepabler/releases?per_page=30", "self-update queried the wrong repository or stable-only endpoint");
            Check(release.Version == "1.2.2" && release.AssetName == "Supurucu.exe" && release.Sha256 == new string('A', 64),
                "self-update selected a draft/ZIP or lost the verified EXE from a development release");
        }
        finally { Directory.Delete(root, true); }
    }

    public static async Task FreshClickAsync()
    {
        var root = Scratch();
        try
        {
            using var handler = new ReleaseHandler();
            using var client = new HttpClient(handler);
            var catalog = SelfMaintenanceService.CreateCatalog(AppSettings.DefaultUpdateRepository);
            var cachedProvider = new GitHubReleaseProvider(client, root);
            await cachedProvider.GetLatestAsync(catalog, CancellationToken.None);
            handler.Version = "1.2.3-dev";
            var cached = await cachedProvider.GetLatestAsync(catalog, CancellationToken.None);
            Check(cached.Version == "1.2.2" && handler.Requests == 1, "fixture did not establish a fresh cached release");

            var fresh = await new GitHubReleaseProvider(client, root, refreshCache: true).GetLatestAsync(catalog, CancellationToken.None);
            Check(fresh.Version == "1.2.3" && handler.Requests == 2, "an explicit update check missed a newly published release because of the 30-minute cache");
        }
        finally { Directory.Delete(root, true); }
    }

    public static async Task<int> CheckLiveChannelAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var catalog = SelfMaintenanceService.CreateCatalog("");
        var release = await new GitHubReleaseProvider(client, refreshCache: true).GetLatestAsync(catalog, deadline.Token);
        Check(release.AssetName == "Supurucu.exe" && release.Sha256.Length == 64, "the published feed lacks the expected EXE and digest");
        var current = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Check(VersionComparer.Compare(current, release.Version) == 0, "the live feed does not match this build's version");
        var update = await new SelfMaintenanceService(client).PrepareUpdateAsync(new AppSettings().UpdateRepository,
            new Progress<string>(), deadline.Token);
        Check(update is null, "the published current build unexpectedly offered another update");
        Console.WriteLine($"PASS Live self-update channel: {catalog.Owner}/{catalog.Repo}; version {release.Version}; asset {release.AssetName}; SHA-256 {release.Sha256}; current build is up to date.");
        return 0;
    }

    private static string Scratch()
    {
        var root = Path.Combine(Path.GetTempPath(), "SupurucuTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ReleaseHandler : HttpMessageHandler
    {
        public string Version { get; set; } = "1.2.2-dev";
        public int Requests { get; private set; }
        public string LastPath { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastPath = request.RequestUri!.PathAndQuery;
            object Asset(string name) => new
            {
                name,
                browser_download_url = "https://github.com/27-coder/Sweepabler/releases/download/v" + Version + "/" + name,
                digest = "sha256:" + new string('A', 64)
            };
            var releases = new[]
            {
                new { tag_name = "v99.0.0-dev", draft = true, prerelease = true, assets = new[] { Asset("Supurucu.exe") } },
                new { tag_name = "v98.0.0-dev", draft = false, prerelease = true, assets = new[] { Asset("Sweepabler.zip") } },
                new { tag_name = "v" + Version, draft = false, prerelease = true, assets = new[] { Asset("Supurucu.exe") } }
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(releases), Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }
}
