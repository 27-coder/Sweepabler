namespace ProperAppUpdater.Services;

internal static class ToolLocator
{
    public static string PowerShellPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    public static string? Find(string command)
    {
        if (command.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase)) return PowerShellPath;
        var name = Path.GetFileNameWithoutExtension(command);
        if (name is not ("winget" or "choco" or "scoop" or "git")) return null;
        var directories = new List<string>();
        void Add(string? directory) { if (!string.IsNullOrWhiteSpace(directory) && Path.IsPathFullyQualified(directory)) directories.Add(directory); }
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add(Path.Combine(local, "Microsoft", "WindowsApps"));
        Add(Path.Combine(Environment.GetEnvironmentVariable("ChocolateyInstall") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey"), "bin"));
        Add(Path.Combine(Environment.GetEnvironmentVariable("SCOOP") ?? Path.Combine(profile, "scoop"), "shims"));
        Add(Path.Combine(programFiles, "Git", "cmd"));
        Add(Path.Combine(local, "Programs", "Git", "cmd"));
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            Add(directory.Trim().Trim('"'));
        var extensions = name == "scoop" ? new[] { ".ps1" } : new[] { ".exe" };
        foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var extension in extensions)
            {
                var path = Path.GetFullPath(Path.Combine(directory, name + extension));
                if (File.Exists(path)) return path;
            }
        return null;
    }

    public static string Require(string command) => Find(command) ?? throw new InvalidOperationException(string.Format(LocalizationService.Current.Get("ToolMissing"), command));

    public static string ScoopCommand(string command)
    {
        if (!command.StartsWith("scoop ", StringComparison.Ordinal)) throw new ArgumentException("Expected a Scoop command.");
        return "& '" + Require("scoop").Replace("'", "''") + "' " + command[6..];
    }

    public static void RefreshEnvironment()
    {
        var paths = new[] {
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)
        }.Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, paths));
        foreach (var name in new[] { "SCOOP", "ChocolateyInstall" })
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
                Environment.SetEnvironmentVariable(name, Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine));
    }
}
