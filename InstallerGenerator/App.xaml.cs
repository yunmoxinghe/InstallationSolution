using InstallerGenerator.Constants;
using InstallerGenerator.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.Resources;
using Windows.UI;

namespace InstallerGenerator
{
    public partial class App : Application
    {
        public static Window? MainWindow { get; private set; }

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // ── 注册通知管理器 ───────────────────────────────────────
            var notifManager = AppNotificationManager.Default;
            notifManager.NotificationInvoked += OnNotificationInvoked;
            notifManager.Register();
            // ────────────────────────────────────────────────────────

            // ── 单实例 ──────────────────────────────────────────────
            var mainInstance = AppInstance.FindOrRegisterForKey("main-instance");

            if (!mainInstance.IsCurrent)
            {
                // 使用 Task.Run 避免阻塞主线程
                var redirectTask = mainInstance.RedirectActivationToAsync(
                    AppInstance.GetCurrent().GetActivatedEventArgs()
                );
                // 等待重定向完成后退出
                redirectTask.AsTask().Wait(TimeSpan.FromSeconds(5));
                Environment.Exit(0);
                return;
            }

            mainInstance.Activated += (_, activatedArgs) =>
            {
                // 在回调线程上立即提取路径，避免跨线程访问 WinRT 对象
                string? filePath = ExtractFilePathFromActivation(activatedArgs);

                if (MainWindow != null)
                {
                    MainWindow.DispatcherQueue.TryEnqueue(() =>
                    {
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
                        BringWindowToFront(hwnd);
                        if (filePath != null && MainWindow is InstallerGenerator.MainWindow mw)
                            mw.LoadFileWhenReady(filePath);
                    });
                }
            };
            // ────────────────────────────────────────────────────────

            AppThemeManager.LoadSettings();

            MainWindow = new MainWindow();

            // ✅ SetupTitleBar 只操作 AppWindow.TitleBar，不依赖 XAML 资源
            // 必须在 Activate() 之前调用，否则窗口弹出瞬间会闪原生标题栏
            AppThemeManager.SetupTitleBar();

            // ✅ 立刻激活窗口，Splash 遮罩第一帧就可见，用户感知秒开
            MainWindow.Activate();

            // ✅ 其余初始化全部延后到下一帧，Splash 完全遮住，用户无感知
            MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                try { MainWindow.AppWindow.SetIcon("Assets/AppIcon.ico"); } catch { }
                AppThemeManager.ApplyMaterial();

                var loader = new ResourceLoader();
                MainWindow.AppWindow.Title = loader.GetString("AppTitle");

                ((MainWindow)MainWindow).ShowSplash();

                // 处理启动时的文件激活
                TryLoadFileFromActivation(AppInstance.GetCurrent().GetActivatedEventArgs());
            });
        }

        // ── 从激活参数中提取文件路径（可在任意线程调用）────────────
        private static string? ExtractFilePathFromActivation(AppActivationArguments activationArgs)
        {
            try
            {
                if (activationArgs.Kind != ExtendedActivationKind.File) return null;
                if (activationArgs.Data is not Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs) return null;

                var file = fileArgs.Files.OfType<Windows.Storage.StorageFile>().FirstOrDefault(f =>
                {
                    var ext = System.IO.Path.GetExtension(f.Name).ToLowerInvariant();
                    return ext is ".msix" or ".msixbundle" or ".appx" or ".appxbundle";
                });

                return file?.Path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ExtractFilePathFromActivation failed: {ex.Message}");
                return null;
            }
        }

        // ── 在 UI 线程上加载文件（OnLaunched 时使用）────────────────
        private static void TryLoadFileFromActivation(AppActivationArguments activationArgs)
        {
            var filePath = ExtractFilePathFromActivation(activationArgs);
            if (filePath == null) return;
            if (MainWindow is InstallerGenerator.MainWindow mw)
                mw.LoadFileWhenReady(filePath);
        }

        // ── 通知激活处理 ─────────────────────────────────────────────
        private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
        {
            if (args.Arguments.TryGetValue("action", out var action) && action == "openFolder"
                && args.Arguments.TryGetValue("path", out var folderPath)
                && !string.IsNullOrEmpty(folderPath))
            {
                MainWindow?.DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"\"{folderPath}\"");
                        // 把窗口带到前台
                        if (MainWindow != null)
                        {
                            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
                            BringWindowToFront(hwnd);
                        }
                    }
                    catch { }
                });
            }
        }

        // ── Win32：把窗口拉到前台 ────────────────────────────────────
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

        static void BringWindowToFront(IntPtr hwnd)
        {
            if (IsIconic(hwnd)) ShowWindow(hwnd, AppConstants.Win32.SW_RESTORE);
            SetForegroundWindow(hwnd);
        }
    }

    public static class AppThemeManager
    {
        public static ElementTheme CurrentTheme = ElementTheme.Default;
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
                    if (CurrentMaterial == BackgroundMaterial.Mica && mica.Kind == MicaKind.Base) return;
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
                    _ => new MicaBackdrop { Kind = MicaKind.Base }
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
                titleBar.ExtendsContentIntoTitleBar = true;
                titleBar.ButtonBackgroundColor = Colors.Transparent;
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
                bool isDark = GetIsDarkTheme();

                var fg = isDark ? Colors.White : Colors.Black;
                var inactiveFg = isDark
                    ? Color.FromArgb(255, 128, 128, 128)
                    : Color.FromArgb(255, 160, 160, 160);
                var hoverBg = isDark
                    ? Color.FromArgb(20, 255, 255, 255)
                    : Color.FromArgb(20, 0, 0, 0);

                titleBar.ButtonForegroundColor = fg;
                titleBar.ButtonInactiveForegroundColor = inactiveFg;
                titleBar.ButtonHoverBackgroundColor = hoverBg;
                titleBar.ButtonHoverForegroundColor = fg;
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(30, hoverBg.R, hoverBg.G, hoverBg.B);
                titleBar.ButtonPressedForegroundColor = fg;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateTitleBarColors failed: {ex.Message}");
            }
        }

        public static void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            UpdateTitleBarColors();
        }

        public static bool GetIsDarkTheme()
        {
            if (App.MainWindow?.Content is FrameworkElement root)
            {
                var actual = root.ActualTheme;
                if (actual != ElementTheme.Default)
                    return actual == ElementTheme.Dark;
            }
            if (CurrentTheme == ElementTheme.Default)
                return Application.Current.RequestedTheme == ApplicationTheme.Dark;
            return CurrentTheme == ElementTheme.Dark;
        }
    }

    public enum BackgroundMaterial
    {
        Mica,
        MicaAlt,
        Acrylic
    }
}