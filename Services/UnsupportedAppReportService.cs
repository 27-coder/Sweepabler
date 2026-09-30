using System.Text.Json;
using ProperAppUpdater.Models;

namespace ProperAppUpdater.Services;

public sealed class UnsupportedAppReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<int> WriteAsync(
        IReadOnlyList<InstalledApp> installedApps,
        IReadOnlyList<CatalogEntry> catalog,
        IEnumerable<string> packageManagerSupportedNames,
        CancellationToken cancellationToken)
    {
        var matchedRegistryPaths = catalog
            .Select(entry => AppMatcher.FindInstalledMatch(entry, installedApps)?.RegistryPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var supportedNames = packageManagerSupportedNames
            .Select(AppMatcher.Normalize)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ignoredNames = await LoadIgnoredNamesAsync(cancellationToken);

        var unsupported = installedApps
            .Where(app => !matchedRegistryPaths.Contains(app.RegistryPath))
            .Where(app => !supportedNames.Contains(AppMatcher.Normalize(app.DisplayName)))
            .Where(app => !ignoredNames.Contains(AppMatcher.Normalize(app.DisplayName)))
            .Where(app => !ShouldSuppress(app))
            .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(BuildEntry)
            .ToList();

        AppPaths.EnsureCreated();
        await AtomicJsonFile.WriteAsync(AppPaths.UnsupportedAppsReportPath, unsupported, JsonOptions, cancellationToken);

        return unsupported.Count;
    }

    private static async Task<HashSet<string>> LoadIgnoredNamesAsync(CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(AppPaths.UnsupportedAppsIgnorePath))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            await using var stream = File.OpenRead(AppPaths.UnsupportedAppsIgnorePath);
            var names = await JsonSerializer.DeserializeAsync<List<string>>(stream, JsonOptions, cancellationToken)
                ?? new List<string>();

            return names
                .Select(AppMatcher.Normalize)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static UnsupportedAppReportEntry BuildEntry(InstalledApp app)
    {
        return new UnsupportedAppReportEntry
        {
            DisplayName = app.DisplayName,
            DisplayVersion = app.DisplayVersion,
            Publisher = app.Publisher,
            SignedPublisher = TryResolveSignedPublisher(app),
            CandidateOfficialUrl = PickFirstUrl(app.UrlUpdateInfo, app.UrlInfoAbout, app.HelpLink),
            UrlUpdateInfo = app.UrlUpdateInfo,
            UrlInfoAbout = app.UrlInfoAbout,
            HelpLink = app.HelpLink,
            InstallLocation = app.InstallLocation,
            DisplayIcon = app.DisplayIcon,
            RegistryPath = app.RegistryPath,
            NextStep = "Verify official domain/feed, signer, installer type, and silent args before adding a catalog entry."
        };
    }

    private static string PickFirstUrl(params string[] urls)
    {
        return urls.FirstOrDefault(url => Uri.TryCreate(url, UriKind.Absolute, out _)) ?? string.Empty;
    }

    private static string TryResolveSignedPublisher(InstalledApp app)
    {
        var candidate = NormalizeDisplayIconPath(app.DisplayIcon);
        if (File.Exists(candidate))
        {
            return SignatureInspector.TryGetSignerName(candidate);
        }

        if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
        {
            var exe = Directory.EnumerateFiles(app.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path.Length)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(exe))
            {
                return SignatureInspector.TryGetSignerName(exe);
            }
        }

        return string.Empty;
    }

    private static bool ShouldSuppress(InstalledApp app)
    {
        var name = app.DisplayName;
        var publisher = app.Publisher;
        var registryPath = app.RegistryPath;

        if (ContainsAny(registryPath, "Steam App") ||
            ContainsAny(publisher, "Valve", "CAPCOM", "FromSoftware", "Destructive Creations") ||
            ContainsAny(name, "Counter-Strike", "ELDEN RING", "Dave The Diver", "Devil May Cry", "Yakuza", "Sekiro", "Hatred", "Banana"))
        {
            return true;
        }

        if (ContainsAny(publisher, "Google\\Chrome") ||
            ContainsAny(name, "Dokümanlar", "E-Tablolar", "Slaytlar"))
        {
            return true;
        }

        if (ContainsAny(name,
                "GDR ",
                "Security Update",
                "Hotfix",
                "Update for SQL Server",
                "Browser for SQL Server",
                "SQL Server 2019 T-SQL Language Service",
                "SQL Server 2019 Setup",
                "Microsoft VSS Writer for SQL Server",
                "Microsoft SQL Server 2019 (64-bit)",
                "Microsoft Teams Meeting Add-in",
                "Microsoft Update Health Tools",
                "Mozilla Maintenance Service"))
        {
            return true;
        }

        if (IsPythonComponent(name))
        {
            return true;
        }

        if (ContainsAny(name,
                "Redistributable",
                "Redistributables",
                "Runtime",
                "Shared Framework",
                "Windows SDK",
                "WPTx64",
                "vs_CoreEditorFonts",
                "TeighaX",
                "CEF for SOLIDWORKS",
                "Epic Online Services",
                "GameInput",
                "OLE DB Driver",
                "ODBC Driver",
                "Native Client",
                "Server Speech Platform"))
        {
            return true;
        }

        if (ContainsAny(name,
                "Driver",
                "Sürücüsü",
                "Chipset Software",
                "NVIDIA",
                "Realtek",
                "Focusrite Audio Drivers",
                "Razer Chroma",
                "Razer Synapse",
                "PUSAT K3",
                "ugeeTablet"))
        {
            return true;
        }

        if (ContainsAny(name,
                "EasyTuneEngineService",
                "GService",
                "DaVinci Resolve Control Panels",
                "3DEXPERIENCE Exchange",
                "3DEXPERIENCE Marketplace",
                "Autodesk Network License Manager",
                "SOLIDWORKS 2023 SP04"))
        {
            return true;
        }

        return false;
    }

    private static bool IsPythonComponent(string name)
    {
        if (!name.StartsWith("Python ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ContainsAny(name,
            " Add to Path ",
            " Core Interpreter ",
            " Development Libraries ",
            " Documentation ",
            " Executables ",
            " pip Bootstrap ",
            " Standard Library ",
            " Tcl/Tk Support ",
            " Test Suite ");
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle =>
            value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeDisplayIconPath(string displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return string.Empty;
        }

        var value = displayIcon.Trim().Trim('"');
        var commaIndex = value.LastIndexOf(',');
        if (commaIndex > 0)
        {
            value = value[..commaIndex].Trim().Trim('"');
        }

        return Environment.ExpandEnvironmentVariables(value);
    }
}
