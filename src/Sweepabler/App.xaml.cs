using System.Windows;
using System.Windows.Threading;
using ProperAppUpdater.Models;
using ProperAppUpdater.Services;

namespace ProperAppUpdater;

public partial class App : System.Windows.Application
{
    private readonly AppSettingsService _settingsService = new();

    public static AppSettings Settings { get; private set; } = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                CrashLogger.Log(exception);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLogger.Log(args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            AppPaths.EnsureCreated();
            Settings = await _settingsService.LoadAsync(CancellationToken.None);

            if (string.IsNullOrWhiteSpace(Settings.Language) || Settings.SetupVersion < 2 || RequiredToolSetupService.RequiredTools.Any(tool => ToolLocator.Find(tool) is null))
            {
                SystemThemeService.Apply(Resources);
                var setupWindow = new SetupWindow();
                var accepted = setupWindow.ShowDialog() == true;
                if (!accepted) { Shutdown(); return; }
                Settings.Language = setupWindow.SelectedLanguage;
                Settings.SetupVersion = 2;
                if (setupWindow.ToolsRefreshed) Settings.LastMaintenanceUtc = DateTimeOffset.UtcNow;
                await _settingsService.SaveAsync(Settings, CancellationToken.None);
            }

            LocalizationService.SetCurrent(Settings.Language);
            SystemThemeService.Apply(Resources);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            CrashLogger.Log(exception);
            MessageBox.Show(
                exception.Message,
                LocalizationService.Current.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLogger.Log(e.Exception);
        e.Handled = true;

        if (Current.MainWindow is MainWindow window)
        {
            window.ShowRecoverableError(e.Exception.Message);
            return;
        }

        MessageBox.Show(e.Exception.Message, LocalizationService.Current.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
