using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ProperAppUpdater.Controls;

namespace ProperAppUpdater;

public partial class MainWindow
{
    private static readonly string[] DeckBrushKeys =
    [
        "WindowBackgroundBrush", "WindowBorderBrush", "PanelBrush", "InkBrush", "MutedBrush",
        "LineBrush", "ButtonBackgroundBrush", "ButtonBorderBrush", "PrimaryBrush", "PrimaryForegroundBrush", "AccentForegroundBrush"
    ];

    private void WorkspaceSwitcher_SelectedDeckChanged(object? sender, EventArgs e)
    {
        RefreshDeckLocalization();
        AnimateDeckSwipe();
    }

    private void RefreshDeckLocalization()
    {
        var deck = WorkspaceSwitcher.SelectedDeck;
        var label = Text.Get($"Deck{deck}");
        Title = $"{Text.AppName} · {label}";
        ModeTitleText.Text = label;
        WorkspaceSwitcher.RefreshLocalization();
        DeckStatusText.Text = Text.Get("DeckInDevelopment");
        DevelopmentBadgeText.Text = Text.Get("DeckPlanned");
        DevelopmentFooterText.Text = Text.Get("DeckPreviewFooter");
        if (deck != WorkspaceDeck.Update)
        {
            DeckTitleText.Text = Text.Get($"Deck{deck}Title");
            DeckDescriptionText.Text = Text.Get($"Deck{deck}Description");
            PlannedFeaturesText.Text = Text.Get($"Deck{deck}Features");
            PreviewFindButton.Content = Text.Get($"Deck{deck}Find");
            PreviewSweepButton.Content = Text.Get($"Deck{deck}Action");
            PreviewFindButton.ToolTip = Text.Get("DeckPreviewFooter");
            PreviewSweepButton.ToolTip = Text.Get("DeckPreviewFooter");
        }
        ApplyDeck();
    }

    private void ApplyDeck()
    {
        var deck = WorkspaceSwitcher.SelectedDeck;
        var update = deck == WorkspaceDeck.Update;
        UpdateActions.Visibility = UpdatePage.Visibility = StatusText.Visibility = FooterText.Visibility =
            update ? Visibility.Visible : Visibility.Collapsed;
        PreviewActions.Visibility = DevelopmentPage.Visibility = DeckStatusText.Visibility = DevelopmentFooterText.Visibility =
            update ? Visibility.Collapsed : Visibility.Visible;

        // Window-scoped colors leave the broom assistant and all other windows alone.
        foreach (var key in DeckBrushKeys) Resources.Remove(key);
        string[] colors = deck switch
        {
            WorkspaceDeck.Update =>
                [WorkspacePalette.Background(deck), "#3E2B1C", "#28160C", "#DFD4CB", "#B3A295", "#3E2B1C", "#2D1C11", "#4B3121", "#382011", "#CFBEB0", "#A97B5A"],
            WorkspaceDeck.Delete =>
                [WorkspacePalette.Background(deck), "#1E3023", "#0C1D13", "#D4DED6", "#9BAC9F", "#1E3023", "#102318", "#273E2E", "#193225", "#BDD0C2", "#789785"],
            WorkspaceDeck.Patch =>
                [WorkspacePalette.Background(deck), "#20304A", "#0D1E37", "#D2DAE5", "#9FABBF", "#20304A", "#13243C", "#2B3E5B", "#1B2D48", "#BEC9D8", "#8196B5"],
            WorkspaceDeck.Backup =>
                [WorkspacePalette.Background(deck), "#222222", "#030303", "#D4D4D4", "#A2A2A2", "#202020", "#090909", "#2B2B2B", "#191919", "#C0C0C0", "#999999"],
            _ => throw new ArgumentOutOfRangeException(nameof(deck))
        };
        for (var index = 0; index < DeckBrushKeys.Length; index++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[index]));
            brush.Freeze();
            Resources[DeckBrushKeys[index]] = brush;
        }
        Resources["BadgeBackgroundBrush"] = Resources["ButtonBackgroundBrush"];
        Resources["BadgeBorderBrush"] = Resources["LineBrush"];
        Resources["BadgeForegroundBrush"] = Resources["AccentForegroundBrush"];
        Resources["FamilyBackgroundBrush"] = Resources["ButtonBackgroundBrush"];
        Resources["FamilyHeaderBrush"] = Resources["PrimaryBrush"];
        Resources["FamilyBorderBrush"] = Resources["LineBrush"];
        Resources["FamilyForegroundBrush"] = Resources["InkBrush"];
    }

    private void AnimateDeckSwipe()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var update = WorkspaceSwitcher.SelectedDeck == WorkspaceDeck.Update;
        FrameworkElement[] parts = update ? [UpdateActions, UpdatePage] : [PreviewActions, DevelopmentPage];
        foreach (var part in parts)
        {
            var move = new TranslateTransform();
            part.RenderTransform = move;
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(230))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
            part.BeginAnimation(OpacityProperty, new DoubleAnimation(0.75, 1, TimeSpan.FromMilliseconds(230))
            {
                FillBehavior = FillBehavior.Stop
            });
        }
    }
}
