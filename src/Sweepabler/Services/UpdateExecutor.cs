using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Principal;
using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

public sealed class UpdateExecutor
{
    private static readonly TimeSpan InstallerTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan DefaultDownloadTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ProcessHeartbeatInterval = TimeSpan.FromSeconds(30);
    private const long MaxInstallerBytes = 2L * 1024 * 1024 * 1024; // 2 GB safety ceiling

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _downloadTimeout;
    private readonly string _downloadsRoot;

    public UpdateExecutor(
        HttpClient httpClient,
        TimeSpan? downloadTimeout = null,
        string? downloadsRoot = null)
    {
        _httpClient = httpClient;
        _downloadTimeout = downloadTimeout ?? DefaultDownloadTimeout;
        _downloadsRoot = downloadsRoot ?? AppPaths.DownloadsRoot;
    }

    public async Task<UpdateRecord> InstallAsync(
        CatalogEntry catalog,
        string appName,
        string installedVersion,
        string latestVersion,
        string downloadUrl,
        string expectedSha512,
        string expectedSha256,
        string sourceName,
        string trustSummary,
        IProgress<string> progress,
        CancellationToken cancellationToken,
        UpdateRecord? preparedRecord = null)
    {
        var record = preparedRecord ?? new UpdateRecord
        {
            AppName = appName,
            FromVersion = installedVersion,
            ToVersion = latestVersion,
            DownloadUrl = downloadUrl,
            SourceName = sourceName,
            TrustSummary = trustSummary
        };

        try
        {
            if (IsPackageManagerProvider(catalog.Provider))
            {
                return await InstallPackageManagerUpdateAsync(catalog, record, progress, cancellationToken);
            }

            if (preparedRecord is null)
            {
                record = await PrepareAsync(catalog, record, expectedSha512, expectedSha256, progress, cancellationToken);
            }
            if (record.Error.Length > 0) return record;

            var installerPath = SafePath.RequireInside(_downloadsRoot, record.InstallerPath);
            if (preparedRecord is not null)
            {
                // A prepared file may have waited while another app installed.
                var verifiedBy = await VerifyHashesAsync(installerPath, expectedSha512, expectedSha256, progress, cancellationToken);
                var signature = await Task.Run(() => SignatureInspector.Verify(installerPath), cancellationToken);
                record.Signer = signature.SignerName;
                ApplyTrustDecision(record, catalog, signature, verifiedBy);
            }
            progress.Report(Text.Get("Installing"));
            SafePath.RequireInside(_downloadsRoot, installerPath);
            var exitCode = await RunInstallerAsync(catalog, installerPath, progress, cancellationToken);
            record.ExitCode = exitCode;

            record.Outcome = IsSuccessfulInstallerExit(exitCode)
                ? exitCode == 3010 ? "Updated; restart required" : "Updated"
                : $"Installer exited with {exitCode}";
            return record;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            record.Outcome = "Failed";
            record.Error = exception.Message;
            return record;
        }
    }

    public async Task<long?> GetDownloadSizeAsync(CatalogEntry catalog, string url, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            SourceTrustPolicy.ValidateDownloadUrl(catalog, url);
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            SourceTrustPolicy.ValidateRedirectTarget(catalog, url, response.RequestMessage?.RequestUri?.ToString() ?? url);
            return response.IsSuccessStatusCode && response.Content.Headers.ContentLength is > 0 ? response.Content.Headers.ContentLength : null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return null; }
    }

    public async Task<UpdateRecord> PrepareAsync(
        CatalogEntry catalog, UpdateRecord record, string expectedSha512, string expectedSha256,
        IProgress<string> progress, CancellationToken cancellationToken,
        DownloadBandwidthCoordinator? bandwidth = null, string? downloadId = null)
    {
        try
        {
            progress.Report(Text.Get("Downloading"));
            record.InstallerPath = await DownloadInstallerAsync(catalog, record.ToVersion, record.DownloadUrl,
                progress, cancellationToken, bandwidth, downloadId, expectedSha512, expectedSha256);
            if (downloadId is not null) bandwidth?.Complete(downloadId);
            SafePath.RequireInside(_downloadsRoot, record.InstallerPath);
            var verifiedBy = await VerifyHashesAsync(record.InstallerPath, expectedSha512, expectedSha256, progress, cancellationToken);
            var signature = await Task.Run(() => SignatureInspector.Verify(record.InstallerPath), cancellationToken);
            record.Signer = signature.SignerName;
            ApplyTrustDecision(record, catalog, signature, verifiedBy);
            progress.Report(Text.Get("DownloadReady"));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { record.Outcome = "Failed"; record.Error = exception.Message; }
        finally { if (downloadId is not null) bandwidth?.Complete(downloadId); }
        return record;
    }

    private async Task<string> DownloadInstallerAsync(
        CatalogEntry catalog,
        string latestVersion,
        string downloadUrl,
        IProgress<string> progress,
        CancellationToken cancellationToken,
        DownloadBandwidthCoordinator? bandwidth,
        string? downloadId,
        string expectedSha512,
        string expectedSha256)
    {
        var safeVersion = SanitizeFileName(string.IsNullOrWhiteSpace(latestVersion) ? "latest" : latestVersion);
        if (string.IsNullOrWhiteSpace(safeVersion))
        {
            safeVersion = "latest";
        }

        var fileName = SanitizeFileName(Path.GetFileName(new Uri(downloadUrl).AbsolutePath));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = SanitizeFileName($"{catalog.Id}-{safeVersion}.installer");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "download.installer";
        }

        var targetDirectory = BuildSafeDownloadDirectory(_downloadsRoot, catalog.Id, safeVersion);
        SafePath.RequireInside(_downloadsRoot, targetDirectory);
        Directory.CreateDirectory(targetDirectory);
        var targetPath = SafePath.RequireInside(_downloadsRoot, Path.Combine(targetDirectory, fileName));
        var partialPath = targetPath + $".{Guid.NewGuid():N}.partial";

        SourceTrustPolicy.ValidateDownloadUrl(catalog, downloadUrl);
        if (File.Exists(targetPath) && (!string.IsNullOrWhiteSpace(expectedSha512) || !string.IsNullOrWhiteSpace(expectedSha256)))
        {
            try
            {
                await VerifyHashesAsync(targetPath, expectedSha512, expectedSha256, progress, cancellationToken);
                progress.Report(Text.Get("UsingVerifiedDownload"));
                return targetPath;
            }
            catch (InvalidOperationException) { /* Changed cache bytes require a fresh official download. */ }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_downloadTimeout);
        var downloadToken = timeoutCts.Token;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            request.Headers.UserAgent.ParseAdd("Supurucu/1.0");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                downloadToken);
            response.EnsureSuccessStatusCode();

            var effectiveUrl = response.RequestMessage?.RequestUri?.ToString() ?? downloadUrl;
            SourceTrustPolicy.ValidateRedirectTarget(catalog, downloadUrl, effectiveUrl);

            var totalBytes = response.Content.Headers.ContentLength;
            if (downloadId is not null) bandwidth?.SetSize(downloadId, totalBytes);
            if (totalBytes is > MaxInstallerBytes)
            {
                throw new InvalidOperationException(Text.Get("InstallerTooLarge"));
            }

            await using var source = await response.Content.ReadAsStreamAsync(downloadToken);
            SafePath.RequireInside(_downloadsRoot, partialPath);
            await using (var destination = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[1024 * 128];
                long downloadedBytes = 0;
                long reportedPercent = -1;
                var progressTimer = Stopwatch.StartNew();
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, downloadToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), downloadToken);
                    downloadedBytes += bytesRead;
                    if (downloadedBytes > MaxInstallerBytes)
                    {
                        throw new InvalidOperationException(Text.Get("InstallerTooLarge"));
                    }

                    if (downloadId is not null && bandwidth is not null)
                        await bandwidth.PaceAsync(downloadId, bytesRead, downloadToken);

                    if (totalBytes is > 0)
                    {
                        var percent = Math.Clamp((downloadedBytes * 100) / totalBytes.Value, 0, 100);
                        if (percent != reportedPercent && (progressTimer.ElapsedMilliseconds >= 100 || percent == 100))
                        {
                            var key = downloadId is not null && bandwidth?.ShouldLimit(downloadId) == true
                                ? "DownloadingLimitedPercent" : "DownloadingPercent";
                            progress.Report(string.Format(Text.Get(key), percent));
                            reportedPercent = percent;
                            progressTimer.Restart();
                        }
                    }
                }

                if (totalBytes is > 0 && downloadedBytes != totalBytes)
                    throw new IOException(Text.Get("DownloadIncomplete"));
                await destination.FlushAsync(downloadToken);
            }

            SafePath.RequireInside(_downloadsRoot, targetPath);
            File.Move(partialPath, targetPath, overwrite: true);
            return targetPath;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(Text.Get("DownloadTimedOut"));
        }
        finally
        {
            try
            {
                SafePath.RequireInside(_downloadsRoot, partialPath);
                File.Delete(partialPath);
            }
            catch
            {
                // A locked partial file will be retried/cleaned on a later run.
            }
        }
    }

    /// <summary>
    /// Decides whether the installer is trusted and, if not, records a non-blocking
    /// warning. A matching feed checksum establishes byte integrity. When a catalog
    /// publisher pin exists, Authenticode identity is checked independently so a hash
    /// cannot silently bypass a changed or missing publisher signature. Warnings remain
    /// non-blocking; an actual checksum mismatch is blocked earlier.
    /// </summary>
    internal static void ApplyTrustDecision(UpdateRecord record, CatalogEntry catalog, SignatureResult signature, string verifiedBy)
    {
        var pin = catalog.SignaturePublisherContains;
        var pinned = !string.IsNullOrWhiteSpace(pin);
        var pinMatched = pinned && signature.IsTrusted && !string.IsNullOrWhiteSpace(signature.SignerName) &&
                         signature.SignerName.Contains(pin!, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(verifiedBy))
        {
            record.VerifiedBy = verifiedBy;

            if (!pinned || pinMatched)
            {
                record.TrustWarning = string.Empty;
            }
            else if (!signature.HasSignature)
            {
                record.TrustWarning = Text.Get("TrustUnsigned");
            }
            else if (!signature.IsTrusted)
            {
                record.TrustWarning = Text.Get("TrustInvalidSig");
            }
            else
            {
                record.TrustWarning = string.Format(Text.Get("TrustPublisherMismatch"), signature.SignerName, pin);
            }

            return;
        }

        if (signature.IsTrusted && (!pinned || pinMatched))
        {
            record.VerifiedBy = "signature";
            record.TrustWarning = string.Empty;
            return;
        }

        record.VerifiedBy = string.Empty;
        if (!signature.HasSignature)
        {
            record.TrustWarning = Text.Get("TrustUnsigned");
        }
        else if (!signature.IsTrusted)
        {
            record.TrustWarning = Text.Get("TrustInvalidSig");
        }
        else
        {
            // Validly signed, but by a different publisher than the catalog pinned.
            record.TrustWarning = string.Format(Text.Get("TrustPublisherMismatch"), signature.SignerName, pin);
        }
    }

    private static async Task<string> VerifyHashesAsync(
        string installerPath,
        string expectedSha512,
        string expectedSha256,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var verifiedBy = string.Empty;

        if (!string.IsNullOrWhiteSpace(expectedSha512))
        {
            progress.Report(Text.Get("Verifying"));
            await VerifyHashAsync(installerPath, expectedSha512, "SHA512", cancellationToken);
            verifiedBy = "sha512";
        }

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            progress.Report(Text.Get("Verifying"));
            await VerifyHashAsync(installerPath, expectedSha256, "SHA256", cancellationToken);
            if (verifiedBy.Length == 0)
            {
                verifiedBy = "sha256";
            }
        }

        return verifiedBy;
    }

    private static async Task VerifyHashAsync(string installerPath, string expected, string algorithm, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(installerPath);
        var hash = string.Equals(algorithm, "SHA512", StringComparison.Ordinal)
            ? await SHA512.HashDataAsync(stream, cancellationToken)
            : await SHA256.HashDataAsync(stream, cancellationToken);

        // Feeds publish checksums as either lowercase/uppercase hex or base64; accept any.
        var actualHex = Convert.ToHexString(hash);
        var actualBase64 = Convert.ToBase64String(hash);
        var normalized = NormalizeChecksum(expected);

        if (!string.Equals(normalized, actualHex, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalized, actualBase64, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(string.Format(Text.Get("ChecksumMismatch"), algorithm));
        }
    }

    private static string NormalizeChecksum(string expected)
    {
        var value = expected.Trim();
        foreach (var prefix in new[] { "sha512:", "sha256:" })
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[prefix.Length..].Trim();
            }
        }

        return new string(value.Where(character => !char.IsWhiteSpace(character)).ToArray());
    }

    private static async Task<int> RunInstallerAsync(
        CatalogEntry catalog,
        string installerPath,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = BuildStartInfo(catalog, installerPath);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows did not start the installer process.");

        await WaitForProcessExitAsync(process, InstallerTimeout, progress, Text.Get("StillInstalling"), cancellationToken);
        return process.ExitCode;
    }

    private async Task<UpdateRecord> InstallPackageManagerUpdateAsync(
        CatalogEntry catalog,
        UpdateRecord record,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var packageId = catalog.PackageId ?? catalog.Id;
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new InvalidOperationException($"{catalog.Name} has no package id.");
        }

        progress.Report(Text.Get("PackageManager"));
        var startInfo = BuildPackageManagerStartInfo(catalog, packageId);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows did not start the package manager process.");

        await WaitForProcessExitAsync(process, InstallerTimeout, progress, Text.Get("StillInstalling"), cancellationToken);

        record.ExitCode = process.ExitCode;
        record.Outcome = IsSuccessfulInstallerExit(process.ExitCode)
            ? process.ExitCode is 3010 or 1641 ? "Updated; restart required" : "Updated"
            : $"Package manager exited with {process.ExitCode}";

        return record;
    }

    private static ProcessStartInfo BuildPackageManagerStartInfo(CatalogEntry catalog, string packageId)
    {
        var provider = catalog.Provider.Trim().ToLowerInvariant();
        PackageIdentity.Require(provider, packageId);
        ProcessStartInfo startInfo = provider switch
        {
            "winget" => new ProcessStartInfo
            {
                FileName = ToolLocator.Require("winget"),
                Arguments = $"upgrade --id \"{packageId}\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity"
            },
            "choco" or "chocolatey" => new ProcessStartInfo
            {
                FileName = ToolLocator.Require("choco"),
                Arguments = $"upgrade \"{packageId}\" -y --skip-if-not-installed --no-progress"
            },
            "scoop" => new ProcessStartInfo
            {
                FileName = ToolLocator.PowerShellPath,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"$ErrorActionPreference='Stop'; {ToolLocator.ScoopCommand($"scoop update '{EscapePowerShellSingleQuoted(packageId)}'")}\""
            },
            _ => throw new NotSupportedException($"Package manager provider '{catalog.Provider}' is not supported.")
        };

        var needsElevation = catalog.RequiresElevation && !IsRunningAsAdministrator();
        startInfo.UseShellExecute = needsElevation;
        startInfo.WorkingDirectory = AppPaths.DataRoot;
        startInfo.CreateNoWindow = !needsElevation;
        startInfo.WindowStyle = ProcessWindowStyle.Hidden;

        if (needsElevation)
        {
            startInfo.Verb = "runas";
        }

        return startInfo;
    }

    private static async Task WaitForProcessExitAsync(
        Process process,
        TimeSpan timeout,
        IProgress<string> progress,
        string heartbeatMessage,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var nextHeartbeat = startedAt + ProcessHeartbeatInterval;

        try
        {
            while (!process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var now = DateTimeOffset.UtcNow;
                if (now - startedAt > timeout)
                {
                    await ProcessTermination.KillTreeAndWaitAsync(process);
                    throw new TimeoutException(Text.Get("InstallerTimedOut"));
                }

                if (now >= nextHeartbeat)
                {
                    progress.Report(heartbeatMessage);
                    nextHeartbeat = now + ProcessHeartbeatInterval;
                }

                await Task.Delay(500, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            await ProcessTermination.KillTreeAndWaitAsync(process);
            throw;
        }
    }

    private static bool IsPackageManagerProvider(string provider)
    {
        return provider.Equals("winget", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("choco", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("chocolatey", StringComparison.OrdinalIgnoreCase) ||
               provider.Equals("scoop", StringComparison.OrdinalIgnoreCase);
    }

    private static ProcessStartInfo BuildStartInfo(CatalogEntry catalog, string installerPath)
    {
        var installerType = catalog.InstallerType.Trim().ToLowerInvariant();
        ProcessStartInfo startInfo;

        if (installerType == "msi")
        {
            startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe"),
                Arguments = $"/i \"{installerPath}\" {catalog.SilentArgs}".Trim()
            };
        }
        else
        {
            startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = catalog.SilentArgs
            };
        }

        var needsElevation = catalog.RequiresElevation && !IsRunningAsAdministrator();
        startInfo.UseShellExecute = needsElevation;
        startInfo.WorkingDirectory = Path.GetDirectoryName(installerPath) ?? AppPaths.DownloadsRoot;
        startInfo.CreateNoWindow = !needsElevation;
        startInfo.WindowStyle = ProcessWindowStyle.Hidden;

        if (needsElevation)
        {
            startInfo.Verb = "runas";
        }

        return startInfo;
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool IsSuccessfulInstallerExit(int? exitCode)
    {
        return exitCode is 0 or 3010 or 1641;
    }

    private static string SanitizeFileName(string value) => SafePath.FileSegment(value);

    internal static string BuildSafeDownloadDirectory(string downloadsRoot, string catalogId, string version)
    {
        var root = Path.GetFullPath(downloadsRoot).TrimEnd(Path.DirectorySeparatorChar);
        var safeId = SanitizeFileName(catalogId);
        var safeVersion = SanitizeFileName(version);
        if (string.IsNullOrWhiteSpace(safeId))
        {
            safeId = "catalog";
        }

        if (string.IsNullOrWhiteSpace(safeVersion))
        {
            safeVersion = "latest";
        }

        var target = Path.GetFullPath(Path.Combine(root, safeId, safeVersion));
        var rootPrefix = root + Path.DirectorySeparatorChar;
        if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(Text.Get("UnsafeDownloadPath"));
        }

        return target;
    }

    private static string EscapePowerShellSingleQuoted(string value)
    {
        return value.Replace("'", "''");
    }

    private static LocalizationService Text => LocalizationService.Current;
}
