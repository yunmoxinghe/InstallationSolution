using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using InstallerGenerator.Services;
using System;
using System.Diagnostics;
using Windows.UI;

namespace InstallerGenerator;

public enum BackgroundMaterial { Mica, MicaAlt, Acrylic }

public static class AppThemeManager
{
    public static ElementTheme      CurrentTheme    = ElementTheme.Default;
    public static BackgroundMaterial CurrentMaterial = BackgroundMaterial.Mica;

    public static void LoadSettings()
    {
        try
        {
            CurrentTheme = SettingsService.Theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark"  => ElementTheme.Dark,
                _       => ElementTheme.Default
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadSettings - Theme error: {ex.Message}");
            CurrentTheme = ElementTheme.Default;
        }

        try
        {
            CurrentMaterial = SettingsService.Material switch
            {
                "MicaAlt" => BackgroundMaterial.MicaAlt,
                "Acrylic" => BackgroundMaterial.Acrylic,
                _         => BackgroundMaterial.Mica
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadSettings - Material error: {ex.Message}");
            CurrentMaterial = BackgroundMaterial.Mica;
        }

        try
        {
            ElementSoundPlayer.State = SettingsService.Sound
                ? ElementSoundPlayerState.On
                : ElementSoundPlayerState.Off;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadSettings - Sound error: {ex.Message}");
            ElementSoundPlayer.State = ElementSoundPlayerState.On;
        }
    }

    public static void ApplyMaterial()
    {
        if (App.MainWindow == null) return;
        try
        {
            if (App.MainWindow.SystemBackdrop is MicaBackdrop mica)
            {
                if (CurrentMaterial == BackgroundMaterial.Mica    && mica.Kind == MicaKind.Base)    return;
                if (CurrentMaterial == BackgroundMaterial.MicaAlt && mica.Kind == MicaKind.BaseAlt) return;
            }
            else if (App.MainWindow.SystemBackdrop is DesktopAcrylicBackdrop &&
                     CurrentMaterial == BackgroundMaterial.Acrylic)
            {
                return;
            }

            App.MainWindow.SystemBackdrop = CurrentMaterial switch
            {
                BackgroundMaterial.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
                BackgroundMaterial.Acrylic => new DesktopAcrylicBackdrop(),
                _                          => new MicaBackdrop { Kind = MicaKind.Base }
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ApplyMaterial failed: {ex.Message}");
            App.MainWindow.SystemBackdrop = null;
        }
    }

    public static void SetupTitleBar()
    {
        if (App.MainWindow == null) return;
        try
        {
            if (!AppWindowTitleBar.IsCustomizationSupported()) return;
            var titleBar = App.MainWindow.AppWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar    = true;
            titleBar.ButtonBackgroundColor         = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            UpdateTitleBarColors();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetupTitleBar failed: {ex.Message}");
        }
    }

    public static void UpdateTitleBarColors()
    {
        if (App.MainWindow == null) return;
        try
        {
            if (!AppWindowTitleBar.IsCustomizationSupported()) return;
            var titleBar = App.MainWindow.AppWindow.TitleBar;
            bool isDark  = GetIsDarkTheme();

            var fg         = isDark ? Colors.White : Colors.Black;
            var inactiveFg = isDark
                ? Color.FromArgb(255, 128, 128, 128)
                : Color.FromArgb(255, 160, 160, 160);
            var hoverBg = isDark
                ? Color.FromArgb(20, 255, 255, 255)
                : Color.FromArgb(20, 0, 0, 0);

            titleBar.ButtonForegroundColor         = fg;
            titleBar.ButtonInactiveForegroundColor = inactiveFg;
            titleBar.ButtonHoverBackgroundColor    = hoverBg;
            titleBar.ButtonHoverForegroundColor    = fg;
            titleBar.ButtonPressedBackgroundColor  = Color.FromArgb(30, hoverBg.R, hoverBg.G, hoverBg.B);
            titleBar.ButtonPressedForegroundColor  = fg;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UpdateTitleBarColors failed: {ex.Message}");
        }
    }

    public static void OnActualThemeChanged(FrameworkElement sender, object args)
        => UpdateTitleBarColors();

    public static bool GetIsDarkTheme()
    {
        if (App.MainWindow?.Content is FrameworkElement root)
        {
            var actual = root.ActualTheme;
            if (actual != ElementTheme.Default)
                return actual == ElementTheme.Dark;
        }
        return CurrentTheme == ElementTheme.Default
            ? Application.Current.RequestedTheme == ApplicationTheme.Dark
            : CurrentTheme == ElementTheme.Dark;
    }
}
