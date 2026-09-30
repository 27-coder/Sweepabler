using System.Collections.ObjectModel;
using System.Windows;
using ProperAppUpdater.Services;
using ProperAppUpdater.ViewModels;

namespace ProperAppUpdater;

public partial class PirateLoverWindow : Window
{
    private readonly InstalledAppScanner _scanner = new();
    private readonly UnsupportedIgnoreService _ignoreService = new();

    public PirateLoverWindow()
    {
        InitializeComponent();
        DataContext = this;
        ApplyLocalization();
    }

    public ObservableCollection<IgnoreAppViewModel> Apps { get; } = new();

    private void ApplyLocalization()
    {
        var text = LocalizationService.Current;
        Title = text.Get("PirateTitle");
        TitleText.Text = text.Get("PirateTitle");
        IntroText.Text = text.Get("PirateIntro");
        IgnoreButton.Content = text.Get("DoNotSweep");
        CloseButton.Content = text.Get("Close");
        StatusText.Text = text.Get("AllAppsLoading");
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var ignored = (await _ignoreService.LoadAsync(CancellationToken.None))
            .Select(AppMatcher.Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Apps.Clear();
        var installedApps = await Task.Run(_scanner.Scan);
        var distinctApps = SelectDistinctApps(installedApps);

        foreach (var app in distinctApps)
        {
            Apps.Add(new IgnoreAppViewModel
            {
                DisplayName = app.DisplayName,
                DisplayVersion = app.DisplayVersion,
                Publisher = app.Publisher,
                IsSelected = ignored.Contains(AppMatcher.Normalize(app.DisplayName))
            });
        }

        StatusText.Text = string.Empty;
    }

    internal static IReadOnlyList<ProperAppUpdater.Models.InstalledApp> SelectDistinctApps(
        IEnumerable<ProperAppUpdater.Models.InstalledApp> installedApps)
    {
        return installedApps
            .GroupBy(app => AppMatcher.Normalize(app.DisplayName), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(app => app.DisplayVersion, InstalledVersionComparer.Instance)
                .First())
            .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async void IgnoreButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = Apps
            .Where(app => app.IsSelected)
            .Select(app => app.DisplayName)
            .ToList();

        var saved = await _ignoreService.ReplaceAsync(selected, CancellationToken.None);
        StatusText.Text = string.Format(LocalizationService.Current.Get("IgnoredSaved"), saved);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private sealed class InstalledVersionComparer : IComparer<string>
    {
        public static InstalledVersionComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            return VersionComparer.Compare(x, y);
        }
    }
}
