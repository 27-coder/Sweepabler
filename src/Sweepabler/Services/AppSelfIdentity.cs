using System.Reflection;
using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

public static class AppSelfIdentity
{
    private static readonly string[] SelfNames =
    {
        "Süpürücü",
        "Supurucu",
        "Sweeper",
        "Sweepable'r",
        "ProperAppUpdater"
    };

    public static InstalledApp CreateInstalledApp()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppSelfIdentity).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "1.0.0";

        return new InstalledApp
        {
            DisplayName = "Süpürücü",
            DisplayVersion = version,
            Publisher = "vneex",
            InstallLocation = AppContext.BaseDirectory,
            DisplayIcon = Path.Combine(AppContext.BaseDirectory, "Süpürücü.exe"),
            RegistryPath = "self"
        };
    }

    public static bool IsSelf(CatalogEntry catalog, string displayName)
    {
        if (IsSelfText(catalog.Id) || IsSelfText(catalog.Name) || IsSelfText(displayName))
        {
            return true;
        }

        return catalog.MatchNames.Any(IsSelfText);
    }

    private static bool IsSelfText(string value)
    {
        var normalized = AppMatcher.Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return SelfNames
            .Select(AppMatcher.Normalize)
            .Any(name => normalized.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
