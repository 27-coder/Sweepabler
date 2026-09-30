using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ProperAppUpdater.Services;

namespace ProperAppUpdater;

public partial class SweepAssistantWindow : Window
{
    private const double MinScale = 0.3;
    private const double MaxScale = 0.48;
    private const double DefaultScale = 0.3;
    private const int WorkDurationMs = 120_000;
    private const int CryDurationMs = 5_000;
    private const int CycleDurationMs = WorkDurationMs + CryDurationMs;
    private const int SweepStepMs = 80;
    private static readonly string ScalePath = Path.Combine(AppPaths.DataRoot, "assistant-scale.txt");

    private bool _closedByUser;
    private bool _animationStarted;
    private bool _failedState;
    private double _scale = DefaultScale;

    public SweepAssistantWindow()
    {
        InitializeComponent();
        SmallSizeMenuItem.Header = LocalizationService.Current.Get("AssistantSizeSmall");
        MediumSizeMenuItem.Header = LocalizationService.Current.Get("AssistantSizeMedium");
        LargeSizeMenuItem.Header = LocalizationService.Current.Get("AssistantSizeLarge");
        CloseMenuItem.Header = LocalizationService.Current.Get("Close");
        TargetCaptionText.Text = LocalizationService.Current.Get("AssistantTargetCaption");
        ApplyScale(LoadScale(), save: false, preserveCenter: false);
        SetTarget(LocalizationService.Current.Get("AssistantWorking"));
    }

    public bool IsClosedByUser => _closedByUser;

    public void SetTarget(string targetName)
    {
        var cleaned = string.IsNullOrWhiteSpace(targetName)
            ? LocalizationService.Current.Get("AssistantWorking")
            : targetName.Trim();
        TargetNameText.Text = cleaned;
        TargetNameText.ToolTip = cleaned;
        TargetNameText.FontSize = cleaned.Length switch
        {
            <= 18 => Lerp(12.6, 14.2, ScaleProgress),
            <= 30 => Lerp(11.6, 13.0, ScaleProgress),
            _ => Lerp(10.4, 11.8, ScaleProgress)
        };
    }

    public void SetMessage(string message)
    {
        Title = string.IsNullOrWhiteSpace(message)
            ? LocalizationService.Current.Get("AssistantWorking")
            : message;
    }

    public void SetWorkingState()
    {
        if (!_failedState)
        {
            return;
        }

        _failedState = false;
        TargetCaptionText.Text = LocalizationService.Current.Get("AssistantTargetCaption");
        TargetBadge.Background = Brushes.Transparent;
        TargetBadge.BorderBrush = Brushes.Transparent;
        FailureLayer.Opacity = 0;
        ResetPetRigPose();
        StartSweepAnimation(forceRestart: true);
    }

    public void SetFailureState()
    {
        _failedState = true;
        TargetCaptionText.Text = "://";
        TargetBadge.Background = Brushes.Transparent;
        TargetBadge.BorderBrush = Brushes.Transparent;
        StopSweepAnimation(stopCryMotion: false);
        ApplyFailurePose();
        StartCryMotionAnimations(hardCry: true);
        SweeperScaleTransform.ScaleX = 1;
        SweeperScaleTransform.ScaleY = 1;
        SweeperRotateTransform.Angle = 0;
        SweeperTranslateTransform.X = 0;
        SweeperTranslateTransform.Y = 0;
        CryLayer.Opacity = 1;
        FailureLayer.Opacity = 1;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 28;
        Top = workArea.Bottom - Height - 22;
        StartSweepAnimation(forceRestart: false);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        ApplyScale(_scale + (e.Delta > 0 ? 0.03 : -0.03), save: true, preserveCenter: true);
        e.Handled = true;
    }

    private void SmallSizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyScale(0.3, save: true, preserveCenter: true);
    }

    private void MediumSizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyScale(0.36, save: true, preserveCenter: true);
    }

    private void LargeSizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyScale(0.48, save: true, preserveCenter: true);
    }

    private void CloseMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _closedByUser = true;
        Close();
    }

    private void StartSweepAnimation(bool forceRestart)
    {
        if (_animationStarted && !forceRestart)
        {
            return;
        }

        _animationStarted = true;
        ResetPetRigPose();
        SweeperScaleTransform.ScaleX = 1;
        SweeperScaleTransform.ScaleY = 1;
        StartKeyframeAnimation(SweeperRotateTransform, RotateTransform.AngleProperty,
            BuildSweepKeys(amplitude: 2.6, holdValue: 0, periodMs: 1_120, phaseOffset: 0));
        StartKeyframeAnimation(SweeperTranslateTransform, TranslateTransform.XProperty,
            BuildSweepKeys(amplitude: 0.78, holdValue: 0, periodMs: 1_120, phaseOffset: 0));
        StartKeyframeAnimation(SweeperTranslateTransform, TranslateTransform.YProperty,
            BuildSweepKeys(amplitude: 0.25, holdValue: 0, periodMs: 1_120, phaseOffset: Math.PI / 2));
        StartDiscreteElementAnimation(CryLayer, OpacityProperty, new[]
        {
            (0, 0.0),
            (WorkDurationMs, 1.0),
            (CycleDurationMs, 0.0),
        });
        StartCryMotionAnimations(hardCry: false);
        FailureLayer.Opacity = 0;
    }

    private static (int Milliseconds, double Value)[] BuildSweepKeys(
        double amplitude,
        double holdValue,
        int periodMs,
        double phaseOffset)
    {
        var keys = new List<(int Milliseconds, double Value)>();
        keys.Add((0, holdValue));

        for (var elapsedMs = SweepStepMs; elapsedMs < WorkDurationMs; elapsedMs += SweepStepMs)
        {
            var phase = (elapsedMs / (double)periodMs) * 2 * Math.PI + phaseOffset;
            var value = holdValue + amplitude * WorkPhaseEnvelope(elapsedMs) * Math.Sin(phase);
            keys.Add((elapsedMs, Math.Round(value, 3)));
        }

        keys.Add((WorkDurationMs, holdValue));
        keys.Add((CycleDurationMs, holdValue));
        return keys.ToArray();
    }

    private static void StartKeyframeAnimation(Animatable target, DependencyProperty property, (int Milliseconds, double Value)[] keys)
    {
        var duration = TimeSpan.FromMilliseconds(keys[^1].Milliseconds);
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = duration,
            RepeatBehavior = RepeatBehavior.Forever
        };

        foreach (var key in keys)
        {
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(
                key.Value,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(key.Milliseconds)),
                new KeySpline(0.38, 0.0, 0.22, 1.0)));
        }

        target.BeginAnimation(property, animation);
    }

    private static void StartDiscreteElementAnimation(UIElement target, DependencyProperty property, (int Milliseconds, double Value)[] keys)
    {
        var duration = TimeSpan.FromMilliseconds(keys[^1].Milliseconds);
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = duration,
            RepeatBehavior = RepeatBehavior.Forever
        };

        foreach (var key in keys)
        {
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(
                key.Value,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(key.Milliseconds))));
        }

        target.BeginAnimation(property, animation);
    }

    private void ApplyScale(double scale, bool save, bool preserveCenter)
    {
        var oldCenterX = Left + ActualWidth / 2;
        var oldCenterY = Top + ActualHeight / 2;

        _scale = Math.Clamp(scale, MinScale, MaxScale);
        var t = (_scale - MinScale) / (MaxScale - MinScale);

        Width = Lerp(168, 218, t);
        Height = Lerp(144, 180, t);
        MinWidth = Width;
        MinHeight = Height;
        MaxWidth = Width;
        MaxHeight = Height;

        AssistantRoot.Width = Width;
        AssistantRoot.Height = Height;
        BadgeRow.Height = new GridLength(Lerp(38, 44, t));
        PetRow.Height = new GridLength(Height - BadgeRow.Height.Value);
        PetViewbox.Width = Lerp(118, 158, t);
        PetViewbox.Height = Lerp(94, 126, t);
        TargetBadge.Width = Lerp(136, 182, t);
        TargetBadge.Height = Lerp(30, 36, t);
        TargetBadge.CornerRadius = new CornerRadius(Lerp(8, 10, t));
        TargetCaptionText.FontSize = Lerp(9, 10.2, t);
        SetTarget(TargetNameText.Text);
        UpdateSizeMenuChecks();

        if (save)
        {
            SaveScale(_scale);
        }

        if (preserveCenter && IsLoaded)
        {
            Left = oldCenterX - Width / 2;
            Top = oldCenterY - Height / 2;
            KeepInsideWorkArea();
        }
    }

    private void KeepInsideWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        Left = Math.Clamp(Left, workArea.Left, workArea.Right - Width);
        Top = Math.Clamp(Top, workArea.Top, workArea.Bottom - Height);
    }

    private void UpdateSizeMenuChecks()
    {
        SmallSizeMenuItem.IsCheckable = true;
        MediumSizeMenuItem.IsCheckable = true;
        LargeSizeMenuItem.IsCheckable = true;
        SmallSizeMenuItem.IsChecked = _scale < 0.34;
        MediumSizeMenuItem.IsChecked = _scale >= 0.34 && _scale < 0.43;
        LargeSizeMenuItem.IsChecked = _scale >= 0.43;
    }

    private double ScaleProgress => (_scale - MinScale) / (MaxScale - MinScale);

    private void StopSweepAnimation(bool stopCryMotion = false)
    {
        SweeperRotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
        SweeperScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        SweeperScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        SweeperTranslateTransform.BeginAnimation(TranslateTransform.XProperty, null);
        SweeperTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
        CryLayer.BeginAnimation(OpacityProperty, null);
        if (stopCryMotion)
        {
            StopCryMotionAnimations();
        }

        _animationStarted = false;
    }

    private void ResetPetRigPose()
    {
        PetRigRotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
        PetRigTranslateTransform.BeginAnimation(TranslateTransform.XProperty, null);
        PetRigTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
        PetRigRotateTransform.Angle = 0;
        PetRigTranslateTransform.X = 0;
        PetRigTranslateTransform.Y = 0;
    }

    private void ApplyFailurePose()
    {
        PetRigRotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
        PetRigTranslateTransform.BeginAnimation(TranslateTransform.XProperty, null);
        PetRigTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
        StartFailureMotionAnimation(PetRigRotateTransform, RotateTransform.AngleProperty, new[]
        {
            (0, 0.0),
            (160, 10.0),
            (380, 42.0),
            (620, 72.0),
            (760, 55.0),
            (1_080, 68.0),
            (1_420, 49.0),
            (1_820, 66.0),
            (2_220, 50.0),
            (2_700, 67.0),
            (3_180, 51.0),
            (3_720, 65.0),
            (4_260, 52.0),
            (4_820, 62.0),
            (5_000, 58.0),
        });
        StartFailureMotionAnimation(PetRigTranslateTransform, TranslateTransform.XProperty, new[]
        {
            (0, 0.0),
            (160, 1.0),
            (380, 5.0),
            (620, 18.0),
            (760, 6.0),
            (1_080, 24.0),
            (1_420, -1.0),
            (1_820, 23.0),
            (2_220, 0.0),
            (2_700, 24.0),
            (3_180, 1.0),
            (3_720, 22.0),
            (4_260, 2.0),
            (4_820, 18.0),
            (5_000, 8.0),
        });
        StartFailureMotionAnimation(PetRigTranslateTransform, TranslateTransform.YProperty, new[]
        {
            (0, 0.0),
            (160, 2.0),
            (380, 12.0),
            (620, 33.0),
            (760, 24.0),
            (1_080, 31.0),
            (1_420, 21.0),
            (1_820, 32.0),
            (2_220, 21.0),
            (2_700, 32.0),
            (3_180, 22.0),
            (3_720, 31.0),
            (4_260, 23.0),
            (4_820, 30.0),
            (5_000, 25.0),
        });
    }

    private static void StartFailureMotionAnimation(
        Animatable target,
        DependencyProperty property,
        (int Milliseconds, double Value)[] keys)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(keys[^1].Milliseconds),
            FillBehavior = FillBehavior.HoldEnd
        };

        foreach (var key in keys)
        {
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(
                key.Value,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(key.Milliseconds)),
                new KeySpline(0.2, 0.0, 0.18, 1.0)));
        }

        target.BeginAnimation(property, animation);
    }

    private void StartCryMotionAnimations(bool hardCry)
    {
        StartCryStreamAnimation(CryLeftStream, delayMs: 0, durationMs: hardCry ? 760 : 1_000, minOpacity: hardCry ? 0.92 : 0.74, maxOpacity: 1.0);
        StartCryStreamAnimation(CryRightStream, delayMs: 120, durationMs: hardCry ? 820 : 1_000, minOpacity: hardCry ? 0.9 : 0.7, maxOpacity: 1.0);
        StartCryDropAnimation(CryLeftDropA, CryLeftDropATransform, delayMs: 0, durationMs: hardCry ? 820 : 1_120, fallDistance: hardCry ? 30 : 18, driftDistance: hardCry ? -0.45 : -0.15);
        StartCryDropAnimation(CryRightDropA, CryRightDropATransform, delayMs: 180, durationMs: hardCry ? 860 : 1_120, fallDistance: hardCry ? 30 : 18, driftDistance: hardCry ? 0.45 : 0.15);

        if (hardCry)
        {
            StartCryDropAnimation(HardCryLeftDrop, HardCryLeftDropTransform, delayMs: 320, durationMs: 900, fallDistance: 35, driftDistance: -0.9);
            StartCryDropAnimation(HardCryRightDrop, HardCryRightDropTransform, delayMs: 470, durationMs: 940, fallDistance: 36, driftDistance: 0.9);
        }
        else
        {
            StopCryDropAnimation(HardCryLeftDrop, HardCryLeftDropTransform);
            StopCryDropAnimation(HardCryRightDrop, HardCryRightDropTransform);
        }
    }

    private static void StartCryStreamAnimation(UIElement stream, int delayMs, int durationMs, double minOpacity, double maxOpacity)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = TimeSpan.FromMilliseconds(durationMs),
            RepeatBehavior = RepeatBehavior.Forever
        };

        animation.KeyFrames.Add(new SplineDoubleKeyFrame(minOpacity, KeyTime.FromPercent(0), new KeySpline(0.32, 0.0, 0.24, 1.0)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(maxOpacity, KeyTime.FromPercent(0.42), new KeySpline(0.32, 0.0, 0.24, 1.0)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(minOpacity, KeyTime.FromPercent(1), new KeySpline(0.32, 0.0, 0.24, 1.0)));
        stream.BeginAnimation(OpacityProperty, animation);
    }

    private static void StartCryDropAnimation(
        UIElement drop,
        TranslateTransform transform,
        int delayMs,
        int durationMs,
        double fallDistance,
        double driftDistance)
    {
        var opacityAnimation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = TimeSpan.FromMilliseconds(durationMs),
            RepeatBehavior = RepeatBehavior.Forever
        };
        opacityAnimation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        opacityAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(1, KeyTime.FromPercent(0.18), new KeySpline(0.16, 0.0, 0.22, 1.0)));
        opacityAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(1, KeyTime.FromPercent(0.72), new KeySpline(0.16, 0.0, 0.22, 1.0)));
        opacityAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromPercent(1), new KeySpline(0.58, 0.0, 1.0, 1.0)));
        drop.BeginAnimation(OpacityProperty, opacityAnimation);

        var fallAnimation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = TimeSpan.FromMilliseconds(durationMs),
            RepeatBehavior = RepeatBehavior.Forever
        };
        fallAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromPercent(0), new KeySpline(0.2, 0.0, 0.18, 1.0)));
        fallAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(fallDistance, KeyTime.FromPercent(1), new KeySpline(0.22, 0.0, 0.34, 1.0)));
        transform.BeginAnimation(TranslateTransform.YProperty, fallAnimation);

        var driftAnimation = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            Duration = TimeSpan.FromMilliseconds(durationMs),
            RepeatBehavior = RepeatBehavior.Forever
        };
        driftAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromPercent(0), new KeySpline(0.2, 0.0, 0.18, 1.0)));
        driftAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(driftDistance, KeyTime.FromPercent(1), new KeySpline(0.22, 0.0, 0.34, 1.0)));
        transform.BeginAnimation(TranslateTransform.XProperty, driftAnimation);
    }

    private void StopCryMotionAnimations()
    {
        CryLeftStream.BeginAnimation(OpacityProperty, null);
        CryRightStream.BeginAnimation(OpacityProperty, null);
        StopCryDropAnimation(CryLeftDropA, CryLeftDropATransform);
        StopCryDropAnimation(CryRightDropA, CryRightDropATransform);
        StopCryDropAnimation(HardCryLeftDrop, HardCryLeftDropTransform);
        StopCryDropAnimation(HardCryRightDrop, HardCryRightDropTransform);
    }

    private static void StopCryDropAnimation(UIElement drop, TranslateTransform transform)
    {
        drop.BeginAnimation(OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        drop.Opacity = 0;
        transform.X = 0;
        transform.Y = 0;
    }

    private static double WorkPhaseEnvelope(int elapsedMs)
    {
        const double easeWindowMs = 900;
        var fadeIn = Math.Min(1, elapsedMs / easeWindowMs);
        var fadeOut = Math.Min(1, (WorkDurationMs - elapsedMs) / easeWindowMs);
        return Math.Clamp(Math.Min(fadeIn, fadeOut), 0, 1);
    }

    private static double LoadScale()
    {
        try
        {
            if (!File.Exists(ScalePath))
            {
                return DefaultScale;
            }

            var raw = File.ReadAllText(ScalePath).Trim();
            return double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale)
                ? Math.Clamp(scale, MinScale, MaxScale)
                : DefaultScale;
        }
        catch
        {
            return DefaultScale;
        }
    }

    private static void SaveScale(double scale)
    {
        try
        {
            AppPaths.EnsureCreated();
            File.WriteAllText(ScalePath, scale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch
        {
            // Assistant size preferences should never break update work.
        }
    }

    private static double Lerp(double from, double to, double amount)
    {
        return from + (to - from) * amount;
    }

    protected override void OnClosed(EventArgs e)
    {
        StopSweepAnimation(stopCryMotion: true);
        FailureLayer.BeginAnimation(OpacityProperty, null);
        base.OnClosed(e);
    }
}
