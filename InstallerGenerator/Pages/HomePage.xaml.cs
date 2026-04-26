using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
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
        private string? _lastBuiltExePath;   // 最近一次生成的 exe 路径
        private readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

        public HomePage()
        {
            InitializeComponent();
            // 恢复上次的输出路径
            if (_localSettings.Values["LastOutputPath"] is string saved && Directory.Exists(saved))
            {
                _outputPath = saved;
                OutputPathText.Text    = saved;
                OutputPathText.Opacity = 1;
                UpdateGenerateButton();
            }
        }

        // ── 外部加载文件（文件关联激活） ────────────────────────────
        public void LoadFile(string filePath)
        {
            _msixPath = filePath;
            PackagePathText.Text    = System.IO.Path.GetFileName(filePath);
            PackagePathText.Opacity = 1;
            UpdateGenerateButton();
        }

        // ── 选择 msix ────────────────────────────────────────────────

        private async void BrowsePackage_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".msix");
            picker.FileTypeFilter.Add(".msixbundle");
            picker.FileTypeFilter.Add(".appx");
            picker.FileTypeFilter.Add(".appxbundle");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow.Instance));

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            _msixPath = file.Path;
            PackagePathText.Text    = file.Name;
            PackagePathText.Opacity = 1;
            UpdateGenerateButton();
        }

        // ── 选择输出路径 ─────────────────────────────────────────────

        private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow.Instance));

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null) return;

            _outputPath = folder.Path;
            OutputPathText.Text    = folder.Path;
            OutputPathText.Opacity = 1;
            _localSettings.Values["LastOutputPath"] = folder.Path;
            UpdateGenerateButton();
        }

        private void UpdateGenerateButton()
            => GenerateButton.IsEnabled = !string.IsNullOrEmpty(_msixPath)
                                       && !string.IsNullOrEmpty(_outputPath);

        // ── 拖拽 ─────────────────────────────────────────────────────

        private void Page_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "导入安装包";
            e.DragUIOverride.IsGlyphVisible = true;
        }

        private async void Page_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                return;

            var items = await e.DataView.GetStorageItemsAsync();
            var file  = items.OfType<Windows.Storage.StorageFile>().FirstOrDefault(f =>
            {
                var ext = System.IO.Path.GetExtension(f.Name).ToLowerInvariant();
                return ext is ".msix" or ".msixbundle" or ".appx" or ".appxbundle";
            });

            if (file == null) return;

            _msixPath = file.Path;
            PackagePathText.Text    = file.Name;
            PackagePathText.Opacity = 1;
            UpdateGenerateButton();
        }

        // ── 生成 ─────────────────────────────────────────────────────

        private async void Generate_Click(object sender, RoutedEventArgs e)
        {
            GenerateButton.IsEnabled  = false;
            BuildProgress.Visibility  = Visibility.Visible;
            StatusBar.IsOpen          = false;
            PostBuildActions.Visibility = Visibility.Collapsed;

            try
            {
                string exePath = await Task.Run(() => RunBuild());
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

        private string RunBuild()
        {
            // Guard 源码在应用安装目录的 GuardSource 子目录
            var installDir  = Package.Current.InstalledLocation.Path;
            var guardSrc    = Path.Combine(installDir, "GuardSource");
            var csproj      = Path.Combine(guardSrc, "InstallerGuard.csproj");
            var payloadDir  = Path.Combine(guardSrc, "Payload");

            if (!File.Exists(csproj))
                throw new FileNotFoundException($"找不到 Guard 源码：{csproj}");

            // 清理旧的 msix payload，复制新的
            foreach (var f in Directory.GetFiles(payloadDir, "*.msix"))   File.Delete(f);
            foreach (var f in Directory.GetFiles(payloadDir, "*.msixbundle")) File.Delete(f);
            foreach (var f in Directory.GetFiles(payloadDir, "*.appx"))   File.Delete(f);
            foreach (var f in Directory.GetFiles(payloadDir, "*.appxbundle")) File.Delete(f);

            var msixFileName = Path.GetFileName(_msixPath!);
            File.Copy(_msixPath!, Path.Combine(payloadDir, msixFileName), overwrite: true);

            // dotnet publish
            var outputExeName = Path.GetFileNameWithoutExtension(_msixPath!) + "_Installer.exe";
            var publishOut    = Path.Combine(Path.GetTempPath(), "GuardPublish_" + Guid.NewGuid().ToString("N"));

            var psi = new ProcessStartInfo
            {
                FileName               = "dotnet",
                Arguments              = $"publish \"{csproj}\" -r win-x64 -c Release -o \"{publishOut}\" --no-self-contained",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };

            using var proc = Process.Start(psi)
                ?? throw new Exception("无法启动 dotnet publish");

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
                throw new Exception($"dotnet publish 失败 (exit {proc.ExitCode}):\n{stderr}\n{stdout}");

            // 找到产物 exe 并复制到输出目录
            var exeFiles = Directory.GetFiles(publishOut, "InstallerGuard.exe", SearchOption.AllDirectories);
            if (exeFiles.Length == 0)
                throw new FileNotFoundException("找不到编译产物 InstallerGuard.exe");

            var destPath = Path.Combine(_outputPath!, outputExeName);
            File.Copy(exeFiles[0], destPath, overwrite: true);

            // 清理临时 publish 目录
            try { Directory.Delete(publishOut, true); } catch { }

            // 如果输出到桌面，刷新桌面图标
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (string.Equals(Path.GetFullPath(_outputPath!), Path.GetFullPath(desktop), StringComparison.OrdinalIgnoreCase))
                SHChangeNotify(0x8000000, 0x1000, IntPtr.Zero, IntPtr.Zero);

            return destPath;
        }

        // ── 通知 ─────────────────────────────────────────────────────

        private void SendSuccessNotification(string exePath)
        {
            try
            {
                var fileName = Path.GetFileName(exePath);
                var folderPath = Path.GetDirectoryName(exePath) ?? _outputPath ?? "";

                var builder = new AppNotificationBuilder()
                    .AddText("安装器生成成功")
                    .AddText(fileName)
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
                // 如果有具体文件，在资源管理器中选中它；否则直接打开文件夹
                if (!string.IsNullOrEmpty(_lastBuiltExePath) && File.Exists(_lastBuiltExePath))
                {
                    Process.Start("explorer.exe", $"/select,\"{_lastBuiltExePath}\"");
                }
                else
                {
                    await Launcher.LaunchFolderPathAsync(_outputPath);
                }
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
                var file = await StorageFile.GetFileFromPathAsync(_lastBuiltExePath);
                var hWnd = WindowNative.GetWindowHandle(MainWindow.Instance);

                var interop = Windows.ApplicationModel.DataTransfer.DataTransferManager
                    .As<IDataTransferManagerInterop>();
                var iid = _dtmIid;
                var result = interop.GetForWindow(hWnd, ref iid);
                var dtm = WinRT.MarshalInterface<Windows.ApplicationModel.DataTransfer.DataTransferManager>
                    .FromAbi(result);

                dtm.DataRequested += (_, args) =>
                {
                    // AOT/Trimming 下必须用 List<IStorageItem>，不能用 StorageFile[]
                    var items = new System.Collections.Generic.List<Windows.Storage.IStorageItem> { file };
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

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }
}
