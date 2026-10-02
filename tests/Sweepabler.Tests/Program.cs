using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ProperAppUpdater.Models;
using ProperAppUpdater.Services;

namespace ProperAppUpdater.Tests;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--self-channel-check", StringComparer.OrdinalIgnoreCase))
        {
            return await SelfUpdateTests.CheckLiveChannelAsync();
        }

        if (args.Contains("--live-audit", StringComparer.OrdinalIgnoreCase))
        {
            return await RunLiveAuditAsync();
        }

        var tests = new (string Name, Func<Task> Run)[]
        {
            ("Version comparison keeps stable and vendor revisions accurate", TestVersionComparisonAsync),
            ("Embedded catalog contains the hardened selections", TestEmbeddedCatalogAsync),
            ("Chocolatey parser removes stale and alias rows", TestChocolateyParserAsync),
            ("Package re-query distinguishes failure from no update", TestPackageRequeryDecisionAsync),
            ("GitHub provider falls back to a matching stable asset", TestGitHubFallbackAsync),
            ("GitHub cache survives a live rate limit", TestGitHubRateLimitCacheAsync),
            ("Electron feed refuses an unrelated fallback asset", TestElectronAssetMismatchAsync),
            ("Redirect policy allows GitHub CDN but blocks foreign hosts", TestRedirectPolicyAsync),
            ("Catalog publisher pin is not bypassed by a hash", TestPinnedPublisherWithHashAsync),
            ("Authenticode validates a signed Windows binary", TestAuthenticodeAsync),
            ("Stalled downloads time out without a partial installer", TestDownloadTimeoutAsync),
            ("Catalog path segments cannot escape the download root", TestSafeDownloadPathAsync),
            ("Cancellation cleanup kills a child process", TestProcessTerminationAsync),
            ("Download cleanup does not traverse a junction", TestCleanupJunctionAsync),
            ("Cancelled JSON save preserves the previous file", TestAtomicJsonCancellationAsync),
            ("Ignore-list replacement can remove existing entries", TestIgnoreListReplacementAsync),
            ("Ignore picker collapses duplicate registry versions", TestIgnorePickerDeduplicationAsync),
            ("Downloads start small and use at most three slots", UpdatePipelineTests.ParallelQueueAsync),
            ("Cancellation drains active and queued downloads", UpdatePipelineTests.CancellationAsync),
            ("Large downloads yield then regain bandwidth", UpdatePipelineTests.BandwidthReleaseAsync),
            ("Ready installers and package work do not stall", UpdatePipelineTests.ReadyOrderAsync),
            ("Prepared bytes are reverified before execution", UpdatePipelineTests.ChangedPreparedFileAsync),
            ("Truncated downloads are rejected and removed", UpdatePipelineTests.IncompleteDownloadAsync),
            ("Translations match and maintenance failures stay visible", UpdatePipelineTests.LocalizationAsync),
            ("Setup installs and verifies all required tools", SetupAndMaintenanceTests.RequiredToolsAsync),
            ("A failed required tool blocks setup completion", SetupAndMaintenanceTests.FailedToolAsync),
            ("Cancelled setup starts no later installations", SetupAndMaintenanceTests.CancelledSetupAsync),
            ("Verified installer cache avoids repeat downloads", SetupAndMaintenanceTests.VerifiedCacheAsync),
            ("Downloads reject a junction before writing", SetupAndMaintenanceTests.LinkedDownloadAsync),
            ("Package and self-maintenance identities reject unsafe inputs", SetupAndMaintenanceTests.IdentitiesAsync),
            ("Update/removal helpers parse with literal bounded deletion", SetupAndMaintenanceTests.HelperSyntaxAsync),
            ("Python variants group without losing individual identities", AppFamilyTests.PythonVariantsAsync),
            ("Family headers refresh after updates and removals", AppFamilyTests.HeaderAfterRemovalAsync),
            ("Self-update finds verified development EXEs with legacy settings", SelfUpdateTests.DefaultChannelAsync),
            ("Explicit self-update checks see fresh releases despite cached metadata", SelfUpdateTests.FreshClickAsync),
            ("Self-update detection checks versions without downloading an installer", TestSelfUpdateDetectionAsync),
            ("Unverified newer self-updates cannot signal availability", TestSelfUpdateUnverifiedAsync),
            ("Every self-update detection checks fresh release metadata", TestSelfUpdateDetectionFreshnessAsync),
            ("Cancelled self-update detection propagates cancellation", TestSelfUpdateDetectionCancellationAsync)
        };

        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS  {test.Name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL  {test.Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static async Task<int> RunLiveAuditAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var scan = await new PackageManagerUpdateScanner().ScanWithInventoryAsync(timeout.Token);

        Console.WriteLine($"Live package-manager scan: {scan.Updates.Count} update(s), {scan.ManagedDisplayNames.Count} managed name(s).");
        foreach (var update in scan.Updates)
        {
            Console.WriteLine($"  {update.Provider,-6} {update.PackageId}: {update.InstalledVersion} -> {update.AvailableVersion}");
        }

        Assert(scan.Updates.All(update =>
                !string.Equals(update.InstalledVersion, update.AvailableVersion, StringComparison.OrdinalIgnoreCase)),
            "live scan returned an equal-version phantom update");
        var chocoIds = scan.Updates
            .Where(update => update.Provider == "choco")
            .Select(update => update.PackageId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert(!(chocoIds.Contains("python") || chocoIds.Contains("python3")) ||
               !chocoIds.Any(id => id.StartsWith("python", StringComparison.OrdinalIgnoreCase) &&
                                   id.Length > "python3".Length &&
                                   id["python".Length..].All(char.IsDigit)),
            "live scan returned Python aliases alongside a concrete Python package");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var github = new GitHubReleaseProvider(client);
        var factory = new UpdateProviderFactory(
            github,
            new ElectronYamlReleaseProvider(client),
            new OfficialWebPageProvider(client),
            new AppcastReleaseProvider(client));
        await using var catalogStream = typeof(CatalogService).Assembly.GetManifestResourceStream("catalog.json")
            ?? throw new InvalidOperationException("embedded catalog.json was missing");
        var catalog = await JsonSerializer.DeserializeAsync<List<CatalogEntry>>(
            catalogStream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            timeout.Token)
            ?? throw new InvalidOperationException("embedded catalog could not be parsed");
        var installedApps = new InstalledAppScanner().Scan();
        var checkedCatalogEntries = 0;
        foreach (var entry in catalog)
        {
            var installed = AppMatcher.FindInstalledMatch(entry, installedApps);
            if (installed is null || UpdateSkipPolicy.ShouldSkip(entry, installed, out _))
            {
                continue;
            }

            var release = await factory.Resolve(entry).GetLatestAsync(entry, timeout.Token);
            checkedCatalogEntries++;
            Console.WriteLine($"Live catalog: {entry.Name}: {installed.DisplayVersion} -> {release.Version} / {release.AssetName}");
            if (entry.Id == "localsend")
            {
                Assert(!release.AssetName.Contains("CLI", StringComparison.OrdinalIgnoreCase),
                    "live LocalSend selection resolved to the CLI build");
            }
        }

        Assert(checkedCatalogEntries > 0, "live audit found no installed catalog apps to check");
        Console.WriteLine($"Live catalog scan: {checkedCatalogEntries} installed direct entry/entries resolved.");

        Console.WriteLine("Live audit passed.");
        return 0;
    }

    private static Task TestVersionComparisonAsync()
    {
        Assert(VersionComparer.Compare("1.0.0", "1.0.0beta") > 0, "stable release ranked below beta");
        Assert(VersionComparer.Compare("v2.4.0", "2.4.1") < 0, "normal upgrade comparison failed");
        Assert(VersionComparer.Compare("2026.6.1", "2026.6.1") == 0, "equal versions did not compare equal");
        Assert(VersionComparer.CleanVersion("v2.55.0.windows.5") == "2.55.0.5",
            "Git for Windows packaging revision was discarded");
        Assert(VersionComparer.Compare("2.55.0.2", "v2.55.0.windows.5") < 0,
            "Git for Windows packaging update was not detected");
        return Task.CompletedTask;
    }

    private static async Task TestEmbeddedCatalogAsync()
    {
        await using var stream = typeof(CatalogService).Assembly.GetManifestResourceStream("catalog.json")
            ?? throw new InvalidOperationException("embedded catalog.json was missing");
        var entries = await JsonSerializer.DeserializeAsync<List<CatalogEntry>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("embedded catalog could not be parsed");

        Assert(entries.Count > 0, "embedded catalog was empty");
        Assert(entries.All(entry => !string.IsNullOrWhiteSpace(entry.Id) && !string.IsNullOrWhiteSpace(entry.Name)),
            "embedded catalog contained a nameless/id-less entry");
        Assert(entries.All(entry => !entry.Id.Equals("powertoys", StringComparison.OrdinalIgnoreCase)),
            "scope-sensitive PowerToys direct installer returned to the embedded catalog");
        var localSend = entries.Single(entry => entry.Id == "localsend");
        Assert(localSend.AssetRegex?.Contains("(?!CLI-)", StringComparison.Ordinal) == true,
            "embedded LocalSend rule can still match the CLI executable");
    }

    private static Task TestChocolateyParserAsync()
    {
        const string output = """
            bitwarden|2026.6.1|2026.6.1|false
            python|3.13.4|3.14.0|false
            python3|3.13.4|3.14.0|false
            python313|3.13.4|3.13.7|false
            git|2.50.0|2.51.0|false
            """;

        var updates = PackageManagerUpdateScanner.ParseChocolatey(output);
        Assert(updates.Count == 2, $"expected 2 real updates, got {updates.Count}");
        Assert(updates.Any(update => update.PackageId == "python313"), "concrete Python package was removed");
        Assert(updates.Any(update => update.PackageId == "git"), "ordinary package was removed");
        Assert(updates.All(update => update.PackageId is not "python" and not "python3" and not "bitwarden"),
            "stale or alias package survived filtering");
        return Task.CompletedTask;
    }

    private static Task TestPackageRequeryDecisionAsync()
    {
        var failed = new PackageManagerUpdateScanner.ProcessResult(1, string.Empty, "network failed", false);
        Assert(PackageManagerUpdateScanner.WingetQueryShowsPending(failed, "Git.Git"),
            "WinGet command failure was treated as current");
        Assert(PackageManagerUpdateScanner.ChocolateyQueryShowsPending(failed, "git"),
            "Chocolatey command failure was treated as current");
        Assert(PackageManagerUpdateScanner.ScoopQueryShowsPending(failed, "git"),
            "Scoop command failure was treated as current");

        var noWingetUpdate = new PackageManagerUpdateScanner.ProcessResult(
            unchecked((int)0x8A15002B),
            "No available upgrade found.",
            string.Empty,
            false);
        Assert(!PackageManagerUpdateScanner.WingetQueryShowsPending(noWingetUpdate, "Git.Git"),
            "official WinGet no-update result was treated as pending");

        var header = $"{"Name",-24}{"Id",-28}{"Version",-12}{"Available",-12}Source";
        var separator = new string('-', header.Length);
        var row = $"{"Git for Windows",-24}{"Git.Git",-28}{"2.55.0",-12}{"2.55.1",-12}winget";
        var scopeMismatchWithRow = new PackageManagerUpdateScanner.ProcessResult(
            unchecked((int)0x8A15002B),
            $"{header}\n{separator}\n{row}\n",
            string.Empty,
            false);
        Assert(PackageManagerUpdateScanner.WingetQueryShowsPending(scopeMismatchWithRow, "Git.Git"),
            "still-listed WinGet update was hidden by UPDATE_NOT_APPLICABLE");

        var noChocoUpdate = new PackageManagerUpdateScanner.ProcessResult(0, "git|2.55.0|2.55.0|false", string.Empty, false);
        Assert(!PackageManagerUpdateScanner.ChocolateyQueryShowsPending(noChocoUpdate, "git"),
            "equal-version Chocolatey row was treated as pending");
        var noScoopUpdate = new PackageManagerUpdateScanner.ProcessResult(0, "Scoop is up to date.", string.Empty, false);
        Assert(!PackageManagerUpdateScanner.ScoopQueryShowsPending(noScoopUpdate, "git"),
            "Scoop banner was treated as pending");
        return Task.CompletedTask;
    }

    private static async Task TestGitHubFallbackAsync()
    {
        const string latest = """
            {
              "tag_name": "v2.0.0",
              "draft": false,
              "prerelease": false,
              "assets": [
                {
                  "name": "LocalSend-CLI-2.0.0-windows-x86-64.exe",
                  "browser_download_url": "https://github.com/localsend/localsend/releases/download/v2.0.0/LocalSend-CLI-2.0.0-windows-x86-64.exe",
                  "digest": "sha256:AAAAAAAA"
                }
              ]
            }
            """;

        const string releases = """
            [
              {
                "tag_name": "v1.99.0-beta",
                "draft": false,
                "prerelease": true,
                "assets": [
                  {
                    "name": "LocalSend-1.99.0-windows-x86-64.exe",
                    "browser_download_url": "https://github.com/localsend/localsend/releases/download/v1.99.0-beta/LocalSend-1.99.0-windows-x86-64.exe"
                  }
                ]
              },
              {
                "tag_name": "v1.17.0",
                "draft": false,
                "prerelease": false,
                "assets": [
                  {
                    "name": "LocalSend-1.17.0-windows-x86-64.exe",
                    "browser_download_url": "https://github.com/localsend/localsend/releases/download/v1.17.0/LocalSend-1.17.0-windows-x86-64.exe",
                    "digest": "sha256:BBBBBBBB",
                    "size": 12345678
                  }
                ]
              }
            ]
            """;

        var handler = new FakeGitHubHandler(latest, releases);
        using var client = new HttpClient(handler);
        var provider = new GitHubReleaseProvider(client, cacheRoot: string.Empty);
        var catalog = new CatalogEntry
        {
            Id = "localsend",
            Name = "LocalSend",
            Provider = "github",
            Owner = "localsend",
            Repo = "localsend",
            AssetRegex = "^LocalSend-(?!CLI-).*?-windows-x86-64\\.exe$",
            OfficialDomains = new List<string> { "github.com" }
        };

        var release = await provider.GetLatestAsync(catalog, CancellationToken.None);
        Assert(release.Version == "1.17.0", $"expected stable fallback 1.17.0, got {release.Version}");
        Assert(release.AssetName == "LocalSend-1.17.0-windows-x86-64.exe", "selected the wrong asset");
        Assert(release.DownloadSizeBytes == 12345678, "GitHub asset size was not retained for download priority");
        Assert(handler.RequestCount == 2, $"expected /latest plus release-list query, got {handler.RequestCount}");
    }

    private static async Task TestGitHubRateLimitCacheAsync()
    {
        const string latest = """
            {
              "tag_name": "v3.6.4",
              "draft": false,
              "prerelease": false,
              "assets": [
                {
                  "name": "GitHubDesktopSetup-x64.exe",
                  "browser_download_url": "https://github.com/desktop/desktop/releases/download/release-3.6.4/GitHubDesktopSetup-x64.exe",
                  "digest": "sha256:CCCCCCCC"
                }
              ]
            }
            """;
        var cacheRoot = Path.Combine(Path.GetTempPath(), "SupurucuTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheRoot);

        try
        {
            var catalog = new CatalogEntry
            {
                Id = "github-desktop",
                Name = "GitHub Desktop",
                Owner = "desktop",
                Repo = "desktop",
                AssetRegex = "GitHubDesktopSetup-x64\\.exe$",
                OfficialDomains = new List<string> { "github.com" }
            };
            var initialHandler = new FakeGitHubHandler(latest, "[]");
            using (var initialClient = new HttpClient(initialHandler))
            {
                var provider = new GitHubReleaseProvider(initialClient, cacheRoot);
                var release = await provider.GetLatestAsync(catalog, CancellationToken.None);
                Assert(release.Version == "3.6.4", "initial cached release was wrong");
                Assert(initialHandler.RequestCount == 1, "initial release did not make exactly one request");

                await provider.GetLatestAsync(catalog, CancellationToken.None);
                Assert(initialHandler.RequestCount == 1, "fresh cache still made a network request");
            }

            var cachePath = Directory.EnumerateFiles(cacheRoot, "*.json").Single();
            var cacheEntry = JsonSerializer.Deserialize<GitHubReleaseProvider.GitHubApiCacheEntry>(
                await File.ReadAllTextAsync(cachePath))
                ?? throw new InvalidOperationException("could not read GitHub cache test entry");
            cacheEntry.StoredUtc = DateTimeOffset.UtcNow.AddDays(-1);
            await AtomicJsonFile.WriteAsync(cachePath, cacheEntry, new JsonSerializerOptions(), CancellationToken.None);

            var rateLimitedHandler = new StaticStatusHandler(HttpStatusCode.Forbidden);
            using var limitedClient = new HttpClient(rateLimitedHandler);
            var limitedProvider = new GitHubReleaseProvider(limitedClient, cacheRoot);
            var fallback = await limitedProvider.GetLatestAsync(catalog, CancellationToken.None);
            Assert(fallback.Version == "3.6.4", "rate-limited provider did not use recent cache");
            Assert(rateLimitedHandler.RequestCount == 1, "stale-cache rate-limit test did not reach the network");
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    private static Task TestPinnedPublisherWithHashAsync()
    {
        LocalizationService.SetCurrent("en");
        var catalog = new CatalogEntry { SignaturePublisherContains = "Microsoft" };

        var mismatched = new UpdateRecord();
        UpdateExecutor.ApplyTrustDecision(
            mismatched,
            catalog,
            new SignatureResult(true, true, "Unexpected Publisher", "valid signature"),
            "sha256");
        Assert(mismatched.VerifiedBy == "sha256", "matching hash was not retained");
        Assert(mismatched.TrustWarning.Contains("Unexpected Publisher", StringComparison.Ordinal),
            "publisher mismatch was hidden by the hash");

        var expected = new UpdateRecord();
        UpdateExecutor.ApplyTrustDecision(
            expected,
            catalog,
            new SignatureResult(true, true, "Microsoft Corporation", "valid signature"),
            "sha256");
        Assert(expected.VerifiedBy == "sha256", "matching hash was not retained for expected signer");
        Assert(string.IsNullOrEmpty(expected.TrustWarning), "expected signer produced a warning");
        return Task.CompletedTask;
    }

    private static Task TestAuthenticodeAsync()
    {
        var systemBinary = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "kernel32.dll");
        var signature = SignatureInspector.Verify(systemBinary);
        Assert(signature.HasSignature, $"Windows system binary appeared unsigned: {signature.StatusText}");
        Assert(signature.IsTrusted, $"Windows rejected its own signed binary: {signature.StatusText}");
        Assert(!string.IsNullOrWhiteSpace(signature.SignerName), "trusted system binary had no signer name");
        return Task.CompletedTask;
    }

    private static async Task TestDownloadTimeoutAsync()
    {
        var downloadRoot = Path.Combine(Path.GetTempPath(), "SupurucuTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(downloadRoot);

        try
        {
            using var client = new HttpClient(new StalledDownloadHandler())
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            var executor = new UpdateExecutor(
                client,
                downloadTimeout: TimeSpan.FromMilliseconds(120),
                downloadsRoot: downloadRoot);
            var catalog = new CatalogEntry
            {
                Id = "stalled-test",
                Name = "Stalled Test",
                Provider = "github",
                OfficialDomains = new List<string> { "example.com" }
            };

            var record = await executor.InstallAsync(
                catalog,
                "Stalled Test",
                "1.0.0",
                "2.0.0",
                "https://example.com/stalled-test.exe",
                string.Empty,
                string.Empty,
                "test",
                "test",
                new Progress<string>(),
                CancellationToken.None);

            Assert(record.Outcome == "Failed", "stalled download did not become a failed update record");
            Assert(record.Error.Contains("timed out", StringComparison.OrdinalIgnoreCase),
                $"stalled download reported the wrong error: {record.Error}");
            Assert(!Directory.EnumerateFiles(downloadRoot, "*.partial", SearchOption.AllDirectories).Any(),
                "stalled download left a partial installer behind");
        }
        finally
        {
            if (Directory.Exists(downloadRoot))
            {
                Directory.Delete(downloadRoot, recursive: true);
            }
        }
    }

    private static Task TestSafeDownloadPathAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "SupurucuTests", Guid.NewGuid().ToString("N"), "Downloads");
        var target = UpdateExecutor.BuildSafeDownloadDirectory(root, "..\\..\\outside", "..");
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Assert(target.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase),
            $"malformed catalog path escaped download root: {target}");
        Assert(!target.Contains($"{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}", StringComparison.Ordinal),
            "malformed catalog path retained a parent traversal segment");
        return Task.CompletedTask;
    }

    private static async Task TestElectronAssetMismatchAsync()
    {
        const string yaml = """
            version: 9.9.9
            path: signal-desktop-win-arm64-9.9.9.exe
            sha512: AAAA
            """;
        using var client = new HttpClient(new StaticContentHandler(yaml, "text/yaml"));
        var provider = new ElectronYamlReleaseProvider(client);
        var catalog = new CatalogEntry
        {
            Name = "Signal Desktop",
            VersionUrl = "https://updates.signal.org/desktop/latest.yml",
            DownloadBaseUrl = "https://updates.signal.org/desktop/",
            AssetRegex = "signal-desktop-win-x64-.*\\.exe$",
            OfficialDomains = new List<string> { "updates.signal.org" }
        };

        try
        {
            await provider.GetLatestAsync(catalog, CancellationToken.None);
            throw new InvalidOperationException("provider selected an asset that did not match the configured architecture");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("no asset matching", StringComparison.Ordinal))
        {
        }
    }

    private static Task TestRedirectPolicyAsync()
    {
        var catalog = new CatalogEntry
        {
            Name = "GitHub app",
            OfficialDomains = new List<string> { "github.com" }
        };

        SourceTrustPolicy.ValidateRedirectTarget(
            catalog,
            "https://github.com/owner/repo/releases/download/v1/app.exe",
            "https://release-assets.githubusercontent.com/github-production-release-asset/app.exe");

        try
        {
            SourceTrustPolicy.ValidateRedirectTarget(
                catalog,
                "https://github.com/owner/repo/releases/download/v1/app.exe",
                "https://downloads.example.net/app.exe");
            throw new InvalidOperationException("foreign redirect was accepted");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("not in officialDomains", StringComparison.Ordinal))
        {
        }

        return Task.CompletedTask;
    }

    private static async Task TestProcessTerminationAsync()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -Command \"Start-Sleep -Seconds 30\"",
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("could not start harmless child process");

        await Task.Delay(150);
        await ProcessTermination.KillTreeAndWaitAsync(process);
        Assert(process.HasExited, "child process remained alive after cancellation cleanup");
    }

    private static async Task TestCleanupJunctionAsync()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "SupurucuTests", Guid.NewGuid().ToString("N"));
        var downloads = Path.Combine(testRoot, "Downloads");
        var outside = Path.Combine(testRoot, "Outside");
        var junction = Path.Combine(downloads, "outside-link");
        Directory.CreateDirectory(downloads);
        Directory.CreateDirectory(outside);

        try
        {
            var internalFile = Path.Combine(downloads, "old-installer.exe");
            var outsideFile = Path.Combine(outside, "must-survive.exe");
            await File.WriteAllTextAsync(internalFile, "inside");
            await File.WriteAllTextAsync(outsideFile, "outside");
            var old = DateTime.UtcNow.AddDays(-30);
            File.SetLastWriteTimeUtc(internalFile, old);
            File.SetLastWriteTimeUtc(outsideFile, old);

            using var mklink = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/d /c mklink /J \"{junction}\" \"{outside}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }) ?? throw new InvalidOperationException("could not create junction test process");
            await mklink.WaitForExitAsync();
            if (mklink.ExitCode != 0)
            {
                throw new InvalidOperationException("could not create the junction used by the safety test");
            }

            var result = LocalCleanupService.CleanDownloads(downloads, DateTime.UtcNow, CancellationToken.None);
            Assert(result.DeletedFiles == 1, $"expected one internal deletion, got {result.DeletedFiles}");
            Assert(!File.Exists(internalFile), "old file inside Downloads was not removed");
            Assert(File.Exists(outsideFile), "cleanup followed a junction and deleted an outside file");
        }
        finally
        {
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction);
            }

            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static async Task TestAtomicJsonCancellationAsync()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "SupurucuTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(testRoot, "history.json");
        Directory.CreateDirectory(testRoot);
        await File.WriteAllTextAsync(path, "{\"original\":true}");

        try
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                await AtomicJsonFile.WriteAsync(
                    path,
                    Enumerable.Range(0, 100_000).ToArray(),
                    new JsonSerializerOptions { WriteIndented = true },
                    cancelled.Token);
                throw new InvalidOperationException("cancelled save unexpectedly completed");
            }
            catch (OperationCanceledException)
            {
            }

            var content = await File.ReadAllTextAsync(path);
            Assert(content == "{\"original\":true}", "cancelled save damaged the previous file");
            Assert(!Directory.EnumerateFiles(testRoot, "*.tmp").Any(), "cancelled save left a temp file behind");
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static async Task TestIgnoreListReplacementAsync()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "SupurucuTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(testRoot, "unsupported.ignore.json");
        Directory.CreateDirectory(testRoot);
        await File.WriteAllTextAsync(path, "[\"Old ignored app\"]");

        try
        {
            var count = await UnsupportedIgnoreService.ReplaceAsync(path, Array.Empty<string>(), CancellationToken.None);
            var saved = JsonSerializer.Deserialize<List<string>>(await File.ReadAllTextAsync(path));
            Assert(count == 0, "empty selection reported a nonzero saved count");
            Assert(saved is { Count: 0 }, "empty selection did not clear the old ignore entry");
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static Task TestIgnorePickerDeduplicationAsync()
    {
        var apps = new[]
        {
            new InstalledApp { DisplayName = "AMD Ryzen Master", DisplayVersion = "2.13.0.2908" },
            new InstalledApp { DisplayName = "AMD Ryzen Master", DisplayVersion = "2.14.2.3341" },
            new InstalledApp { DisplayName = "AnyDesk", DisplayVersion = "9.7.9" }
        };

        var distinct = PirateLoverWindow.SelectDistinctApps(apps);
        Assert(distinct.Count == 2, $"expected 2 distinct apps, got {distinct.Count}");
        Assert(distinct.Single(app => app.DisplayName == "AMD Ryzen Master").DisplayVersion == "2.14.2.3341",
            "ignore picker kept the stale duplicate version");
        return Task.CompletedTask;
    }

    private static async Task TestSelfUpdateDetectionAsync()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "SupurucuTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheRoot);
        try
        {
            using var handler = new SelfUpdateMetadataHandler();
            using var client = new HttpClient(handler);
            var service = new SelfMaintenanceService(client, cacheRoot);
            var current = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? throw new InvalidOperationException("the app build did not expose its current version");
            foreach (var version in new[] { "99.0.0", VersionComparer.CleanVersion(current), "0.1.0" })
            {
                handler.Version = version;
                var release = await service.CheckForUpdateAsync(AppSettings.DefaultUpdateRepository, CancellationToken.None);
                var expected = VersionComparer.Compare(current, version) < 0;
                Assert((release is not null) == expected, $"wrong self-update availability for {current} -> {version}");
                if (release is not null)
                {
                    Assert(release.Version == version && release.AssetName == "Supurucu.exe" && release.Sha256 == new string('A', 64),
                        "self-update detection lost the verified release identity");
                }
            }
            Assert(handler.RequestCount == 3, "self-update detection did not fetch release metadata once per check");
            Assert(Directory.EnumerateFiles(cacheRoot).All(path => Path.GetExtension(path) == ".json"),
                "metadata-only self-update detection wrote an installer");
        }
        finally { Directory.Delete(cacheRoot, recursive: true); }
    }

    private static async Task TestSelfUpdateUnverifiedAsync()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "SupurucuTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheRoot);
        try
        {
            using var handler = new SelfUpdateMetadataHandler { Version = "99.0.0" };
            using var client = new HttpClient(handler);
            var service = new SelfMaintenanceService(client, cacheRoot);
            foreach (var digest in new[] { "", "sha256:" + new string('A', 63), "sha256:" + new string('Z', 64) })
            {
                handler.Digest = digest;
                try
                {
                    await service.CheckForUpdateAsync(AppSettings.DefaultUpdateRepository, CancellationToken.None);
                    throw new InvalidOperationException("an unverified newer self-update signalled availability");
                }
                catch (InvalidOperationException exception) when (exception.Message == LocalizationService.Current.Get("SelfUnverified"))
                {
                }
            }
            Assert(handler.RequestCount == 3, "unverified release checks made unexpected installer requests");
        }
        finally { Directory.Delete(cacheRoot, recursive: true); }
    }

    private static async Task TestSelfUpdateDetectionFreshnessAsync()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "SupurucuTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheRoot);
        try
        {
            using var handler = new SelfUpdateMetadataHandler { Version = "0.1.0" };
            using var client = new HttpClient(handler);
            var service = new SelfMaintenanceService(client, cacheRoot);
            Assert(await service.CheckForUpdateAsync(AppSettings.DefaultUpdateRepository, CancellationToken.None) is null,
                "an older build was offered as a self-update");
            handler.Version = "99.0.0";
            Assert(await service.CheckForUpdateAsync(AppSettings.DefaultUpdateRepository, CancellationToken.None) is not null,
                "a newly published self-update was hidden behind the fresh cache");
            handler.Version = "0.1.0";
            Assert(await service.CheckForUpdateAsync(AppSettings.DefaultUpdateRepository, CancellationToken.None) is null,
                "the check retained a withdrawn update from cached metadata");
            Assert(handler.RequestCount == 3, "explicit self-update detection reused the 30-minute cache");
        }
        finally { Directory.Delete(cacheRoot, recursive: true); }
    }

    private static async Task TestSelfUpdateDetectionCancellationAsync()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "SupurucuTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheRoot);
        try
        {
            using var handler = new SelfUpdateMetadataHandler { Stall = true };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            using var cancellation = new CancellationTokenSource();
            var check = new SelfMaintenanceService(client, cacheRoot).CheckForUpdateAsync(AppSettings.DefaultUpdateRepository, cancellation.Token);
            await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            try
            {
                await check;
                throw new InvalidOperationException("cancelled self-update detection unexpectedly completed");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            Assert(handler.RequestCount == 1, "cancellation started a later self-update request");
        }
        finally { Directory.Delete(cacheRoot, recursive: true); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class SelfUpdateMetadataHandler : HttpMessageHandler
    {
        public string Version { get; set; } = "99.0.0";
        public string Digest { get; set; } = "sha256:" + new string('A', 64);
        public bool Stall { get; init; }
        public int RequestCount { get; private set; }
        public TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert(request.Method == HttpMethod.Get && request.RequestUri?.Host == "api.github.com" &&
                   request.RequestUri.PathAndQuery == "/repos/27-coder/Sweepabler/releases?per_page=30",
                "self-update detection requested installer bytes or an unexpected release channel");
            RequestCount++;
            RequestStarted.TrySetResult();
            if (Stall) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var body = JsonSerializer.Serialize(new[]
            {
                new
                {
                    tag_name = "v" + Version,
                    draft = false,
                    prerelease = false,
                    assets = new[]
                    {
                        new
                        {
                            name = "Supurucu.exe",
                            browser_download_url = "https://github.com/27-coder/Sweepabler/releases/download/v" + Version + "/Supurucu.exe",
                            digest = Digest
                        }
                    }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request
            };
        }
    }

    private sealed class FakeGitHubHandler(string latest, string releases) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var body = request.RequestUri?.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal) == true
                ? latest
                : releases;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StaticContentHandler(string body, string contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType)
            });
        }
    }

    private sealed class StaticStatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = new HttpResponseMessage(statusCode);
            if (statusCode == HttpStatusCode.Forbidden)
            {
                response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            }

            return Task.FromResult(response);
        }
    }

    private sealed class StalledDownloadHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new StalledReadStream()),
                RequestMessage = request
            });
        }
    }

    private sealed class StalledReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
