using System.Text;
using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

public static class AppMatcher
{
    public static InstalledApp? FindInstalledMatch(CatalogEntry catalog, IReadOnlyCollection<InstalledApp> installedApps)
    {
        IEnumerable<string> matchNames = catalog.MatchNames.Count == 0
            ? new[] { catalog.Name }
            : catalog.MatchNames;

        foreach (var matchName in matchNames)
        {
            var normalizedMatch = Normalize(matchName);
            if (string.IsNullOrWhiteSpace(normalizedMatch))
            {
                continue;
            }

            var exact = PickHighestVersion(installedApps.Where(app => Normalize(app.DisplayName) == normalizedMatch));
            if (exact is not null)
            {
                return exact;
            }

            if (normalizedMatch.Length <= 4)
            {
                continue;
            }

            var contains = PickHighestVersion(installedApps.Where(app => Normalize(app.DisplayName).Contains(normalizedMatch)));
            if (contains is not null)
            {
                return contains;
            }
        }

        return null;
    }

    private static InstalledApp? PickHighestVersion(IEnumerable<InstalledApp> apps)
    {
        return apps
            .Where(app => !string.IsNullOrWhiteSpace(app.DisplayVersion))
            .OrderByDescending(app => app.DisplayVersion, VersionDisplayComparer.Instance)
            .ThenBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private sealed class VersionDisplayComparer : IComparer<string>
    {
        public static VersionDisplayComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            return VersionComparer.Compare(x, y);
        }
    }
}
