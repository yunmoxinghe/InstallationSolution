using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.System;
using WinRT.Interop;
using InstallationSolution.Pages;
using InstallationSolution.Dialogs;

namespace InstallationSolution
{
    public sealed partial class MainWindow : Window
    {
        private const uint WM_GETMINMAXINFO = 0x0024;
        private static SUBCLASSPROC? _subclassProc;
        private readonly AppWindow _appWindow;
        private readonly IntPtr _hwnd;

        public static MainWindow? Instance { get; private set; }

        public MainWindow()
        {
            this.InitializeComponent();

            Instance = this;

            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _appWindow = GetAppWindowForCurrentWindow();

            try
            {
                _appWindow.SetIcon("Assets/AppIcon.ico");
                TitleBarAppName.Text = Package.Current.DisplayName;
            }
            catch { }

            this.SetTitleBar(TitleBarArea);

            this.Activated += MainWindow_Activated;
            this.Closed += MainWindow_Closed;

            // 默认窗口尺寸对齐 APK-Installer：652×414，DPI 感知
            SetWindowSize(_hwnd, 652, 414);
            SetMinWindowSize(_hwnd, minWidth: 652, minHeight: 414);
        }

        private AppWindow GetAppWindowForCurrentWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            Microsoft.UI.WindowId wndId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            return AppWindow.GetFromWindowId(wndId);
        }

        private static void SetWindowSize(IntPtr hwnd, int width, int height)
        {
            uint dpi = GetDpiForWindow(hwnd);
            float scale = (float)dpi / 96;
            AppWindow.GetFromWindowId(
                Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd))
                .Resize(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(height * scale)));
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            bool isActive = args.WindowActivationState != WindowActivationState.Deactivated;
            TitleBarAppName.Opacity = isActive ? 1.0 : 0.5;
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (_subclassProc != null)
            {
                RemoveWindowSubclass(_hwnd, _subclassProc, 0);
                _subclassProc = null;
            }
        }

        public async void ShowSplash()
        {
            SplashOverlay.Visibility = Visibility.Visible;
            SplashOverlay.Opacity = 1;

            await Task.Delay(100);
            SplashFadeIn.Begin();

            await Task.Delay(1500);

            var tcs = new TaskCompletionSource<bool>();
            SplashFadeOut.Completed += (s, e) => tcs.SetResult(true);
            SplashFadeOut.Begin();
            await tcs.Task;

            SplashOverlay.Visibility = Visibility.Collapsed;
            await Task.Delay(16);

            ContentFrame.Navigate(typeof(HomePage));

            // UI 已就绪，关闭 Guard 发出的加载中 Toast
            DismissGuardToast();
        }

        public void NavigateToGenerator()
        {
            // 导航到生成器主界面
            ContentFrame.Navigate(typeof(GeneratorPage));
        }

        public void LoadFileToGenerator(string filePath)
        {
            // 导航到生成器并加载文件
            ContentFrame.Navigate(typeof(GeneratorPage));
            
            // 等待页面加载完成后设置文件
            ContentFrame.DispatcherQueue.TryEnqueue(() =>
            {
                if (ContentFrame.Content is GeneratorPage page)
                {
                    page.LoadFile(filePath);
                }
            });
        }

        private static void DismissGuardToast()
        {
            try
            {
                var appId  = Environment.GetEnvironmentVariable("GUARD_TOAST_APPID");
                var tag    = Environment.GetEnvironmentVariable("GUARD_TOAST_TAG");
                var group  = Environment.GetEnvironmentVariable("GUARD_TOAST_GROUP");
                if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(tag)) return;

                var script = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.UI.Notifications.ToastNotificationManager]::History.Remove('{tag}', '{group}', '{appId}')
";
                var ps1 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dismiss_{Guid.NewGuid():N}.ps1");
                try
                {
                    System.IO.File.WriteAllText(ps1, script, System.Text.Encoding.UTF8);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName        = "powershell.exe",
                        Arguments       = $"-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{ps1}\"",
                        UseShellExecute = false,
                        CreateNoWindow  = true,
                    });
                    // 不等待，fire-and-forget
                }
                finally
                {
                    // 延迟删除 ps1（进程还在跑）
                    Task.Delay(6000).ContinueWith(_ => { try { System.IO.File.Delete(ps1); } catch { } });
                }
            }
            catch { }
        }

        // ── 最小尺寸：Win32 Subclass ─────────────────────────────────
        static int _minW, _minH;

        static void SetMinWindowSize(IntPtr hwnd, int minWidth, int minHeight)
        {
            _minW = minWidth;
            _minH = minHeight;
            _subclassProc = SubclassProc;
            SetWindowSubclass(hwnd, _subclassProc, 0, 0);
        }

        static nuint SubclassProc(IntPtr hWnd, uint uMsg, nuint wParam, nint lParam,
                                   nuint uIdSubclass, nuint dwRefData)
        {
            if (uMsg == WM_GETMINMAXINFO)
            {
                double dpi = GetDpiForWindow(hWnd) / 96.0;
                var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                info.ptMinTrackSize.x = (int)(_minW * dpi);
                info.ptMinTrackSize.y = (int)(_minH * dpi);
                Marshal.StructureToPtr(info, lParam, true);
            }
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        delegate nuint SUBCLASSPROC(IntPtr hWnd, uint uMsg, nuint wParam, nint lParam,
                                     nuint uIdSubclass, nuint dwRefData);

        [DllImport("comctl32.dll")]
        static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass,
                                              nuint uIdSubclass, nuint dwRefData);
        [DllImport("comctl32.dll")]
        static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass);
        [DllImport("comctl32.dll")]
        static extern nuint DefSubclassProc(IntPtr hWnd, uint uMsg, nuint wParam, nint lParam);
        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        struct MINMAXINFO
        {
            public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x, y; }
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 应用设置（例如导航栏位置等）
        /// </summary>
        public void ApplySettings()
        {
            // 这个方法用于应用设置变更
            // 目前InstallerUI项目中主要用于导航栏位置设置
            // 由于当前MainWindow没有导航栏，这个方法可以为空或用于其他设置
        }

        /// <summary>
        /// 打开外部链接（带确认对话框）
        /// </summary>
        public async void OpenExternalLink(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element) return;
            if (element.Tag is not string url || string.IsNullOrEmpty(url)) return;

            try
            {
                var dialog = new ExternalOpenDialog
                {
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Secondary)
                {
                    await Launcher.LaunchUriAsync(new Uri(url));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OpenExternalLink error: {ex.Message}");
            }
        }
    }
}