using System.ComponentModel;
using System.Windows;
using ProperAppUpdater.Services;

namespace ProperAppUpdater;

public partial class SetupWindow : Window
{
    private readonly bool _languageOnly;
    private readonly CancellationTokenSource _preparationCts = new();
    private bool _preparing;
    private bool _closeRequested;

    public SetupWindow(bool languageOnly = false)
    {
        InitializeComponent();
        _languageOnly = languageOnly;
        QuestionText.Text = "Choose your language";
        SubtitleText.Text = languageOnly ? "Select your preferred language." :
            "WinGet · Git · Chocolatey · Scoop\nRequired tools will be installed.";
        Closed += (_, _) => _preparationCts.Dispose();
    }

    public string SelectedLanguage { get; private set; } = "tr";
    public bool ToolsRefreshed { get; private set; }

    private async void EnglishButton_Click(object sender, RoutedEventArgs e) => await SelectLanguageAsync("en");
    private async void TurkishButton_Click(object sender, RoutedEventArgs e) => await SelectLanguageAsync("tr");
    private async void RetryButton_Click(object sender, RoutedEventArgs e) => await SelectLanguageAsync(SelectedLanguage);

    private async Task SelectLanguageAsync(string language)
    {
        if (_preparing) return;
        SelectedLanguage = language;
        if (_languageOnly) { DialogResult = true; return; }
        LocalizationService.SetCurrent(language);
        var text = LocalizationService.Current;
        Title = text.AppName;
        QuestionText.Text = text.Get("SetupPreparing");
        System.Windows.Automation.AutomationProperties.SetName(CancelButton, text.Get("Close"));
        SubtitleText.Visibility = Visibility.Collapsed;
        LanguageButtons.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Collapsed;
        PreparationPanel.Visibility = Visibility.Visible;
        PreparationProgress.Visibility = Visibility.Visible;
        _preparing = true;
        var completed = false;
        try
        {
            await new EnvironmentPreparationService().PrepareAsync(
                new Progress<string>(message => { PreparationText.Text = message; PreparationText.ToolTip = message; }), _preparationCts.Token);
            ToolsRefreshed = true;
            completed = !_closeRequested;
        }
        catch (OperationCanceledException) when (_preparationCts.IsCancellationRequested) { }
        catch (Exception exception)
        {
            CrashLogger.Log(exception);
            QuestionText.Text = text.Get("SetupNeedsRepair");
            PreparationText.Text = exception.Message;
            PreparationText.ToolTip = exception.Message;
            RetryButton.Content = text.Get("SetupRetry");
            RetryButton.Visibility = Visibility.Visible;
        }
        finally
        {
            _preparing = false;
            PreparationProgress.Visibility = Visibility.Collapsed;
            if (_closeRequested) Close();
            else if (completed) DialogResult = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _preparationCts.Cancel();
        if (_preparing) { _closeRequested = true; e.Cancel = true; }
    }
}
