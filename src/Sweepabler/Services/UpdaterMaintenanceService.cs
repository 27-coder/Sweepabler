using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace ProperAppUpdater.Services;

public sealed class UpdaterMaintenanceService
{
    public async Task<UpdaterMaintenanceResult> SweepAsync(
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var checks = new List<UpdaterMaintenanceCheck>();

        var results = await Task.WhenAll(
            SweepWingetAsync(progress, cancellationToken),
            SweepChocolateyAsync(progress, cancellationToken),
            SweepScoopAsync(progress, cancellationToken));
        foreach (var result in results) checks.AddRange(result);

        return new UpdaterMaintenanceResult(checks);
    }

    private static async Task<IReadOnlyList<UpdaterMaintenanceCheck>> SweepWingetAsync(
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var checks = new List<UpdaterMaintenanceCheck>();
        if (!CommandExists("winget"))
        {
            checks.Add(UpdaterMaintenanceCheck.Skipped("WinGet", string.Format(Text.Get("ToolMissing"), "winget")));
            return checks;
        }

        progress.Report(Text.Get("UpdaterWingetSource"));
        checks.Add(await RunToolAsync(
            "WinGet source",
            "winget",
            "source update --disable-interactivity",
            requireElevation: false,
            cancellationToken));

        progress.Report(Text.Get("UpdaterWingetCheck"));
        var check = await RunToolAsync(
            "WinGet self-check",
            "winget",
            "upgrade --accept-source-agreements --disable-interactivity",
            requireElevation: false,
            cancellationToken);
        checks.Add(check);

        if (check.IsSuccess && PackageManagerUpdateScanner.ParseWinget(check.Output, onlyUpgradeRows: true).Any(update => update.PackageId.Equals("Microsoft.AppInstaller", StringComparison.OrdinalIgnoreCase)))
        {
            progress.Report(Text.Get("UpdaterWingetUpdate"));
            checks.Add(await RunToolAsync(
                "WinGet self-update",
                "winget",
                "upgrade --id Microsoft.AppInstaller --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
                requireElevation: false,
                cancellationToken));
        }

        return checks;
    }

    private static async Task<IReadOnlyList<UpdaterMaintenanceCheck>> SweepChocolateyAsync(
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var checks = new List<UpdaterMaintenanceCheck>();
        if (!CommandExists("choco"))
        {
            checks.Add(UpdaterMaintenanceCheck.Skipped("Chocolatey", string.Format(Text.Get("ToolMissing"), "choco")));
            return checks;
        }

        progress.Report(Text.Get("UpdaterChocoCheck"));
        var outdated = await RunToolAsync(
            "Chocolatey self-check",
            "choco",
            "outdated -r --limit-output",
            requireElevation: false,
            cancellationToken);
        checks.Add(outdated);

        var packages = ParseChocolateySelfPackages(outdated.Output);
        if (packages.Count == 0)
        {
            return checks;
        }

        progress.Report(Text.Get("UpdaterChocoUpdate"));
        checks.Add(await RunToolAsync(
            "Chocolatey self-update",
            "choco",
            $"upgrade {string.Join(' ', packages.Select(package => $"\"{package}\""))} -y --skip-if-not-installed --no-progress",
            requireElevation: true,
            cancellationToken));

        return checks;
    }

    private static async Task<IReadOnlyList<UpdaterMaintenanceCheck>> SweepScoopAsync(
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var checks = new List<UpdaterMaintenanceCheck>();
        if (!CommandExists("scoop"))
        {
            checks.Add(UpdaterMaintenanceCheck.Skipped("Scoop", string.Format(Text.Get("ToolMissing"), "scoop")));
            return checks;
        }

        progress.Report(Text.Get("UpdaterScoopUpdate"));
        checks.Add(await RunToolAsync(
            "Scoop self-update",
            "powershell.exe",
            BuildPowerShellCommand("scoop update"),
            requireElevation: false,
            cancellationToken));

        return checks;
    }

    internal static IReadOnlyList<string> ParseChocolateySelfPackages(string output)
    {
        return output
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('|'))
            .Where(parts => parts.Length >= 3 && VersionComparer.Compare(parts[1].Trim(), parts[2].Trim()) < 0)
            .Select(parts => parts[0].Trim())
            .Where(package =>
                package.Equals("chocolatey", StringComparison.OrdinalIgnoreCase) ||
                package.StartsWith("chocolatey-", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static bool CommandExists(string command) => ToolLocator.Find(command) is not null;

    private static async Task<UpdaterMaintenanceCheck> RunToolAsync(
        string name,
        string fileName,
        string arguments,
        bool requireElevation,
        CancellationToken cancellationToken)
    {
        try
        {
            var elevated = requireElevation && !IsRunningAsAdministrator();
            using var process = new Process
            {
                StartInfo = BuildStartInfo(fileName, arguments, elevated)
            };

            var output = new StringBuilder();
            if (!process.StartInfo.UseShellExecute)
            {
                process.OutputDataReceived += (_, args) =>
                {
                    if (args.Data is not null)
                    {
                        lock (output) { output.AppendLine(args.Data); }
                    }
                };
                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data is not null)
                    {
                        lock (output) { output.AppendLine(args.Data); }
                    }
                };
            }

            process.Start();
            if (!process.StartInfo.UseShellExecute)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(4));
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

                return new UpdaterMaintenanceCheck(name, -1, elevated, output.ToString(), "Timed out");
            }

            return new UpdaterMaintenanceCheck(
                name,
                process.ExitCode,
                elevated,
                output.ToString(),
                IsSuccessExitCode(process.ExitCode) ? "OK" : $"Exit {process.ExitCode}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            CrashLogger.Log(exception);
            return new UpdaterMaintenanceCheck(name, null, requireElevation, string.Empty, exception.Message);
        }
    }

    private static ProcessStartInfo BuildStartInfo(string fileName, string arguments, bool elevated)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ToolLocator.Require(fileName),
            Arguments = arguments,
            CreateNoWindow = !elevated,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = elevated,
            WorkingDirectory = AppPaths.DataRoot
        };

        if (elevated)
        {
            startInfo.Verb = "runas";
            return startInfo;
        }

        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;
        return startInfo;
    }

    private static string BuildPowerShellCommand(string command)
    {
        return $"-NoProfile -ExecutionPolicy Bypass -Command \"$ErrorActionPreference='Stop'; {(command.StartsWith("scoop ", StringComparison.Ordinal) ? ToolLocator.ScoopCommand(command) : command)}\"";
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool IsSuccessExitCode(int exitCode)
    {
        return exitCode is 0 or 2 or 3010 or 1641 || exitCode == unchecked((int)0x8A15002B);
    }

    private static LocalizationService Text => LocalizationService.Current;
}

public sealed record UpdaterMaintenanceResult(IReadOnlyList<UpdaterMaintenanceCheck> Checks)
{
    public string Summary
    {
        get
        {
            var failed = Checks.Count(check => !check.WasSkipped && !check.IsSuccess);
            var skipped = Checks.Count(check => check.WasSkipped);

            if (failed == 0 && skipped == 0)
            {
                return LocalizationService.Current.Get("UpdaterSummaryClean");
            }

            if (failed == 0)
            {
                return string.Format(LocalizationService.Current.Get("UpdaterSummarySkipped"), skipped);
            }

            return string.Format(LocalizationService.Current.Get("UpdaterSummaryErrors"), failed, skipped);
        }
    }
}

public sealed record UpdaterMaintenanceCheck(
    string Name,
    int? ExitCode,
    bool UsedElevation,
    string Output,
    string Outcome,
    bool WasSkipped = false)
{
    public bool IsSuccess => ExitCode is 0 or 2 or 3010 or 1641 || ExitCode == unchecked((int)0x8A15002B);

    public static UpdaterMaintenanceCheck Skipped(string name, string outcome)
    {
        return new UpdaterMaintenanceCheck(name, null, false, string.Empty, outcome, WasSkipped: true);
    }
}
