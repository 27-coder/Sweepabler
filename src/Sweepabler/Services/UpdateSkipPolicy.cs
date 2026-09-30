using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

public static class UpdateSkipPolicy
{
    // Only apps whose own updater fights back (re-launches themselves during an update)
    // are hard-blocked. Everything else — including apps that auto-start — is allowed to
    // be checked and updated; if one is actually running at install time, the
    // close-running-app flow (RunningAppGuard) handles it.
    private static readonly string[] HardBlockedTokens =
    {
        "discord"
    };

    public static bool ShouldSkip(CatalogEntry catalog, InstalledApp installedApp, out string reason)
    {
        var identity = new[] { catalog.Id, catalog.Name, installedApp.DisplayName }
            .Concat(catalog.MatchNames);
        return ShouldSkip(identity, out reason);
    }

    public static bool ShouldSkip(PackageManagerUpdate update, InstalledApp? installedApp, out string reason)
    {
        var identity = new[] { update.PackageId, update.DisplayName, installedApp?.DisplayName };
        return ShouldSkip(identity, out reason);
    }

    private static bool ShouldSkip(IEnumerable<string?> identityValues, out string reason)
    {
        // Match the blacklist against identity fields only (id / name), never publisher or
        // file paths, so an unrelated app cannot be skipped just because a blocked token
        // appears somewhere in its install path or vendor name.
        var normalized = identityValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => AppMatcher.Normalize(value!))
            .Where(value => value.Length > 0);

        if (normalized.Any(value => HardBlockedTokens.Any(token => value.Contains(token, StringComparison.Ordinal))))
        {
            reason = "Discord-style self-opening updater is blacklisted.";
            return true;
        }

        reason = string.Empty;
        return false;
    }
}
