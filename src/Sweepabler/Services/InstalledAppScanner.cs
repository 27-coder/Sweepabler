using Microsoft.Win32;
using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

public sealed class InstalledAppScanner
{
    private const string UninstallSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string WowUninstallSubKey = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public IReadOnlyList<InstalledApp> Scan()
    {
        var apps = new List<InstalledApp>();
        apps.AddRange(ReadRegistryHive(RegistryHive.LocalMachine, RegistryView.Registry64, UninstallSubKey));
        apps.AddRange(ReadRegistryHive(RegistryHive.LocalMachine, RegistryView.Registry32, UninstallSubKey));
        apps.AddRange(ReadRegistryHive(RegistryHive.LocalMachine, RegistryView.Registry64, WowUninstallSubKey));
        apps.AddRange(ReadRegistryHive(RegistryHive.CurrentUser, RegistryView.Registry64, UninstallSubKey));
        apps.AddRange(ReadRegistryHive(RegistryHive.CurrentUser, RegistryView.Registry32, UninstallSubKey));

        return apps
            .GroupBy(app => $"{AppMatcher.Normalize(app.DisplayName)}|{app.DisplayVersion}|{app.Publisher}")
            .Select(group => group.First())
            .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<InstalledApp> ReadRegistryHive(RegistryHive hive, RegistryView view, string subKeyPath)
    {
        using var baseKey = OpenBaseKey(hive, view);
        if (baseKey is null)
        {
            yield break;
        }

        using var uninstallKey = baseKey.OpenSubKey(subKeyPath);
        if (uninstallKey is null)
        {
            yield break;
        }

        foreach (var subKeyName in uninstallKey.GetSubKeyNames())
        {
            using var appKey = uninstallKey.OpenSubKey(subKeyName);
            if (appKey is null)
            {
                continue;
            }

            var displayName = GetString(appKey, "DisplayName");
            if (string.IsNullOrWhiteSpace(displayName) || IsHiddenSystemComponent(appKey))
            {
                continue;
            }

            var releaseType = GetString(appKey, "ReleaseType");
            if (releaseType.Equals("Hotfix", StringComparison.OrdinalIgnoreCase) ||
                releaseType.Equals("Security Update", StringComparison.OrdinalIgnoreCase) ||
                releaseType.Equals("Update Rollup", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new InstalledApp
            {
                DisplayName = displayName,
                DisplayVersion = GetString(appKey, "DisplayVersion"),
                Publisher = GetString(appKey, "Publisher"),
                InstallLocation = GetString(appKey, "InstallLocation"),
                UninstallString = GetString(appKey, "UninstallString"),
                DisplayIcon = GetString(appKey, "DisplayIcon"),
                UrlInfoAbout = GetString(appKey, "URLInfoAbout"),
                UrlUpdateInfo = GetString(appKey, "URLUpdateInfo"),
                HelpLink = GetString(appKey, "HelpLink"),
                RegistryPath = $@"{hive}\{view}\{subKeyPath}\{subKeyName}"
            };
        }
    }

    private static RegistryKey? OpenBaseKey(RegistryHive hive, RegistryView view)
    {
        try
        {
            return RegistryKey.OpenBaseKey(hive, view);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsHiddenSystemComponent(RegistryKey appKey)
    {
        return appKey.GetValue("SystemComponent") is int intValue && intValue == 1;
    }

    private static string GetString(RegistryKey key, string valueName)
    {
        return key.GetValue(valueName)?.ToString()?.Trim() ?? string.Empty;
    }
}
