using System.Diagnostics;

namespace ProperAppUpdater.Services;

internal static class ProcessTermination
{
    private static readonly TimeSpan ExitGracePeriod = TimeSpan.FromSeconds(5);

    public static async Task KillTreeAndWaitAsync(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch
        {
            // Best effort: the process may have exited or Windows may deny access.
        }

        try
        {
            await process.WaitForExitAsync().WaitAsync(ExitGracePeriod);
        }
        catch
        {
            // Do not hide the original timeout/cancellation if cleanup itself fails.
        }
    }
}
