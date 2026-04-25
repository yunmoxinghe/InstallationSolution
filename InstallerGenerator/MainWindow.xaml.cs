using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.System;
using WinRT.Interop;
using InstallerGenerator.Dialogs;
using InstallerGenerator.Pages;

namespace InstallerGenerator
{
    public sealed partial class MainWindow : Window
    {
        private readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;
        private readonly AppWindow _appWindow;
        private readonly IntPtr _hwnd;
        
        public static MainWindow? Instance { get; private set; }

        /// <summary>
        /// 公共打开链接弹出对话框方法
        /// </summary>
        public async void OpenExternalLink(object sender, RoutedEventArgs e)
        {
            var root = (ContentFrame.Content as FrameworkElement)?.XamlRoot;
            if (root == null)
                return;

            string? url = null;
            
            if (sender is Button btn && btn.Tag is string btnUrl)
                url = btnUrl;
            else if (sender is HyperlinkButton link && link.Tag is string linkUrl)
                url = linkUrl;

            if (string.IsNullOrEmpty(url))
                return;

            var dialog = new ExternalOpenDialog { XamlRoot = root };
            var result = await dialog.ShowAsync();

            // Secondary 按钮表示"是，打开"
            if (result == ContentDialogResult.Secondary)
            {
                try
                {
                    await Launcher.LaunchUriAsync(new Uri(url));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to launch URL: {ex.Message}");
                }
            }
        }

        public MainWindow()
        {
            this.InitializeComponent();
            Instance = this;

            if (Content is FrameworkElement root)
                root.RequestedTheme = AppThemeManager.CurrentTheme;

            // ✅ 获取 AppWindow 实例和窗口句柄
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _appWindow = GetAppWindowForCurrentWindow();

            // ✅ 使用官方推荐方式设置窗口图标（立即生效）
            _appWindow.SetIcon("Assets/AppIcon.ico");

            this.SetTitleBar(TitleBarArea);

            NavView.SelectedItem = NavView.MenuItems[0];
            NavigateByTag("home");
            ContentFrame.Navigated += ContentFrame_Navigated;

            this.Activated += MainWindow_Activated;
            this.Closed += MainWindow_Closed;

            if (Content is FrameworkElement rootEl)
                rootEl.Loaded += Root_Loaded;
        }

        // ── 获取 AppWindow 实例 ──────────────────────────────────────
        private AppWindow GetAppWindowForCurrentWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            return AppWindow.GetFromWindowId(wndId);
        }

        // ── 导航核心：tag → 页面类型映射 ──
        private static readonly System.Collections.Generic.Dictionary<string, Type> _pageTypeMap = new()
        {
            { "home", typeof(HomePage) },
            { "settings", typeof(SettingsPage) }
        };

        private static Type? TagToPageType(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) 
                return null;
            
            _pageTypeMap.TryGetValue(tag.ToLowerInvariant(), out var pageType);
            return pageType;
        }

        private void NavigateByTag(string tag)
        {
            var pageType = TagToPageType(tag);
            if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
                ContentFrame.Navigate(pageType);
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                if (ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
                    ContentFrame.Navigate(typeof(SettingsPage));
                return;
            }

            string? tag = args.InvokedItemContainer?.Tag?.ToString();
            if (!string.IsNullOrEmpty(tag))
                NavigateByTag(tag);
        }

        private void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (ContentFrame.CanGoBack) ContentFrame.GoBack();
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            UpdateBackButton();
            UpdateSelectedNavItem(e.SourcePageType);
        }

        private void UpdateBackButton() =>
            NavView.IsBackEnabled = ContentFrame.CanGoBack;

        // 反向查找：当前页类型 → 找到对应 Tag 的 NavItem 并选中
        private void UpdateSelectedNavItem(Type pageType)
        {
            if (pageType == typeof(SettingsPage))
            {
                NavView.SelectedItem = NavView.SettingsItem;
                return;
            }

            string? pageName = pageType?.Name;
            if (pageName == null) return;

            foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
            {
                string? tag = item.Tag?.ToString();
                if (!string.IsNullOrEmpty(tag) && TagToPageType(tag) == pageType)
                {
                    NavView.SelectedItem = item;
                    return;
                }
            }
        }

        // ── 失焦标题文字变灰 ────────────────────────────────────────
        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            bool isActive = args.WindowActivationState != WindowActivationState.Deactivated;
            double opacity = isActive ? 1.0 : 0.5;
            TitleBarAppName.Opacity = opacity;
            ImgAppIcon.Opacity = opacity;
        }

        // ── 窗口关闭清理 ────────────────────────────────────────────
        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            // 清理事件订阅
            if (Content is FrameworkElement root)
            {
                root.ActualThemeChanged -= AppThemeManager.OnActualThemeChanged;
            }

            Instance = null;
        }

        // ── 标题栏图标：根据 DPI scale 选择最佳资源 ─────────────────
        private void UpdateTitleBarIcon()
        {
            double scale = (Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1.0;
            int scalePercent = scale switch
            {
                >= 4.0 => 400,
                >= 2.0 => 200,
                >= 1.5 => 150,
                >= 1.25 => 125,
                _ => 100
            };
            ImgAppIcon.Source = new BitmapImage(
                new Uri($"ms-appx:///Assets/Square44x44Logo.scale-{scalePercent}.png"));
        }

        // ── Splash ───────────────────────────────────────────────────
        public async void ShowSplash()
        {
            // 显示 Splash
            SplashOverlay.Visibility = Visibility.Visible;
            SplashOverlay.Opacity = 1;

            await Task.Delay(1500);

            SplashFadeOut.Completed += (s, e) =>
            {
                SplashOverlay.Visibility = Visibility.Collapsed;

                bool sound = _localSettings.Values["EnableSound"] is bool b ? b : true;
                ElementSoundPlayer.State = sound
                    ? ElementSoundPlayerState.On
                    : ElementSoundPlayerState.Off;
            };

            SplashFadeOut.Begin();
        }

        private void Root_Loaded(object sender, RoutedEventArgs e)
        {
            TitleBarAppName.Text = Package.Current.DisplayName;
            UpdateTitleBarIcon();

            ApplySettings();
            UpdateBackButton();

            if (Content is FrameworkElement root)
            {
                root.ActualThemeChanged -= AppThemeManager.OnActualThemeChanged;
                root.ActualThemeChanged += AppThemeManager.OnActualThemeChanged;
            }
        }

        public void ApplySettings()
        {
            try
            {
                string position = _localSettings.Values["PanePosition"] as string ?? "Left";
                if (_localSettings.Values["PanePosition"] == null)
                    _localSettings.Values["PanePosition"] = "Left";

                NavView.PaneDisplayMode = position == "Top"
                    ? NavigationViewPaneDisplayMode.Top
                    : NavigationViewPaneDisplayMode.Left;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplySettings Error: {ex.Message}");
            }
        }
    }
}