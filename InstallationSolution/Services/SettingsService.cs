using InstallationSolution.Constants;
using static InstallationSolution.Constants.AppConstants;
using Windows.Storage;

namespace InstallationSolution.Services;

/// <summary>
/// 统一管理应用设置的读写，避免各处直接操作 LocalSettings
/// </summary>
public static class SettingsService
{
    private static ApplicationDataContainer Store => ApplicationData.Current.LocalSettings;

    public static string  Theme        => Store.Values[Settings.Theme]        as string ?? "System";
    public static string  Material     => Store.Values[Settings.Material]     as string ?? "Mica";
    public static string  PanePosition => Store.Values[Settings.PanePosition] as string ?? "Left";
    public static bool    Sound        => Store.Values[Settings.Sound]        is bool b ? b : true;
    public static string? LastOutput   => Store.Values[Settings.LastOutput]   as string;

    public static void SetTheme(string value)        => Store.Values[Settings.Theme]        = value;
    public static void SetMaterial(string value)     => Store.Values[Settings.Material]     = value;
    public static void SetPanePosition(string value) => Store.Values[Settings.PanePosition] = value;
    public static void SetSound(bool value)          => Store.Values[Settings.Sound]        = value;
    public static void SetLastOutput(string value)   => Store.Values[Settings.LastOutput]   = value;
}
