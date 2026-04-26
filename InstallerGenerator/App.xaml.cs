using InstallerGenerator.Constants;
using InstallerGenerator.Services;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.Resources;

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
                    AppConstants.FileExtensions.Package.Contains(
                        System.IO.Path.GetExtension(f.Name).ToLowerInvariant()));

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
}