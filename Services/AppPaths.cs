namespace ProperAppUpdater.Services;

public static class AppPaths
{
    public static string DataRoot { get; } = ResolveDataRoot();

    private static string ResolveDataRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("SWEEPABLER_DATA_ROOT");
        return string.IsNullOrWhiteSpace(overrideRoot) ? DefaultDataRoot : Path.GetFullPath(overrideRoot);
    }

    private static string DefaultDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Supurucu");

    public static string DownloadsRoot { get; } = Path.Combine(DataRoot, "Downloads");

    public static string IconCacheRoot { get; } = Path.Combine(DataRoot, "Icons");

    public static string GitHubCacheRoot { get; } = Path.Combine(DataRoot, "GitHubCache");

    public static string HistoryPath { get; } = Path.Combine(DataRoot, "history.json");

    public static string CrashLogPath { get; } = Path.Combine(DataRoot, "crash.log");

    public static string UserCatalogPath { get; } = Path.Combine(DataRoot, "catalog.user.json");

    public static string SettingsPath { get; } = Path.Combine(DataRoot, "settings.json");

    public static string UnsupportedAppsReportPath { get; } = Path.Combine(DataRoot, "unsupported-apps.json");

    public static string UnsupportedAppsIgnorePath { get; } = Path.Combine(DataRoot, "unsupported.ignore.json");

    public static string BlockedUpdatesPath { get; } = Path.Combine(DataRoot, "blocked-updates.json");

    public static void EnsureCreated()
    {
        SafePath.RejectReparsePoints(DataRoot);
        Directory.CreateDirectory(DataRoot);
        foreach (var path in new[] { DownloadsRoot, IconCacheRoot, GitHubCacheRoot, HistoryPath, CrashLogPath,
                     UserCatalogPath, SettingsPath, UnsupportedAppsReportPath, UnsupportedAppsIgnorePath, BlockedUpdatesPath,
                     Path.Combine(DataRoot, "assistant-scale.txt"), Path.Combine(DataRoot, "self-maintenance.log") })
            SafePath.RequireInside(DataRoot, path);
        Directory.CreateDirectory(DownloadsRoot);
        Directory.CreateDirectory(IconCacheRoot);
        Directory.CreateDirectory(GitHubCacheRoot);

        CreateEmptyJson(UserCatalogPath);
        CreateEmptyJson(UnsupportedAppsIgnorePath);
    }

    private static void CreateEmptyJson(string path)
    {
        if (File.Exists(path)) return;
        SafePath.RequireInside(DataRoot, path);
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write("[]"u8);
        }
        catch (IOException) when (File.Exists(path))
        {
            SafePath.RequireInside(DataRoot, path);
        }
    }
}
