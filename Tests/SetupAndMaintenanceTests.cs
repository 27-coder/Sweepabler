using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using ProperAppUpdater.Models;
using ProperAppUpdater.Services;

namespace ProperAppUpdater.Tests;

internal static class SetupAndMaintenanceTests
{
    public static async Task RequiredToolsAsync()
    {
        var host = new FakeTools();
        await new RequiredToolSetupService(host).EnsureAsync(new Progress<string>(), CancellationToken.None);
        Check(host.Installed.SequenceEqual(new[] { "winget", "git", "choco", "scoop" }), "required dependencies installed out of order");
        Check(host.Usable.SetEquals(RequiredToolSetupService.RequiredTools), "setup completed without all required tools");
        await new RequiredToolSetupService(host).EnsureAsync(new Progress<string>(), CancellationToken.None);
        Check(host.Installed.Count == 4, "working tools were unnecessarily reinstalled");
    }

    public static async Task FailedToolAsync()
    {
        var host = new FakeTools { BrokenTool = "git" };
        var failed = false;
        try { await new RequiredToolSetupService(host).EnsureAsync(new Progress<string>(), CancellationToken.None); }
        catch (InvalidOperationException) { failed = true; }
        Check(failed && host.Installed.SequenceEqual(new[] { "winget", "git" }), "failed Git setup was ignored or later tools were installed");
    }

    public static async Task CancelledSetupAsync()
    {
        using var cancellation = new CancellationTokenSource();
        var host = new FakeTools { CancelAfterFirstInstall = cancellation };
        var cancelled = false;
        try { await new RequiredToolSetupService(host).EnsureAsync(new Progress<string>(), cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && host.Installed.Count == 1, "setup cancellation started more tools");
    }

    public static async Task VerifiedCacheAsync()
    {
        var root = Scratch();
        try
        {
            var bytes = new byte[] { 3, 1, 4, 1, 5 };
            using var handler = new CountingHandler(bytes);
            using var client = new HttpClient(handler);
            var executor = new UpdateExecutor(client, downloadsRoot: root);
            var catalog = new CatalogEntry { Id = "cache", Name = "cache", OfficialDomains = new() { "example.com" } };
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            Task<UpdateRecord> Prepare() => executor.PrepareAsync(catalog, new UpdateRecord { ToVersion = "2", DownloadUrl = "https://example.com/cache.exe" },
                "", hash, new Progress<string>(), CancellationToken.None);
            var first = await Prepare();
            var cached = await Prepare();
            Check(handler.Requests == 1 && first.Error == "" && cached.Error == "", "verified cache did not avoid a second network download");
            await File.WriteAllBytesAsync(first.InstallerPath, new byte[] { 9, 9 });
            var fresh = await Prepare();
            Check(handler.Requests == 2 && fresh.Error == "", "changed cached bytes were reused or not replaced");
            Check((await File.ReadAllBytesAsync(fresh.InstallerPath)).SequenceEqual(bytes), "the refreshed cache contains unexpected bytes");
        }
        finally { Directory.Delete(root, true); }
    }

    public static async Task LinkedDownloadAsync()
    {
        var root = Scratch();
        var external = Scratch();
        var link = Path.Combine(root, "linked-app");
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "/c", "mklink", "/J", link, external }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync();
            Check(process.ExitCode == 0, "test junction could not be created");
            using var handler = new CountingHandler(new byte[] { 1, 2, 3 });
            using var client = new HttpClient(handler);
            var catalog = new CatalogEntry { Id = "linked-app", Name = "linked-app", OfficialDomains = new() { "example.com" } };
            var record = await new UpdateExecutor(client, downloadsRoot: root).PrepareAsync(catalog,
                new UpdateRecord { ToVersion = "2", DownloadUrl = "https://example.com/test.exe" }, "", "", new Progress<string>(), CancellationToken.None);
            Check(record.Error.Contains("linked", StringComparison.OrdinalIgnoreCase), "a download followed a junction");
            Check(handler.Requests == 0 && !Directory.EnumerateFileSystemEntries(external).Any(), "junction download touched the external folder");
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(root, true);
            Directory.Delete(external, true);
        }
    }

    public static async Task IdentitiesAsync()
    {
        foreach (var invalid in new[] { "../outside", "..\\outside", "bad\" --all", "x;Remove-Item", "C:\\temp", "--all", "bucket/../app" })
        {
            var rejected = false;
            try { PackageIdentity.Require("scoop", invalid); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "unsafe package identity accepted: " + invalid);
        }
        Check(PackageIdentity.Require("winget", "Microsoft.AppInstaller") == "Microsoft.AppInstaller", "normal WinGet id rejected");
        Check(PackageIdentity.Require("scoop", "extras/firefox") == "extras/firefox", "bucket-qualified Scoop id rejected");
        Check(SafePath.FileSegment("CON.exe") == "_CON.exe", "reserved Windows device filename was retained");
        var rejectedHost = false;
        try { SelfMaintenanceService.ValidateExecutablePath(Path.Combine(Path.GetTempPath(), "dotnet.exe")); }
        catch (InvalidOperationException) { rejectedHost = true; }
        Check(rejectedHost, "delete/update accepted the .NET host executable");
        Check(SelfMaintenanceService.CreateCatalog("").Repo == "Sweepabler", "legacy empty settings did not select the official release channel");
        Check(SelfMaintenanceService.CreateCatalog("owner/repo")?.Repo == "repo", "a normal release channel was rejected");
        var rejectedRepository = false;
        try { SelfMaintenanceService.CreateCatalog("owner/repo/../../another"); }
        catch (InvalidOperationException) { rejectedRepository = true; }
        Check(rejectedRepository, "invalid release repository accepted");
        var root = Scratch();
        try
        {
            using var client = new HttpClient(new CountingHandler(new byte[] { 1 }));
            var record = await new UpdateExecutor(client, downloadsRoot: root).InstallAsync(
                new CatalogEntry { Id = "prepared", Name = "prepared" }, "prepared", "1", "2", "", "", "", "", "",
                new Progress<string>(), CancellationToken.None,
                new UpdateRecord { InstallerPath = Path.Combine(Path.GetTempPath(), "outside-prepared-test.exe") });
            Check(record.Error == LocalizationService.Current.Get("UnsafeDownloadPath"), "an out-of-cache prepared installer reached execution");
        }
        finally { Directory.Delete(root, true); }
    }

    public static async Task HelperSyntaxAsync()
    {
        var root = Scratch();
        try
        {
            var target = Path.Combine(root, "Owner's portable app", "Süpürücü.exe");
            var download = Path.Combine(AppPaths.DownloadsRoot, "test-release", "Süpürücü.exe");
            var scripts = new[] {
                SelfMaintenanceService.BuildHelperScript(target, Environment.ProcessId, download, new string('A',64)),
                SelfMaintenanceService.BuildHelperScript(target, Environment.ProcessId, null, null)
            };
            foreach (var script in scripts)
            {
                var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".ps1");
                await File.WriteAllTextAsync(path, script);
                var parse = "$tokens=$null; $errors=$null; $ast=[System.Management.Automation.Language.Parser]::ParseFile('" + path.Replace("'", "''") +
                    "',[ref]$tokens,[ref]$errors); if($errors.Count) { throw ($errors | Out-String) }; " +
                    "$commands=$ast.FindAll({param($n) $n -is [System.Management.Automation.Language.CommandAst]},$true); " +
                    "foreach($c in $commands) { if($c.GetCommandName() -eq 'Remove-Item') { if($c.Extent.Text -match '-Recursive' -or $c.Extent.Text -notmatch '-LiteralPath') { throw 'Unsafe removal command' } } }";
                var start = WindowsSetupToolHost.PowerShellStart(parse);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.RedirectStandardError = true;
                using var process = Process.Start(start)!;
                var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                Check(process.ExitCode == 0, "maintenance helper did not parse safely: " + await error);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string Scratch()
    {
        var root = Path.Combine(Path.GetTempPath(), "SupurucuTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FakeTools : ISetupToolHost
    {
        public HashSet<string> Usable { get; } = new();
        public List<string> Installed { get; } = new();
        public string? BrokenTool { get; init; }
        public CancellationTokenSource? CancelAfterFirstInstall { get; init; }
        public Task<bool> IsUsableAsync(string tool, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Usable.Contains(tool)); }
        public Task InstallAsync(string tool, IProgress<string> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Installed.Add(tool);
            if (tool != BrokenTool) Usable.Add(tool);
            CancelAfterFirstInstall?.Cancel();
            return Task.CompletedTask;
        }
        public void RefreshEnvironment() { }
    }
    private sealed class CountingHandler(byte[] bytes) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes), RequestMessage = request });
        }
    }
}
