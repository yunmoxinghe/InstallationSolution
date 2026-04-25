using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Resources;
using Windows.UI;
using AppInstance = Microsoft.Windows.AppLifecycle.AppInstance;

namespace InstallerUI
{
    public partial class App : Application
    {
        public static Window? MainWindow { get; private set; }

        /// <summary>Guard 传入的 msix 路径（命令行第一个参数）</summary>
        public static string? MsixPath { get; private set; }

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // ── 读取命令行参数（Guard 传入的 msix 路径）──────────────
            var cmdArgs = Environment.GetCommandLineArgs();
            if (cmdArgs.Length > 1)
                MsixPath = cmdArgs[1];

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

            mainInstance.Activated += (_, _) =>
            {
                if (MainWindow != null)
                {
                    MainWindow.DispatcherQueue.TryEnqueue(() =>
                    {
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
                        BringWindowToFront(hwnd);
                    });
                }
            };
            // ────────────────────────────────────────────────────────

            MainWindow = new MainWindow();

            // 设置标题栏
            AppThemeManager.SetupTitleBar();

            // 立刻激活窗口
            MainWindow.Activate();

            // 延后初始化
            MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                try 
                { 
                    MainWindow.AppWindow.SetIcon("Assets/AppIcon.ico"); 
                } 
                catch { }

                // 设置窗口标题
                try
                {
                    var loader = new ResourceLoader();
                    MainWindow.AppWindow.Title = loader.GetString("AppTitle");
                }
                catch
                {
                    // 如果资源加载失败，使用默认标题
                    try
                    {
                        MainWindow.AppWindow.Title = Package.Current.DisplayName;
                    }
                    catch
                    {
                        MainWindow.AppWindow.Title = "Installer";
                    }
                }

                ((MainWindow)MainWindow).ShowSplash();
            });
        }

        // ── Win32：把窗口拉到前台 ────────────────────────────────────
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

        static void BringWindowToFront(IntPtr hwnd)
        {
            if (IsIconic(hwnd)) ShowWindow(hwnd, 9); // SW_RESTORE
            SetForegroundWindow(hwnd);
        }
    }

    public static class AppThemeManager
    {
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
            }
            catch { }
        }
    }
}
