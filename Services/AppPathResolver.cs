using System.Diagnostics;
using System.Text.RegularExpressions;
using ProperAppUpdater.ViewModels;

namespace ProperAppUpdater.Services;

public static partial class AppPathResolver
{
    public static string ResolveOpenTarget(UpdateCandidateViewModel candidate)
    {
        var installLocation = CleanPath(candidate.InstallLocation);
        if (Directory.Exists(installLocation))
        {
            return installLocation;
        }

        var iconPath = ExtractExecutablePath(candidate.DisplayIcon);
        if (File.Exists(iconPath))
        {
            return iconPath;
        }

        var uninstallPath = ExtractExecutablePath(candidate.UninstallString);
        if (File.Exists(uninstallPath))
        {
            return uninstallPath;
        }

        var packageManagerPath = ResolvePackageManagerTarget(candidate);
        if (!string.IsNullOrWhiteSpace(packageManagerPath))
        {
            return packageManagerPath;
        }

        return string.Empty;
    }

    public static void OpenInExplorer(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidOperationException(LocalizationService.Current.Get("FilePathMissing"));
        }

        var fullPath = Path.GetFullPath(target);
        ProcessStartInfo startInfo;
        if (File.Exists(fullPath))
        {
            startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                Arguments = $"/select,\"{fullPath}\"",
                UseShellExecute = false
            };
        }
        else if (Directory.Exists(fullPath))
        {
            startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                Arguments = $"\"{fullPath}\"",
                UseShellExecute = false
            };
        }
        else
        {
            throw new InvalidOperationException(LocalizationService.Current.Get("FilePathMissing"));
        }

        Process.Start(startInfo);
    }

    public static void OpenSystemUninstaller()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "ms-settings:appsfeatures",
            UseShellExecute = true
        });
    }

    public static string ExtractExecutablePath(string value)
    {
        var cleaned = CleanPath(value);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        if (cleaned.StartsWith('"'))
        {
            var endQuote = cleaned.IndexOf('"', 1);
            if (endQuote > 1)
            {
                return cleaned[1..endQuote];
            }
        }

        var exeMatch = ExecutablePathRegex().Match(cleaned);
        if (exeMatch.Success)
        {
            return exeMatch.Groups["path"].Value.Trim('"');
        }

        var commaIndex = cleaned.IndexOf(',', StringComparison.Ordinal);
        return commaIndex > 0 ? cleaned[..commaIndex].Trim('"', ' ') : cleaned.Trim('"', ' ');
    }

    private static string CleanPath(string value)
    {
        var cleaned = (value ?? string.Empty)
            .Trim()
            .Trim('"')
            .Trim();

        return Environment.ExpandEnvironmentVariables(cleaned);
    }

    private static string ResolvePackageManagerTarget(UpdateCandidateViewModel candidate)
    {
        var packageId = candidate.Catalog.PackageId ?? candidate.Catalog.Id;
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return string.Empty;
        }

        var provider = candidate.Catalog.Provider.Trim().ToLowerInvariant();
        if (provider is "scoop" or "choco" or "chocolatey" or "winget") PackageIdentity.Require(provider, packageId);
        if (provider == "scoop") packageId = packageId.Split('/')[^1];
        return provider switch
        {
            "scoop" => FirstExistingPath(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps", packageId, "current"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps", packageId),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "scoop", "apps", packageId, "current"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "scoop", "apps", packageId)),
            "choco" or "chocolatey" => FirstExistingPath(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey", "lib", packageId, "tools"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey", "lib", packageId)),
            "winget" => FirstExistingPath(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps")),
            _ => string.Empty
        };
    }

    private static string FirstExistingPath(params string[] paths)
    {
        return paths.FirstOrDefault(path => Directory.Exists(path) || File.Exists(path)) ?? string.Empty;
    }

    [GeneratedRegex(@"(?<path>[A-Za-z]:\\.+?\.exe)", RegexOptions.IgnoreCase)]
    private static partial Regex ExecutablePathRegex();
}
