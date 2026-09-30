namespace ProperAppUpdater.Services;

public static class CrashLogger
{
    public static void Log(Exception exception)
    {
        try
        {
            AppPaths.EnsureCreated();
            File.AppendAllText(
                AppPaths.CrashLogPath,
                $"{DateTimeOffset.Now:u}\r\n{exception}\r\n\r\n");
        }
        catch
        {
            // Crash logging is best effort and must not crash the app.
        }
    }
}
