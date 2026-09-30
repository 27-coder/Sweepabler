using System.Reflection;
using System.Runtime.InteropServices;
using ProperAppUpdater.ViewModels;

namespace ProperAppUpdater.Services;

/// <summary>
/// Removes desktop shortcuts that a third-party installer creates while Süpürücü is
/// updating an app. Süpürücü never creates shortcuts itself; this only cleans up what
/// an installer dropped, and only when the shortcut is (a) NEW since the pre-install
/// snapshot and (b) points at the app that was just updated. Pre-existing shortcuts and
/// shortcuts to anything else are never touched.
/// </summary>
public static class DesktopShortcutCleanupService
{
    /// <summary>Snapshot of the .lnk files currently on the user and common desktops.</summary>
    public static IReadOnlySet<string> Snapshot()
    {
        var snapshot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in DesktopRoots())
        {
            try
            {
                foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", SearchOption.TopDirectoryOnly))
                {
                    snapshot.Add(lnk);
                }
            }
            catch
            {
                // Unreadable desktop root — skip it.
            }
        }

        return snapshot;
    }

    /// <summary>
    /// Deletes desktop .lnk files created since <paramref name="before"/> that point at
    /// the just-updated app. Returns how many were removed. Best-effort: never throws.
    /// </summary>
    public static int RemoveNewShortcutsFor(UpdateCandidateViewModel candidate, IReadOnlySet<string> before)
    {
        var installLocation = NormalizeDirectory(candidate.InstallLocation);
        var iconPath = NormalizeFile(AppPathResolver.ExtractExecutablePath(candidate.DisplayIcon));
        var uninstallPath = NormalizeFile(AppPathResolver.ExtractExecutablePath(candidate.UninstallString));
        var appName = AppMatcher.Normalize(candidate.Name);

        var removed = 0;
        foreach (var root in DesktopRoots())
        {
            List<string> current;
            try
            {
                current = Directory.EnumerateFiles(root, "*.lnk", SearchOption.TopDirectoryOnly).ToList();
            }
            catch
            {
                continue;
            }

            foreach (var lnk in current)
            {
                if (before.Contains(lnk))
                {
                    continue; // Pre-existing shortcut — never touch.
                }

                if (!PointsToApp(lnk, ResolveShortcutTarget(lnk), installLocation, iconPath, uninstallPath, appName))
                {
                    continue;
                }

                try
                {
                    File.Delete(lnk);
                    removed++;
                }
                catch
                {
                    // Best effort — a locked or vanished shortcut is fine to skip.
                }
            }
        }

        return removed;
    }

    private static bool PointsToApp(
        string lnkPath,
        string target,
        string installLocation,
        string iconPath,
        string uninstallPath,
        string appName)
    {
        if (!string.IsNullOrWhiteSpace(target))
        {
            string normalizedTarget;
            try
            {
                normalizedTarget = Path.GetFullPath(target);
            }
            catch
            {
                normalizedTarget = target;
            }

            if (!string.IsNullOrWhiteSpace(installLocation) &&
                normalizedTarget.StartsWith(installLocation, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(iconPath) &&
                string.Equals(normalizedTarget, iconPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(uninstallPath) &&
                string.Equals(normalizedTarget, uninstallPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var normalizedTargetPath = AppMatcher.Normalize(normalizedTarget);
            if (appName.Length >= 4 && normalizedTargetPath.Contains(appName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Fallback for installers whose target could not be resolved: match the new
        // shortcut's own file name against the app. Safe because the shortcut is known
        // to be new (created during this app's install).
        var lnkName = AppMatcher.Normalize(Path.GetFileNameWithoutExtension(lnkPath));
        return appName.Length >= 4 && lnkName.Contains(appName, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveShortcutTarget(string lnkPath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return string.Empty;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return string.Empty;
            }

            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { lnkPath });
            if (shortcut is null)
            {
                return string.Empty;
            }

            var target = shortcut.GetType().InvokeMember(
                "TargetPath",
                BindingFlags.GetProperty,
                null,
                shortcut,
                null) as string;
            return target ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            if (shortcut is not null)
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null)
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static IEnumerable<string> DesktopRoots()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        return roots
            .Where(root => !string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            .Distinct(StringComparer.OrdinalIgnoreCase);
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
