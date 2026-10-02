using System.Windows.Controls;
using ProperAppUpdater.Services;

namespace ProperAppUpdater;

public partial class MainWindow
{
    private bool _selfUpdateAvailable;

    private async Task CheckSelfUpdateAvailabilityAsync(CancellationToken cancellationToken)
    {
        SetSelfUpdateAvailable(false);
        try
        {
            var release = await new SelfMaintenanceService(_httpClient)
                .CheckForUpdateAsync(App.Settings.UpdateRepository, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_isClosing) SetSelfUpdateAvailable(release is not null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            // A failed self-check must not interrupt the normal application scan.
            CrashLogger.Log(exception);
        }
    }

    private void SetSelfUpdateAvailable(bool available)
    {
        _selfUpdateAvailable = available;
        RefreshSelfUpdateButton();
    }

    private void RefreshSelfUpdateButton() => SelfSweepButton.SetResourceReference(
        Control.ForegroundProperty, _selfUpdateAvailable ? "SelfUpdateAvailableBrush" : "InkBrush");
}
