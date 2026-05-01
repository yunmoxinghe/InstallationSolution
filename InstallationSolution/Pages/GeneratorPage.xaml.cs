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
using InstallationSolution.Services;

namespace InstallationSolution.Pages
{
    public sealed partial class GeneratorPage : Page
    {
        private string? _msixPath;
        private string? _outputPath;
        private string? _lastBuiltExePath;
        private readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

        public GeneratorPage()
        {
            InitializeComponent();
            
            // 恢复上次的输出路径
            if (_localSettings.Values["LastOutputPath"] is string saved && Directory.Exists(saved))
            {
                _outputPath = saved;
                OutputPathText.Text = saved;
                OutputPathText.Opacity = 1;
                UpdateGenerateButton();
            }
        }

        /// <summary>
        /// 从外部加载文件（用于文件关联启动）
        /// </summary>
        public void LoadFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return;

            _msixPath = filePath;
            PackagePathText.Text = Path.GetFileName(filePath);
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
            PackagePathText.Text = file.Name;
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
            OutputPathText.Text = folder.Path;
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
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "导入安装包";
            e.DragUIOverride.IsGlyphVisible = true;
        }

        private async void Page_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
                return;

            var items = await e.DataView.GetStorageItemsAsync();
            var file = items.OfType<StorageFile>().FirstOrDefault(f =>
            {
                var ext = Path.GetExtension(f.Name).ToLowerInvariant();
                return ext is ".msix" or ".msixbundle" or ".appx" or ".appxbundle";
            });

            if (file == null) return;

            _msixPath = file.Path;
            PackagePathText.Text = file.Name;
            PackagePathText.Opacity = 1;
            UpdateGenerateButton();
        }

        // ── 生成 ─────────────────────────────────────────────────────

        private async void Generate_Click(object sender, RoutedEventArgs e)
        {
            GenerateButton.IsEnabled = false;
            BuildProgress.Visibility = Visibility.Visible;
            StatusBar.IsOpen = false;
            PostBuildActions.Visibility = Visibility.Collapsed;

            try
            {
                // 首次生成时预构建 InstallerUI.zip
                ShowStatus(InfoBarSeverity.Informational, "准备中", "正在构建非打包版本的 InstallerUI，这可能需要 30-60 秒...");
                await SelfBuildService.GetOrBuildInstallerUIZipAsync();
                
                ShowStatus(InfoBarSeverity.Informational, "构建中", "正在编译安装器...");
                string exePath = await Task.Run(() => RunBuild());
                _lastBuiltExePath = exePath;
                ShowStatus(InfoBarSeverity.Success, "生成成功", $"输出到：{_outputPath}");
                PostBuildActions.Visibility = Visibility.Visible;
                SendSuccessNotification(exePath);
            }
            catch (Exception ex)
            {
                var errorMsg = ex.InnerException != null 
                    ? $"{ex.Message}\n\n详细信息: {ex.InnerException.Message}" 
                    : ex.Message;
                ShowStatus(InfoBarSeverity.Error, "生成失败", errorMsg);
                Debug.WriteLine($"[GeneratorPage] 生成失败: {ex}");
            }
            finally
            {
                BuildProgress.Visibility = Visibility.Collapsed;
                GenerateButton.IsEnabled = true;
            }
        }

        private string RunBuild()
        {
            // 创建临时的 GuardSource 目录（包含完整的 Payload）
            var tempGuardSrc = Path.Combine(Path.GetTempPath(), "GuardSource_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempGuardSrc);

            try
            {
                // 从应用安装目录复制 Guard 源码
                var installDir = Package.Current.InstalledLocation.Path;
                var sourceGuardSrc = Path.Combine(installDir, "GuardSource");
                
                Debug.WriteLine($"[RunBuild] installDir: {installDir}");
                Debug.WriteLine($"[RunBuild] sourceGuardSrc: {sourceGuardSrc}");
                Debug.WriteLine($"[RunBuild] tempGuardSrc: {tempGuardSrc}");

                if (!Directory.Exists(sourceGuardSrc))
                    throw new DirectoryNotFoundException($"找不到 Guard 源码目录：{sourceGuardSrc}");

                // 复制所有 Guard 源文件
                foreach (var file in Directory.GetFiles(sourceGuardSrc))
                {
                    var fileName = Path.GetFileName(file);
                    File.Copy(file, Path.Combine(tempGuardSrc, fileName), overwrite: true);
                }

                var csproj = Path.Combine(tempGuardSrc, "InstallerGuard.csproj");
                if (!File.Exists(csproj))
                    throw new FileNotFoundException($"找不到 Guard 项目文件：{csproj}");

                // 创建 Payload 目录
                var payloadDir = Path.Combine(tempGuardSrc, "Payload");
                Directory.CreateDirectory(payloadDir);
                Debug.WriteLine($"[RunBuild] payloadDir: {payloadDir}");

                // 使用自举式构建：运行时将当前应用打包成 InstallerUI.zip
                Debug.WriteLine($"[RunBuild] 开始复制 InstallerUI.zip 到 Payload");
                SelfBuildService.CopyToPayloadAsync(payloadDir).GetAwaiter().GetResult();
                Debug.WriteLine($"[RunBuild] InstallerUI.zip 复制完成");
                
                // 验证文件是否存在
                var installerUIZip = Path.Combine(payloadDir, "InstallerUI.zip");
                if (!File.Exists(installerUIZip))
                    throw new FileNotFoundException($"InstallerUI.zip 未能复制到 Payload: {installerUIZip}");
                
                Debug.WriteLine($"[RunBuild] InstallerUI.zip 大小: {new FileInfo(installerUIZip).Length} bytes");

                Debug.WriteLine($"[RunBuild] InstallerUI.zip 大小: {new FileInfo(installerUIZip).Length} bytes");

                // 复制用户选择的 msix
                var msixFileName = Path.GetFileName(_msixPath!);
                File.Copy(_msixPath!, Path.Combine(payloadDir, msixFileName), overwrite: true);
                Debug.WriteLine($"[RunBuild] 已复制用户 msix: {msixFileName}");

                // dotnet publish
                var outputExeName = Path.GetFileNameWithoutExtension(_msixPath!) + "_Installer.exe";
                var publishOut = Path.Combine(Path.GetTempPath(), "GuardPublish_" + Guid.NewGuid().ToString("N"));

                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"publish \"{csproj}\" -r win-x64 -c Release -o \"{publishOut}\" --no-self-contained",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                Debug.WriteLine($"[RunBuild] 开始 dotnet publish");
                Debug.WriteLine($"[RunBuild] 命令: {psi.FileName} {psi.Arguments}");

                using var proc = Process.Start(psi)
                    ?? throw new Exception("无法启动 dotnet publish");

                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                // 保存构建日志到桌面用于调试
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "dotnet_publish_log.txt");
                File.WriteAllText(logPath, $"=== STDOUT ===\n{stdout}\n\n=== STDERR ===\n{stderr}\n\n=== Exit Code ===\n{proc.ExitCode}");
                Debug.WriteLine($"[RunBuild] 构建日志已保存到: {logPath}");

                if (proc.ExitCode != 0)
                {
                    Debug.WriteLine($"[RunBuild] dotnet publish 失败");
                    Debug.WriteLine($"[RunBuild] stdout: {stdout}");
                    Debug.WriteLine($"[RunBuild] stderr: {stderr}");
                    throw new Exception($"dotnet publish 失败 (exit {proc.ExitCode}):\n{stderr}\n{stdout}");
                }

                Debug.WriteLine($"[RunBuild] dotnet publish 成功");

                // 找到产物 exe 并复制到输出目录
                var exeFiles = Directory.GetFiles(publishOut, "InstallerGuard.exe", SearchOption.AllDirectories);
                if (exeFiles.Length == 0)
                    throw new FileNotFoundException("找不到编译产物 InstallerGuard.exe");

                var destPath = Path.Combine(_outputPath!, outputExeName);
                File.Copy(exeFiles[0], destPath, overwrite: true);
                Debug.WriteLine($"[RunBuild] 已复制到输出目录: {destPath}");

                // 清理临时 publish 目录
                try { Directory.Delete(publishOut, true); } catch { }

                // 如果输出到桌面，刷新桌面图标
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (string.Equals(Path.GetFullPath(_outputPath!), Path.GetFullPath(desktop), StringComparison.OrdinalIgnoreCase))
                    SHChangeNotify(0x8000000, 0x1000, IntPtr.Zero, IntPtr.Zero);

                return destPath;
            }
            finally
            {
                // 暂时不清理，用于调试
                Debug.WriteLine($"[RunBuild] 临时目录保留用于调试: {tempGuardSrc}");
                // TODO: 调试完成后恢复清理
                // try 
                // { 
                //     Directory.Delete(tempGuardSrc, true);
                //     Debug.WriteLine($"[RunBuild] 已清理临时目录: {tempGuardSrc}");
                // } 
                // catch (Exception ex)
                // {
                //     Debug.WriteLine($"[RunBuild] 清理临时目录失败: {ex.Message}");
                // }
            }
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

                var interop = DataTransferManager.As<IDataTransferManagerInterop>();
                var iid = _dtmIid;
                var result = interop.GetForWindow(hWnd, ref iid);
                var dtm = WinRT.MarshalInterface<DataTransferManager>.FromAbi(result);

                dtm.DataRequested += (_, args) =>
                {
                    var items = new System.Collections.Generic.List<IStorageItem> { file };
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
                StatusBar.Title = title;
                StatusBar.Message = message;
                StatusBar.IsOpen = true;
            });
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }
}
