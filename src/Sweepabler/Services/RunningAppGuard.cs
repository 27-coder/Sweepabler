using System.Diagnostics;
using ProperAppUpdater.ViewModels;

namespace ProperAppUpdater.Services;

public static class RunningAppGuard
{
    public static string FindBlockingProcess(UpdateCandidateViewModel candidate)
    {
        var processes = FindBlockingProcesses(candidate);
        try
        {
            return processes.Count > 0 ? processes[0].ProcessName : string.Empty;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>
    /// Returns the live processes that belong to this candidate's installed app and
    /// would block its installer. The caller owns the returned processes and must
    /// dispose them (TryForceCloseAsync does this for you). Süpürücü's own process is
    /// never included.
    /// </summary>
    public static IReadOnlyList<Process> FindBlockingProcesses(UpdateCandidateViewModel candidate)
    {
        var installLocation = NormalizeDirectory(candidate.InstallLocation);
        var iconPath = NormalizeFile(AppPathResolver.ExtractExecutablePath(candidate.DisplayIcon));
        var uninstallPath = NormalizeFile(AppPathResolver.ExtractExecutablePath(candidate.UninstallString));
        var candidateName = AppMatcher.Normalize(candidate.Name);
        var currentProcessId = Environment.ProcessId;

        var matches = new List<Process>();
        foreach (var process in Process.GetProcesses())
        {
            var keep = false;
            try
            {
                keep = process.Id != currentProcessId &&
                       IsBlockingMatch(process, installLocation, iconPath, uninstallPath, candidateName);
            }
            catch
            {
                keep = false;
            }

            if (keep)
            {
                matches.Add(process);
            }
            else
            {
                process.Dispose();
            }
        }

        return matches;
    }

    /// <summary>
    /// Force-terminates the given processes (and their child trees) and waits briefly
    /// for them to exit. Returns true if all of them are gone. Disposes every process.
    /// </summary>
    public static async Task<bool> TryForceCloseAsync(IReadOnlyList<Process> processes, CancellationToken cancellationToken)
    {
        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Already exited, or access denied — re-checked below via HasExited.
            }
        }

        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!processes.All(HasExitedSafe) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(200, cancellationToken);
            }

            return processes.All(HasExitedSafe);
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static bool IsBlockingMatch(
        Process process,
        string installLocation,
        string iconPath,
        string uninstallPath,
        string candidateName)
    {
        string? processPath = null;
        try
        {
            processPath = process.MainModule?.FileName;
        }
        catch
        {
            // Some system processes deny MainModule; those cannot be this app's installer target.
        }

        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var normalizedProcessPath = Path.GetFullPath(processPath);
            if (!string.IsNullOrWhiteSpace(installLocation) &&
                normalizedProcessPath.StartsWith(installLocation, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(iconPath) &&
                string.Equals(normalizedProcessPath, iconPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(uninstallPath) &&
                string.Equals(normalizedProcessPath, uninstallPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var processName = AppMatcher.Normalize(process.ProcessName);
        return candidateName.Length >= 6 &&
               (processName.Equals(candidateName, StringComparison.OrdinalIgnoreCase) ||
                candidateName.StartsWith(processName, StringComparison.OrdinalIgnoreCase) && processName.Length >= 6);
    }

    private static bool HasExitedSafe(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static string NormalizeDirectory(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Directory.Exists(value))
        {
            return string.Empty;
        }

        var fullPath = Path.GetFullPath(value);
        return fullPath.EndsWith(Path.DirectorySeparatorChar)
            ? fullPath
            : fullPath + Path.DirectorySeparatorChar;
    }

    private static string NormalizeFile(string value)
    {
        return string.IsNullOrWhiteSpace(value) || !File.Exists(value)
            ? string.Empty
            : Path.GetFullPath(value);
    }
}
