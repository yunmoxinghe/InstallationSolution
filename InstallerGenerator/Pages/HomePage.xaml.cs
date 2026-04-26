using InstallerGenerator.Constants;
using InstallerGenerator.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace InstallerGenerator.Pages
{
    public sealed partial class HomePage : Page
    {
        private string? _msixPath;
        private string? _outputPath;
        private string? _lastBuiltExePath;

        public HomePage()
        {
            InitializeComponent();

            // 恢复上次的输出路径
            var saved = SettingsService.LastOutput;
            if (saved != null && Directory.Exists(saved))
            {
                _outputPath            = saved;
                OutputPathText.Text    = saved;
                OutputPathText.Opacity = 1;
                UpdateGenerateButton();
            }
        }

        // ── 外部加载文件（文件关联激活） ────────────────────────────

        public void LoadFile(string filePath)
        {
            _msixPath               = filePath;
            PackagePathText.Text    = Path.GetFileName(filePath);
            PackagePathText.Opacity = 1;
            UpdateGenerateButton();
        }

        // ── 选择 msix ────────────────────────────────────────────────

        private async void BrowsePackage_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            foreach (var ext in AppConstants.FileExtensions.Package)
                picker.FileTypeFilter.Add(ext);
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow.Instance));

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            LoadFile(file.Path);
        }

        // ── 选择输出路径 ─────────────────────────────────────────────

        private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow.Instance));

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null) return;

            _outputPath            = folder.Path;
            OutputPathText.Text    = folder.Path;
            OutputPathText.Opacity = 1;
            SettingsService.SetLastOutput(folder.Path);
            UpdateGenerateButton();
        }

        private void UpdateGenerateButton()
            => GenerateButton.IsEnabled = !string.IsNullOrEmpty(_msixPath)
                                       && !string.IsNullOrEmpty(_outputPath);

        // ── 拖拽 ─────────────────────────────────────────────────────

        private void Page_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.Caption        = "导入安装包";
            e.DragUIOverride.IsGlyphVisible = true;
        }

        private async void Page_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                return;

            var items = await e.DataView.GetStorageItemsAsync();
            var file  = items.OfType<StorageFile>().FirstOrDefault(f =>
                AppConstants.FileExtensions.Package.Contains(
                    Path.GetExtension(f.Name).ToLowerInvariant()));

            if (file == null) return;
            LoadFile(file.Path);
        }

        // ── 生成 ─────────────────────────────────────────────────────

        private async void Generate_Click(object sender, RoutedEventArgs e)
        {
            GenerateButton.IsEnabled    = false;
            BuildProgress.Visibility    = Visibility.Visible;
            StatusBar.IsOpen            = false;
            PostBuildActions.Visibility = Visibility.Collapsed;

            try
            {
                string exePath = await Task.Run(() => BuildService.Build(_msixPath!, _outputPath!));
                _lastBuiltExePath = exePath;
                ShowStatus(InfoBarSeverity.Success, "生成成功", $"输出到：{_outputPath}");
                PostBuildActions.Visibility = Visibility.Visible;
                SendSuccessNotification(exePath);
            }
            catch (Exception ex)
            {
                ShowStatus(InfoBarSeverity.Error, "生成失败", ex.Message);
            }
            finally
            {
                BuildProgress.Visibility = Visibility.Collapsed;
                GenerateButton.IsEnabled = true;
            }
        }

        // ── 通知 ─────────────────────────────────────────────────────

        private void SendSuccessNotification(string exePath)
        {
            try
            {
                var folderPath = Path.GetDirectoryName(exePath) ?? _outputPath ?? "";
                var builder    = new AppNotificationBuilder()
                    .AddText("安装器生成成功")
                    .AddText(Path.GetFileName(exePath))
                    .AddButton(new AppNotificationButton("打开文件夹")
                        .AddArgument("action", "openFolder")
                        .AddArgument("path", folderPath));

                AppNotificationManager.Default.Show(builder.BuildNotification());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Notification failed: {ex.Message}");
            }
        }

        // ── 打开文件夹 ───────────────────────────────────────────────

        private async void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_outputPath)) return;
            try
            {
                if (!string.IsNullOrEmpty(_lastBuiltExePath) && File.Exists(_lastBuiltExePath))
                    Process.Start("explorer.exe", $"/select,\"{_lastBuiltExePath}\"");
                else
                    await Launcher.LaunchFolderPathAsync(_outputPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OpenFolder failed: {ex.Message}");
            }
        }

        // ── 分享文件 ─────────────────────────────────────────────────

        [System.Runtime.InteropServices.ComImport]
        [System.Runtime.InteropServices.Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
        [System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
        interface IDataTransferManagerInterop
        {
            IntPtr GetForWindow([System.Runtime.InteropServices.In] IntPtr appWindow,
                                [System.Runtime.InteropServices.In] ref Guid riid);
            void ShowShareUIForWindow(IntPtr appWindow);
        }

        private static readonly Guid _dtmIid =
            new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

        private async void ShareFile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastBuiltExePath) || !File.Exists(_lastBuiltExePath))
                return;

            try
            {
                var file   = await StorageFile.GetFileFromPathAsync(_lastBuiltExePath);
                var hWnd   = WindowNative.GetWindowHandle(MainWindow.Instance);
                var interop = Windows.ApplicationModel.DataTransfer.DataTransferManager
                    .As<IDataTransferManagerInterop>();
                var iid    = _dtmIid;
                var result = interop.GetForWindow(hWnd, ref iid);
                var dtm    = WinRT.MarshalInterface<Windows.ApplicationModel.DataTransfer.DataTransferManager>
                    .FromAbi(result);

                dtm.DataRequested += (_, args) =>
                {
                    var items = new List<Windows.Storage.IStorageItem> { file };
                    args.Request.Data.SetStorageItems(items);
                    args.Request.Data.Properties.Title = Path.GetFileName(_lastBuiltExePath);
                };

                interop.ShowShareUIForWindow(hWnd);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShareFile failed: {ex.Message}");
            }
        }

        // ── 工具 ─────────────────────────────────────────────────────

        private void ShowStatus(InfoBarSeverity severity, string title, string message)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                StatusBar.Severity = severity;
                StatusBar.Title    = title;
                StatusBar.Message  = message;
                StatusBar.IsOpen   = true;
            });
        }
    }
}
