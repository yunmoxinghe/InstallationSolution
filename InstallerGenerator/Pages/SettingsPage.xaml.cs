using InstallerGenerator.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using InstallerGenerator.Dialogs;
using Windows.ApplicationModel;
using Windows.System;

namespace InstallerGenerator.Pages
{
    public sealed partial class SettingsPage : Page
    {
        private bool _isInitializing = true;

        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadUI();
            LoadAppInfo();
            _isInitializing = false;
            // ActualThemeChanged 已在 MainWindow.Root_Loaded 统一注册，此处无需重复
        }

        private void LoadUI()
        {
            RbTheme.SelectedIndex = SettingsService.Theme switch
            {
                "Light" => 1,
                "Dark"  => 2,
                _       => 0
            };

            RbMaterial.SelectedIndex = SettingsService.Material switch
            {
                "MicaAlt" => 1,
                "Acrylic" => 2,
                _         => 0
            };

            PanePositionCombo.SelectedIndex = SettingsService.PanePosition == "Top" ? 1 : 0;
            SoundToggle.IsOn = SettingsService.Sound;
        }

        public void LoadAppInfo()
        {
            try
            {
                TxtAppName.Text = Package.Current.DisplayName;
                var v = Package.Current.Id.Version;
                
                // 获取本地化的版本前缀，如果失败则使用默认值
                string versionPrefix = "Version";
                try
                {
                    var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
                    versionPrefix = loader.GetString("VersionPrefix");
                }
                catch
                {
                    // 使用默认值
                }
                
                TxtVersion.Text = $"{versionPrefix} {v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
                TxtCopyright.Text = $"© {DateTime.Now.Year} {Package.Current.PublisherDisplayName}";
                
                // 设置图标 - 使用 BitmapImage
                var bitmap = new BitmapImage(new Uri("ms-appx:///Assets/Square44x44Logo.scale-200.png"));
                ImgAppIconSettings.Source = bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadAppInfo 错误: {ex.Message}");
                // 非打包应用时不设置，保持空白
            }
        }

        private void OpenLink_Click(object sender, RoutedEventArgs e)
        {
            if (MainWindow.Instance != null)
                MainWindow.Instance.OpenExternalLink(sender, e);
        }

        private void RbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            string value = RbTheme.SelectedIndex switch
            {
                1 => "Light",
                2 => "Dark",
                _ => "System"
            };

            var theme = RbTheme.SelectedIndex switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

            SettingsService.SetTheme(value);
            AppThemeManager.CurrentTheme = theme;

            if (App.MainWindow?.Content is FrameworkElement root)
                root.RequestedTheme = theme;

            AppThemeManager.UpdateTitleBarColors();
        }

        private void RbMaterial_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            string value = RbMaterial.SelectedIndex switch
            {
                1 => "MicaAlt",
                2 => "Acrylic",
                _ => "Mica"
            };

            SettingsService.SetMaterial(value);
            AppThemeManager.CurrentMaterial = value switch
            {
                "MicaAlt" => BackgroundMaterial.MicaAlt,
                "Acrylic" => BackgroundMaterial.Acrylic,
                _ => BackgroundMaterial.Mica
            };

            AppThemeManager.ApplyMaterial();
        }

        private void PanePositionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            SettingsService.SetPanePosition(PanePositionCombo.SelectedIndex == 1 ? "Top" : "Left");

            if (App.MainWindow is MainWindow mainWindow)
                mainWindow.ApplySettings();
        }

        private void SoundToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool isOn = SoundToggle.IsOn;
            SettingsService.SetSound(isOn);

            // 直接设置，不再调 ApplySettings（避免重复执行导航栏逻辑）
            ElementSoundPlayer.State = isOn
                ? ElementSoundPlayerState.On
                : ElementSoundPlayerState.Off;
        }
    }
}
