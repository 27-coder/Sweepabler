using System.Diagnostics;
using System.Text;
using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

public sealed class PackageManagerUpdateScanner
{
    private const int WingetNoApplicableUpdate = unchecked((int)0x8A15002B);

    public async Task<PackageManagerScanResult> ScanWithInventoryAsync(CancellationToken cancellationToken)
    {
        var updates = new List<PackageManagerUpdate>();
        var managedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var probes = await Task.WhenAll(
            TryScanWingetAsync(cancellationToken),
            TryScanChocolateyAsync(cancellationToken),
            TryScanScoopAsync(cancellationToken));

        foreach (var probe in probes)
        {
            updates.AddRange(probe.Updates);
            foreach (var name in probe.ManagedDisplayNames)
            {
                managedNames.Add(name);
            }
        }

        var normalizedUpdates = updates
            .Where(update => !string.IsNullOrWhiteSpace(update.PackageId))
            .GroupBy(update => $"{AppMatcher.Normalize(update.DisplayName)}|{update.Provider}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(update => update.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PackageManagerScanResult(normalizedUpdates, managedNames.OrderBy(name => name).ToList());
    }

    public async Task<IReadOnlyList<PackageManagerUpdate>> ScanAsync(CancellationToken cancellationToken)
    {
        return (await ScanWithInventoryAsync(cancellationToken)).Updates;
    }

    /// <summary>
    /// Re-queries a single package manager to see whether <paramref name="packageId"/>
    /// still has a pending update. Used after an update to confirm success without
    /// trusting the package manager's exit code. On any uncertainty (timeout/error) it
    /// returns true (still pending) so a real failure is never masked as success.
    /// </summary>
    public async Task<bool> HasPendingUpdateAsync(string provider, string packageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return true;
        }

        var timeout = TimeSpan.FromMinutes(2);
        try
        {
            switch (provider.Trim().ToLowerInvariant())
            {
                case "winget":
                {
                    var result = await RunAsync(
                        "winget",
                        "upgrade --accept-source-agreements --disable-interactivity",
                        timeout,
                        cancellationToken);
                    return WingetQueryShowsPending(result, packageId);
                }

                case "choco":
                case "chocolatey":
                {
                    var result = await RunAsync("choco", "outdated -r --limit-output", timeout, cancellationToken);
                    return ChocolateyQueryShowsPending(result, packageId);
                }

                case "scoop":
                {
                    var result = await RunAsync("powershell.exe", BuildPowerShellCommand("scoop status"), timeout, cancellationToken);
                    return ScoopQueryShowsPending(result, packageId);
                }

                default:
                    return true;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return true;
        }
    }

    internal static bool WingetQueryShowsPending(ProcessResult result, string packageId)
    {
        if (result.TimedOut)
        {
            return true;
        }

        // Inspect rows before the exit code. WinGet can return UPDATE_NOT_APPLICABLE
        // for an installer-scope mismatch even while the global table still lists the
        // update; in that case it is not verified as current.
        if (ParseWinget(result.StdOut, onlyUpgradeRows: true)
            .Any(update => update.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return result.ExitCode != 0 && result.ExitCode != WingetNoApplicableUpdate;
    }

    internal static bool ChocolateyQueryShowsPending(ProcessResult result, string packageId)
    {
        if (result.TimedOut)
        {
            return true;
        }

        if (ParseChocolatey(result.StdOut)
            .Any(update => update.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return result.ExitCode is not 0 and not 2;
    }

    internal static bool ScoopQueryShowsPending(ProcessResult result, string packageId)
    {
        if (result.TimedOut)
        {
            return true;
        }

        if (ParseScoop(result.StdOut)
            .Any(update => update.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return result.ExitCode != 0;
    }

    private static async Task<PackageManagerProbeResult> TryScanWingetAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!CommandExists("winget"))
            {
                return PackageManagerProbeResult.Empty;
            }

            var upgradeTask = RunAsync(
                "winget",
                "upgrade --accept-source-agreements --disable-interactivity",
                TimeSpan.FromMinutes(3),
                cancellationToken);

            var listTask = RunAsync(
                "winget",
                "list --accept-source-agreements --disable-interactivity",
                TimeSpan.FromMinutes(3),
                cancellationToken);

            await Task.WhenAll(upgradeTask, listTask);
            var upgradeResult = await upgradeTask;
            var listResult = await listTask;

            var updates = !upgradeResult.TimedOut && (upgradeResult.ExitCode == 0 || !string.IsNullOrWhiteSpace(upgradeResult.StdOut))
                ? ParseWinget(upgradeResult.StdOut, onlyUpgradeRows: true).ToList()
                : new List<PackageManagerUpdate>();

            var managedNames = !listResult.TimedOut && (listResult.ExitCode == 0 || !string.IsNullOrWhiteSpace(listResult.StdOut))
                ? ParseWingetManagedNames(listResult.StdOut)
                : Array.Empty<string>();

            return new PackageManagerProbeResult(updates, managedNames);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return PackageManagerProbeResult.Empty;
        }
    }

    private static async Task<PackageManagerProbeResult> TryScanChocolateyAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!CommandExists("choco"))
            {
                return PackageManagerProbeResult.Empty;
            }

            var outdatedTask = RunAsync(
                "choco",
                "outdated -r --limit-output",
                TimeSpan.FromMinutes(3),
                cancellationToken);

            var listTask = RunAsync(
                "choco",
                "list --limit-output -r",
                TimeSpan.FromMinutes(3),
                cancellationToken);

            await Task.WhenAll(outdatedTask, listTask);
            var outdatedResult = await outdatedTask;
            var listResult = await listTask;

            var updates = !outdatedResult.TimedOut && (outdatedResult.ExitCode is 0 or 2 || !string.IsNullOrWhiteSpace(outdatedResult.StdOut))
                ? ParseChocolatey(outdatedResult.StdOut).ToList()
                : new List<PackageManagerUpdate>();

            var managedNames = !listResult.TimedOut && (listResult.ExitCode == 0 || !string.IsNullOrWhiteSpace(listResult.StdOut))
                ? ParseChocolateyManagedNames(listResult.StdOut)
                : Array.Empty<string>();

            return new PackageManagerProbeResult(updates, managedNames);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return PackageManagerProbeResult.Empty;
        }
    }

    private static async Task<PackageManagerProbeResult> TryScanScoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!CommandExists("scoop"))
            {
                return PackageManagerProbeResult.Empty;
            }

            var statusTask = RunAsync(
                "powershell.exe",
                BuildPowerShellCommand("scoop status"),
                TimeSpan.FromMinutes(3),
                cancellationToken);

            var listTask = RunAsync(
                "powershell.exe",
                BuildPowerShellCommand("scoop list"),
                TimeSpan.FromMinutes(3),
                cancellationToken);

            await Task.WhenAll(statusTask, listTask);
            var statusResult = await statusTask;
            var listResult = await listTask;

            var updates = !statusResult.TimedOut && (statusResult.ExitCode == 0 || !string.IsNullOrWhiteSpace(statusResult.StdOut))
                ? ParseScoop(statusResult.StdOut).ToList()
                : new List<PackageManagerUpdate>();

            var managedNames = !listResult.TimedOut && (listResult.ExitCode == 0 || !string.IsNullOrWhiteSpace(listResult.StdOut))
                ? ParseScoopManagedNames(listResult.StdOut)
                : Array.Empty<string>();

            return new PackageManagerProbeResult(updates, managedNames);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return PackageManagerProbeResult.Empty;
        }
    }

    internal static IReadOnlyList<PackageManagerUpdate> ParseWinget(string output, bool onlyUpgradeRows)
    {
        var updates = new List<PackageManagerUpdate>();
        var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        for (var index = 0; index < lines.Length; index++)
        {
            var header = lines[index];
            var idStart = header.IndexOf("Id", StringComparison.Ordinal);
            var versionStart = header.IndexOf("Version", StringComparison.Ordinal);
            var availableStart = header.IndexOf("Available", StringComparison.Ordinal);
            var sourceStart = header.IndexOf("Source", StringComparison.Ordinal);

            if (idStart <= 0 || versionStart <= idStart || availableStart <= versionStart || sourceStart <= availableStart)
            {
                continue;
            }

            for (index += 1; index < lines.Length; index++)
            {
                var line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                {
                    break;
                }

                if (line.TrimStart().StartsWith("-", StringComparison.Ordinal) ||
                    line.Contains("packages have an upgrade available", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (line.Length <= sourceStart)
                {
                    continue;
                }

                var source = Slice(line, sourceStart, line.Length - sourceStart).Trim();
                if (!source.Contains("winget", StringComparison.OrdinalIgnoreCase) &&
                    !source.Contains("msstore", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = Slice(line, 0, idStart).Trim();
                var packageId = Slice(line, idStart, versionStart - idStart).Trim();
                var installed = Slice(line, versionStart, availableStart - versionStart).Trim();
                var available = Slice(line, availableStart, sourceStart - availableStart).Trim();

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(packageId))
                {
                    continue;
                }

                if (onlyUpgradeRows && string.IsNullOrWhiteSpace(available))
                {
                    continue;
                }

                updates.Add(new PackageManagerUpdate
                {
                    Provider = "winget",
                    PackageId = packageId,
                    DisplayName = name,
                    InstalledVersion = installed,
                    AvailableVersion = available,
                    SourceName = "WinGet",
                    TrustSummary = $"manifest:{packageId}",
                    RequiresElevation = IsLikelyMachineWideWingetPackage(packageId, source)
                });
            }
        }

        return updates;
    }

    private static bool IsLikelyMachineWideWingetPackage(string packageId, string source)
    {
        if (source.Contains("msstore", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return packageId.StartsWith("Python.Python.", StringComparison.OrdinalIgnoreCase) ||
               packageId.StartsWith("Python.Launcher", StringComparison.OrdinalIgnoreCase) ||
               packageId.StartsWith("Microsoft.AppInstaller", StringComparison.OrdinalIgnoreCase) ||
               packageId.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ParseWingetManagedNames(string output)
    {
        return ParseWinget(output, onlyUpgradeRows: false)
            .Select(update => update.DisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static IReadOnlyList<PackageManagerUpdate> ParseChocolatey(string output)
    {
        var updates = output
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('|'))
            .Where(parts => parts.Length >= 3)
            .Select(parts => new
            {
                PackageId = parts[0].Trim(),
                InstalledVersion = parts[1].Trim(),
                AvailableVersion = parts[2].Trim()
            })
            // Chocolatey has occasionally emitted stale rows whose installed and
            // available versions are identical. They are not updates and previously
            // produced false "Updated" history records.
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.PackageId) &&
                !string.IsNullOrWhiteSpace(row.InstalledVersion) &&
                !string.IsNullOrWhiteSpace(row.AvailableVersion) &&
                !string.Equals(row.InstalledVersion, row.AvailableVersion, StringComparison.OrdinalIgnoreCase))
            .GroupBy(row => row.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        // Chocolatey's python/python3 packages are aliases for the concrete packages
        // (for example python313). Showing all of them asks the user to update the same
        // runtime multiple times. Prefer the concrete package whenever it is present.
        var hasConcretePython = updates.Any(row => IsConcretePythonPackage(row.PackageId));
        var hasPython3Alias = updates.Any(row => row.PackageId.Equals("python3", StringComparison.OrdinalIgnoreCase));

        return updates
            .Where(row =>
                !(hasConcretePython && IsPythonAlias(row.PackageId)) &&
                !(hasPython3Alias && row.PackageId.Equals("python", StringComparison.OrdinalIgnoreCase)))
            .Select(parts => new PackageManagerUpdate
            {
                Provider = "choco",
                PackageId = parts.PackageId,
                DisplayName = parts.PackageId,
                InstalledVersion = parts.InstalledVersion,
                AvailableVersion = parts.AvailableVersion,
                SourceName = "Chocolatey",
                TrustSummary = $"package:{parts.PackageId}",
                RequiresElevation = true
            })
            .ToList();
    }

    private static bool IsPythonAlias(string packageId)
    {
        return packageId.Equals("python", StringComparison.OrdinalIgnoreCase) ||
               packageId.Equals("python3", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConcretePythonPackage(string packageId)
    {
        const string prefix = "python";
        if (!packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = packageId[prefix.Length..];
        return suffix.Length >= 2 && suffix.All(char.IsDigit);
    }

    private static IReadOnlyList<string> ParseChocolateyManagedNames(string output)
    {
        return output
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('|')[0].Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<PackageManagerUpdate> ParseScoop(string output)
    {
        var updates = new List<PackageManagerUpdate>();

        foreach (var rawLine in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 ||
                line.StartsWith("WARN", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Name", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("-", StringComparison.Ordinal) ||
                // Scoop status banners are full sentences, not app rows. Filtering these
                // is what stops "Scoop is up to date." becoming a phantom "Scoop" update.
                line.Contains("is ok", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("up to date", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("out of date", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("updates are available", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            var installed = parts[1];
            var available = parts[2];

            // A genuine app row has version-like columns (they contain digits) and a real
            // version change. English words like "is"/"up" never qualify.
            if (!installed.Any(char.IsDigit) ||
                !available.Any(char.IsDigit) ||
                string.Equals(installed, available, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            updates.Add(new PackageManagerUpdate
            {
                Provider = "scoop",
                PackageId = parts[0],
                DisplayName = parts[0],
                InstalledVersion = installed,
                AvailableVersion = available,
                SourceName = "Scoop",
                TrustSummary = $"manifest:{parts[0]}",
                RequiresElevation = false
            });
        }

        return updates;
    }

    private static IReadOnlyList<string> ParseScoopManagedNames(string output)
    {
        var names = new List<string>();

        foreach (var rawLine in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 ||
                line.StartsWith("Installed apps", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Name", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                names.Add(parts[0]);
            }
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Slice(string value, int startIndex, int length)
    {
        if (startIndex >= value.Length)
        {
            return string.Empty;
        }

        var safeLength = Math.Min(length, value.Length - startIndex);
        return value.Substring(startIndex, safeLength);
    }

    private static bool CommandExists(string command) => ToolLocator.Find(command) is not null;

    private static string BuildPowerShellCommand(string command)
    {
        return $"-NoProfile -ExecutionPolicy Bypass -Command \"$ErrorActionPreference='Stop'; {(command.StartsWith("scoop ", StringComparison.Ordinal) ? ToolLocator.ScoopCommand(command) : command)}\"";
    }

    private static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ToolLocator.Require(fileName),
                Arguments = arguments,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false
            }
        };

        // Keep stdout and stderr separate: stderr (error banners, warnings) must never
        // be fed to the row parsers, or it fabricates phantom update entries.
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdout.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderr.AppendLine(args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            await ProcessTermination.KillTreeAndWaitAsync(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            timedOut = true;
        }

        return new ProcessResult(process.HasExited ? process.ExitCode : -1, stdout.ToString(), stderr.ToString(), timedOut);
    }

    internal sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);

    private sealed record PackageManagerProbeResult(
        IReadOnlyList<PackageManagerUpdate> Updates,
        IReadOnlyList<string> ManagedDisplayNames)
    {
        public static PackageManagerProbeResult Empty { get; } = new(
            Array.Empty<PackageManagerUpdate>(),
            Array.Empty<string>());
    }
}

public sealed record PackageManagerScanResult(
    IReadOnlyList<PackageManagerUpdate> Updates,
    IReadOnlyList<string> ManagedDisplayNames);
