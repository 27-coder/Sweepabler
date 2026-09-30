using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace ProperAppUpdater.Services;

public static class SystemThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static void Apply(ResourceDictionary resources)
    {
        if (IsDarkMode())
        {
            ApplyDark(resources);
            return;
        }

        ApplyLight(resources);
    }

    private static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyLight(ResourceDictionary resources)
    {
        Set(resources, "WindowBackgroundBrush", "#F8FAFC");
        Set(resources, "WindowBorderBrush", "#0F172A");
        Set(resources, "PanelBrush", "#FFFFFF");
        Set(resources, "InkBrush", "#111827");
        Set(resources, "MutedBrush", "#64748B");
        Set(resources, "LineBrush", "#D5DEE8");
        Set(resources, "ButtonBackgroundBrush", "#FFFFFF");
        Set(resources, "ButtonBorderBrush", "#CBD5E1");
        Set(resources, "PrimaryBrush", "#176B87");
        Set(resources, "PrimaryForegroundBrush", "#FFFFFF");
        Set(resources, "BadgeBackgroundBrush", "#E8F4F7");
        Set(resources, "BadgeBorderBrush", "#B7D7DF");
        Set(resources, "BadgeForegroundBrush", "#176B87");
        Set(resources, "SelectionBrush", "#E8F4F7");
        Set(resources, "FamilyBackgroundBrush", "#DDF3EE");
        Set(resources, "FamilyHeaderBrush", "#BEE8DC");
        Set(resources, "FamilyBorderBrush", "#77C7B3");
        Set(resources, "FamilyForegroundBrush", "#0F5F54");
    }

    private static void ApplyDark(ResourceDictionary resources)
    {
        Set(resources, "WindowBackgroundBrush", "#111827");
        Set(resources, "WindowBorderBrush", "#334155");
        Set(resources, "PanelBrush", "#182235");
        Set(resources, "InkBrush", "#F8FAFC");
        Set(resources, "MutedBrush", "#94A3B8");
        Set(resources, "LineBrush", "#334155");
        Set(resources, "ButtonBackgroundBrush", "#1F2937");
        Set(resources, "ButtonBorderBrush", "#475569");
        Set(resources, "PrimaryBrush", "#2A9DB8");
        Set(resources, "PrimaryForegroundBrush", "#FFFFFF");
        Set(resources, "BadgeBackgroundBrush", "#12323D");
        Set(resources, "BadgeBorderBrush", "#2A6475");
        Set(resources, "BadgeForegroundBrush", "#7DD3FC");
        Set(resources, "SelectionBrush", "#223347");
        Set(resources, "FamilyBackgroundBrush", "#123C39");
        Set(resources, "FamilyHeaderBrush", "#15564F");
        Set(resources, "FamilyBorderBrush", "#2A8B7C");
        Set(resources, "FamilyForegroundBrush", "#A7F3D0");
    }

    private static void Set(ResourceDictionary resources, string key, string color)
    {
        resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }
}
