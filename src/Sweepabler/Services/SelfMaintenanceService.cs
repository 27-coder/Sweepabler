using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

internal sealed partial class SelfMaintenanceService(HttpClient httpClient)
{
    public async Task<SelfUpdateDownload?> PrepareUpdateAsync(string repository, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var catalog = CreateCatalog(repository);
        var release = await new GitHubReleaseProvider(httpClient, refreshCache: true).GetLatestAsync(catalog, cancellationToken);
        var current = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
        if (VersionComparer.Compare(current, release.Version) >= 0) return null;
        if (!Sha256().IsMatch(release.Sha256) || !release.AssetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(LocalizationService.Current.Get("SelfUnverified"));

        var record = await new UpdateExecutor(httpClient).PrepareAsync(catalog, new UpdateRecord
        {
            AppName = LocalizationService.Current.AppName,
            FromVersion = current,
            ToVersion = release.Version,
            DownloadUrl = release.DownloadUrl,
            SourceName = release.SourceName,
            TrustSummary = release.TrustSummary
        }, release.Sha512, release.Sha256, progress, cancellationToken);
        if (record.Error.Length > 0) throw new InvalidOperationException(record.Error);
        SafePath.RequireInside(AppPaths.DownloadsRoot, record.InstallerPath);
        var product = FileVersionInfo.GetVersionInfo(record.InstallerPath).ProductName ?? "";
        if (!product.Contains("Süpürücü", StringComparison.OrdinalIgnoreCase) && !product.Contains("Sweepable", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(LocalizationService.Current.Get("SelfUnverified"));
        return new SelfUpdateDownload(record.InstallerPath, release.Sha256);
    }

    public static string CurrentExecutable()
    {
        var path = Environment.ProcessPath ?? "";
        ValidateExecutablePath(path);
        return path;
    }

    internal static void ValidateExecutablePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) ||
            !new[] { "Süpürücü.exe", "Supurucu.exe", "Sweepabler.exe", "Sweepable'r.exe", "Sweepable’r.exe" }.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(LocalizationService.Current.Get("SelfPortableOnly"));
        SafePath.RejectReparsePoints(path);
    }

    public static void ScheduleUpdate(SelfUpdateDownload update)
    {
        SafePath.RequireInside(AppPaths.DownloadsRoot, update.Path);
        if (!Sha256().IsMatch(update.Sha256)) throw new InvalidOperationException(LocalizationService.Current.Get("SelfUnverified"));
        LaunchHelper(BuildHelperScript(CurrentExecutable(), Environment.ProcessId, update.Path, update.Sha256));
    }

    public static void ScheduleRemoval() => LaunchHelper(BuildHelperScript(CurrentExecutable(), Environment.ProcessId, null, null));

    private static void LaunchHelper(string script)
    {
        var start = WindowsSetupToolHost.PowerShellStart(script);
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.WindowStyle = ProcessWindowStyle.Hidden;
        start.WorkingDirectory = AppPaths.DataRoot;
        using var helper = Process.Start(start) ?? throw new InvalidOperationException("Could not start the maintenance helper.");
    }

    internal static CatalogEntry CreateCatalog(string repository)
    {
        if (string.IsNullOrWhiteSpace(repository)) repository = AppSettings.DefaultUpdateRepository;
        var parts = repository.Trim().Split('/');
        if (parts.Length != 2 || parts.Any(part => !RepositoryPart().IsMatch(part) || part is "." or ".."))
            throw new InvalidOperationException("UpdateRepository must be owner/repository.");
        return new CatalogEntry
        {
            Id = "sweepabler",
            Name = "Sweepable’r",
            Provider = "github",
            Owner = parts[0],
            Repo = parts[1],
            AssetRegex = @"^(?:Süpürücü|Supurucu|Sweepabler|Sweepable['’]r)(?:[-_.].*)?\.exe$",
            OfficialDomains = new() { "github.com" },
            RequireHttps = true,
            IncludePrereleases = true
        };
    }

    internal static string BuildHelperScript(string target, int processId, string? source, string? expectedHash)
    {
        ValidateExecutablePath(target);
        var update = source is not null;
        if (update)
        {
            SafePath.RequireInside(AppPaths.DownloadsRoot, source!);
            if (expectedHash is null || !Sha256().IsMatch(expectedHash)) throw new InvalidOperationException("Invalid release checksum.");
        }
        var log = SafePath.RequireInside(AppPaths.DataRoot, Path.Combine(AppPaths.DataRoot, "self-maintenance.log"));
        var targetLiteral = Quote(Path.GetFullPath(target));
        var sourceLiteral = Quote(source is null ? "" : Path.GetFullPath(source));
        var nonce = Guid.NewGuid().ToString("N");
        return $$"""
            $target = {{targetLiteral}}
            $source = {{sourceLiteral}}
            $expectedHash = {{Quote(expectedHash ?? "")}}
            $log = {{Quote(log)}}
            $temporary = $target + '.update-{{nonce}}'
            $backup = $target + '.previous-{{nonce}}'
            $replaced = $false
            function Assert-PlainPath([string] $path) {
                $path = [IO.Path]::GetFullPath($path)
                while ($path) {
                    if ([IO.File]::Exists($path) -or [IO.Directory]::Exists($path)) {
                        if (([IO.File]::GetAttributes($path) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Linked path rejected.' }
                    }
                    $path = [IO.Path]::GetDirectoryName($path)
                }
            }
            try {
                $old = Get-Process -Id {{processId}} -ErrorAction SilentlyContinue
                if ($old -and -not $old.WaitForExit(120000)) { throw 'The app did not finish closing.' }
                Assert-PlainPath $target
                Assert-PlainPath $temporary
                Assert-PlainPath $backup
                if ({{(update ? "$true" : "$false")}}) {
                    Assert-PlainPath $source
                    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Release checksum changed.' }
                    [IO.File]::Copy($source, $temporary, $false)
                    if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Copied release checksum changed.' }
                    Assert-PlainPath $target
                    [IO.File]::Replace($temporary, $target, $backup)
                    $replaced = $true
                    $new = Start-Process -FilePath $target -WindowStyle Hidden -PassThru
                    Start-Sleep -Seconds 2
                    if ($new.HasExited -and $new.ExitCode -ne 0) { throw 'Updated app exited immediately.' }
                    Assert-PlainPath $backup
                    Remove-Item -LiteralPath $backup -Force
                } else {
                    Remove-Item -LiteralPath $target -Force
                }
            } catch {
                $message = $_.Exception.Message
                if ($replaced -and [IO.File]::Exists($backup)) {
                    try {
                        Assert-PlainPath $backup
                        Assert-PlainPath $target
                        [IO.File]::Copy($backup, $target, $true)
                        Start-Process -FilePath $target -WindowStyle Hidden
                    } catch { $message += ' Recovery copy remains at: ' + $backup }
                }
                Assert-PlainPath $log
                Add-Content -LiteralPath $log -Value $message
            } finally {
                if ([IO.File]::Exists($temporary)) {
                    Assert-PlainPath $temporary
                    Remove-Item -LiteralPath $temporary -Force
                }
            }
            """;
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPart();
    [GeneratedRegex(@"^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256();
}

internal sealed record SelfUpdateDownload(string Path, string Sha256);
