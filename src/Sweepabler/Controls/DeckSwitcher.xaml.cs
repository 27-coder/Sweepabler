using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ProperAppUpdater.Services;
using Path = System.Windows.Shapes.Path;

namespace ProperAppUpdater.Controls;

public enum WorkspaceDeck { Update, Delete, Patch, Backup }

public partial class DeckSwitcher : UserControl
{
    public static readonly DependencyProperty WardrobeAngleProperty = DependencyProperty.Register(
        nameof(WardrobeAngle), typeof(double), typeof(DeckSwitcher),
        new PropertyMetadata(-10.0, (owner, _) => ((DeckSwitcher)owner).RenderWardrobe()));

    private WorkspaceDeck _selectedDeck;
    private double _targetAngle = -10;

    public double WardrobeAngle
    {
        get => (double)GetValue(WardrobeAngleProperty);
        set => SetValue(WardrobeAngleProperty, value);
    }

    public DeckSwitcher()
    {
        InitializeComponent();
        SelectedDeck = WorkspaceDeck.Update;
        RenderWardrobe();
    }

    public event EventHandler? SelectedDeckChanged;

    public WorkspaceDeck SelectedDeck
    {
        get => _selectedDeck;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            var changed = _selectedDeck != value;
            var turns = ((int)value - (int)_selectedDeck + 4) % 4;
            _selectedDeck = value;
            RefreshPanelColors();
            RefreshLocalization();
            if (!changed) return;
            _targetAngle += turns * 90;
            PlaySwipe();
            SelectedDeckChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RenderWardrobe()
    {
        if (UpdateFace is null) return;
        Path[] faces = [UpdateFace, DeleteFace, PatchFace, BackupFace];
        TextBlock[] letters = [UpdateLetter, DeleteLetter, PatchLetter, BackupLetter];
        for (var index = 0; index < faces.Length; index++)
        {
            // Four upright wings around a vertical hinge, viewed slightly from above.
            // Y rotation changes their projection and depth instead of rotating a flat X.
            var angle = (-45 + index * 90 - WardrobeAngle) * Math.PI / 180;
            var x = Math.Sin(angle) * 55;
            var depth = Math.Cos(angle) * 18;
            var geometry = new StreamGeometry();
            using (var drawing = geometry.Open())
            {
                drawing.BeginFigure(new Point(64, 19), true, true);
                drawing.LineTo(new Point(64 + x, 19 + depth), true, false);
                drawing.LineTo(new Point(64 + x, 76 + depth), true, false);
                drawing.LineTo(new Point(64, 76), true, false);
            }
            geometry.Freeze();
            faces[index].Data = geometry;
            var layer = (int)Math.Round((depth + 18) * 10) * 2;
            Panel.SetZIndex(faces[index], layer);
            Panel.SetZIndex(letters[index], layer + 1);

            // Keep letters readable on both sides while following each panel's slope.
            var width = Math.Abs(x) / 48;
            var slope = Math.Sign(x) * depth / 48;
            letters[index].RenderTransform = new MatrixTransform(new Matrix(
                width, slope, 0, 1,
                64 + x * 0.63 - 16 * width,
                19 + depth * 0.63 + 28.5 - 16 * slope - 20));
            letters[index].Opacity = Math.Clamp((Math.Abs(x) - 5) / 12, 0, 1)
                * Math.Clamp((depth + 2) / 6, 0, 1);
        }
    }

    public void RefreshLocalization()
    {
        var text = LocalizationService.Current;
        var next = (WorkspaceDeck)(((int)SelectedDeck + 1) % 4);
        var caption = string.Format(text.Get("DeckNextPage"), text.Get($"Deck{next}"));
        CycleButton.ToolTip = caption;
        AutomationProperties.SetName(CycleButton, caption);
        AutomationProperties.SetName(this, text.Get("DeckSwitcher"));
    }

    private void CycleButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsEnabled && CycleButton.IsEnabled)
            SelectedDeck = (WorkspaceDeck)(((int)SelectedDeck + 1) % 4);
    }

    private void RefreshPanelColors()
    {
        Path[] faces = [UpdateFace, DeleteFace, PatchFace, BackupFace];
        TextBlock[] letters = [UpdateLetter, DeleteLetter, PatchLetter, BackupLetter];
        string[] tops = ["#A8464B", "#95383E", "#B25055", "#B84D55"];
        string[] bottoms = ["#813035", "#68242B", "#842F38", "#8E343C"];
        for (var index = 0; index < faces.Length; index++)
        {
            var selected = index == (int)SelectedDeck;
            faces[index].Fill = FaceBrush(selected ? "#BD5C61" : tops[index], selected ? "#944047" : bottoms[index]);
            faces[index].Stroke = Brush(selected ? "#A05E62" : "#703039");
            faces[index].StrokeThickness = selected ? 1.8 : 1.25;
            letters[index].Foreground = Brush(WorkspacePalette.Background((WorkspaceDeck)index));
        }
    }

    private static LinearGradientBrush FaceBrush(string top, string bottom)
    {
        var brush = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(top),
            (Color)ColorConverter.ConvertFromString(bottom),
            new Point(0, 0), new Point(0, 1));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private void PlaySwipe()
    {
        var start = WardrobeAngle;
        BeginAnimation(WardrobeAngleProperty, null);
        WardrobeAngle = _targetAngle;
        if (!SystemParameters.ClientAreaAnimation) return;
        var duration = TimeSpan.FromMilliseconds(340);
        BeginAnimation(WardrobeAngleProperty, new DoubleAnimation(start, _targetAngle, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        });
        CabinetSwipe.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimationUsingKeyFrames
        {
            Duration = duration,
            KeyFrames =
            {
                new DiscreteDoubleKeyFrame(CabinetSwipe.X, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new SplineDoubleKeyFrame(-8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)), new KeySpline(0.4, 0, 1, 1)),
                new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(duration), new KeySpline(0, 0, 0.2, 1))
            }
        });
    }

    private void CycleButton_MouseEnter(object sender, MouseEventArgs e) => AnimateHover(1.06);

    private void CycleButton_MouseLeave(object sender, MouseEventArgs e) => AnimateHover(1);

    private void AnimateHover(double scale)
    {
        if (!IsEnabled || !SystemParameters.ClientAreaAnimation) return;
        var animation = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CabinetHover.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        CabinetHover.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private void Switcher_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsEnabled || e.Key is not (Key.Right or Key.Down)) return;
        CycleButton_Click(CycleButton, new RoutedEventArgs());
        e.Handled = true;
    }
}
