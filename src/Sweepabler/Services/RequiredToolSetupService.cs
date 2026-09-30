using System.Diagnostics;
using System.Net.Http;
using System.Text;

namespace ProperAppUpdater.Services;

internal interface ISetupToolHost
{
    Task<bool> IsUsableAsync(string tool, CancellationToken cancellationToken);
    Task InstallAsync(string tool, IProgress<string> progress, CancellationToken cancellationToken);
    void RefreshEnvironment();
}

internal sealed class RequiredToolSetupService(ISetupToolHost host)
{
    public static readonly string[] RequiredTools = { "winget", "git", "choco", "scoop" };

    public async Task EnsureAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        host.RefreshEnvironment();
        foreach (var tool in RequiredTools)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(string.Format(LocalizationService.Current.Get("SetupCheckingTool"), tool));
            if (await host.IsUsableAsync(tool, cancellationToken)) continue;
            progress.Report(string.Format(LocalizationService.Current.Get("SetupInstallingTool"), tool));
            await host.InstallAsync(tool, progress, cancellationToken);
            host.RefreshEnvironment();
            if (!await host.IsUsableAsync(tool, cancellationToken))
                throw new InvalidOperationException(string.Format(LocalizationService.Current.Get("SetupToolFailed"), tool));
        }
    }
}

internal sealed class WindowsSetupToolHost : ISetupToolHost
{
    public void RefreshEnvironment() => ToolLocator.RefreshEnvironment();

    public async Task<bool> IsUsableAsync(string tool, CancellationToken cancellationToken)
    {
        var path = ToolLocator.Find(tool);
        if (path is null) return false;
        try
        {
            var start = tool == "scoop" ? PowerShellStart(ToolLocator.ScoopCommand("scoop --version")) : new ProcessStartInfo(path);
            if (tool != "scoop") start.ArgumentList.Add("--version");
            return await RunAsync(start, TimeSpan.FromSeconds(30), null, cancellationToken) == 0;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return false; }
    }

    public async Task InstallAsync(string tool, IProgress<string> progress, CancellationToken cancellationToken)
    {
        ProcessStartInfo start;
        if (tool == "winget")
        {
            start = PowerShellStart("[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072; " +
                "Install-PackageProvider -Name NuGet -Force -Scope CurrentUser | Out-Null; " +
                "Install-Module -Name Microsoft.WinGet.Client -Repository PSGallery -Force -Scope CurrentUser -AcceptLicense; " +
                "Import-Module Microsoft.WinGet.Client; Repair-WinGetPackageManager -AllUsers");
        }
        else if (tool is "git" or "choco")
        {
            start = new ProcessStartInfo(ToolLocator.Require("winget"));
            foreach (var argument in new[] { "install", "--id", tool == "git" ? "Git.Git" : "Chocolatey.Chocolatey", "--exact", "--source", "winget", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" })
                start.ArgumentList.Add(argument);
        }
        else if (tool == "scoop")
        {
            await InstallScoopAsync(progress, cancellationToken);
            return;
        }
        else throw new ArgumentException("Unknown setup tool.");

        var exit = await RunAsync(start, TimeSpan.FromMinutes(20), progress, cancellationToken);
        if (!UpdateExecutor.IsSuccessfulInstallerExit(exit))
            throw new InvalidOperationException(string.Format(LocalizationService.Current.Get("SetupToolFailed"), tool) + $" ({exit})");
    }

    private static async Task InstallScoopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        const string url = "https://raw.githubusercontent.com/ScoopInstaller/Install/master/install.ps1";
        var script = SafePath.RequireInside(AppPaths.DownloadsRoot,
            Path.Combine(AppPaths.DownloadsRoot, "scoop-install-" + Guid.NewGuid().ToString("N") + ".ps1"));
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.AbsoluteUri != url || response.Content.Headers.ContentLength is > 1024 * 1024)
            throw new InvalidOperationException("Unexpected Scoop installer response.");
        try
        {
            await using (var destination = new FileStream(script, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                var buffer = new byte[32 * 1024];
                var total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > 1024 * 1024) throw new IOException("Scoop installer script is too large.");
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (total == 0) throw new IOException("Scoop installer script is empty.");
            }
            SafePath.RequireInside(AppPaths.DownloadsRoot, script);
            // Hold the downloaded script read-only until PowerShell finishes reading it.
            using var lockedScript = new FileStream(script, FileMode.Open, FileAccess.Read, FileShare.Read);
            var start = PowerShellStart("& '" + script.Replace("'", "''") + "' -RunAsAdmin; if ($LASTEXITCODE -ne 0) { throw 'Scoop installation failed.' }");
            var exit = await RunAsync(start, TimeSpan.FromMinutes(20), progress, cancellationToken);
            if (exit != 0) throw new InvalidOperationException(string.Format(LocalizationService.Current.Get("SetupToolFailed"), "scoop"));
        }
        finally
        {
            SafePath.RequireInside(AppPaths.DownloadsRoot, script);
            File.Delete(script);
        }
    }

    internal static ProcessStartInfo PowerShellStart(string command)
    {
        var start = new ProcessStartInfo(ToolLocator.PowerShellPath);
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; " + command)) })
            start.ArgumentList.Add(argument);
        return start;
    }

    private static async Task<int> RunAsync(ProcessStartInfo start, TimeSpan timeout, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.WindowStyle = ProcessWindowStyle.Hidden;
        start.WorkingDirectory = AppPaths.DataRoot;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = new Process { StartInfo = start };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var exitTask = process.WaitForExitAsync(deadline.Token);
        try
        {
            while (!process.HasExited)
            {
                await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(15), deadline.Token));
                deadline.Token.ThrowIfCancellationRequested();
                if (!process.HasExited) progress?.Report(LocalizationService.Current.Get("SetupStillPreparing"));
            }
            await exitTask;
            await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0 && progress is not null) progress.Report(LocalizationService.Current.Get("SetupToolError"));
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            await ProcessTermination.KillTreeAndWaitAsync(process);
            await Task.WhenAll(stdout, stderr);
            if (!cancellationToken.IsCancellationRequested) throw new TimeoutException(LocalizationService.Current.Get("InstallerTimedOut"));
            throw;
        }
    }
}
