using System.Text.RegularExpressions;

namespace ProperAppUpdater.Services;

public static partial class VersionComparer
{
    public static int Compare(string? installedVersion, string? latestVersion)
    {
        var left = ExtractParts(installedVersion);
        var right = ExtractParts(latestVersion);

        if (left.Count == 0 || right.Count == 0)
        {
            return string.Compare(
                installedVersion ?? string.Empty,
                latestVersion ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        var max = Math.Max(left.Count, right.Count);
        for (var index = 0; index < max; index++)
        {
            var leftPart = index < left.Count ? left[index] : 0;
            var rightPart = index < right.Count ? right[index] : 0;

            if (leftPart != rightPart)
            {
                return leftPart.CompareTo(rightPart);
            }
        }

        var leftSuffix = ExtractSuffix(CleanVersion(installedVersion));
        var rightSuffix = ExtractSuffix(CleanVersion(latestVersion));

        // A version with no alphabetic suffix is a final/stable release and ranks
        // ABOVE any pre-release suffix (alpha/beta/rc) that shares the same numbers.
        // Without this, "1.0.0" would sort below "1.0.0beta" and the updater would
        // offer a pre-release as an "upgrade" over the stable build.
        var leftIsStable = leftSuffix.Length == 0;
        var rightIsStable = rightSuffix.Length == 0;
        if (leftIsStable != rightIsStable)
        {
            return leftIsStable ? 1 : -1;
        }

        return string.Compare(leftSuffix, rightSuffix, StringComparison.OrdinalIgnoreCase);
    }

    public static string CleanVersion(string? version, string? versionRegex = null)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        var trimmed = version.Trim();
        if (!string.IsNullOrWhiteSpace(versionRegex))
        {
            var configuredMatch = Regex.Match(
                trimmed,
                versionRegex,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (configuredMatch.Success)
            {
                trimmed = configuredMatch.Groups["version"].Success
                    ? configuredMatch.Groups["version"].Value
                    : configuredMatch.Value;
            }
        }

        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        // Git for Windows tags releases as, for example,
        // "v2.55.0.windows.5" while its registry/installer version is
        // "2.55.0.5". Preserve that packaging revision; truncating the tag to
        // 2.55.0 can hide a real update from 2.55.0.2 to 2.55.0.5.
        trimmed = Regex.Replace(
            trimmed,
            @"(?<=\d)\.windows\.(?=\d)",
            ".",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var match = VersionPattern().Match(trimmed);
        return match.Success ? match.Value : trimmed;
    }

    private static List<int> ExtractParts(string? version)
    {
        var cleaned = CleanVersion(version);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return new List<int>();
        }

        return NumberPattern()
            .Matches(cleaned)
            .Select(match => int.TryParse(match.Value, out var part) ? part : 0)
            .ToList();
    }

    private static string ExtractSuffix(string version)
    {
        var match = SuffixPattern().Match(version);
        return match.Success ? match.Groups["suffix"].Value : string.Empty;
    }

    [GeneratedRegex(@"\d+(?:\.\d+){0,5}[a-zA-Z]*")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"(?<suffix>[a-zA-Z]+)$")]
    private static partial Regex SuffixPattern();
}
