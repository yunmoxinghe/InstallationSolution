using InstallationSolution.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Management.Deployment;

namespace InstallationSolution.Pages
{
    public sealed partial class HomePage : Page
    {
        private string? _msixPath;
        private string? _packageName;

        public HomePage()
        {
            this.InitializeComponent();
            this.Loaded += HomePage_Loaded;
        }

        // ── 初始化 ────────────────────────────────────────────────────

        private async void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            _msixPath = App.MsixPath;

            // 没有收到 Guard 信号 → UI 测试模式
            var guardPath = Environment.GetEnvironmentVariable("GUARD_SOURCE_PATH");
            if (string.IsNullOrEmpty(guardPath))
            {
                AppNameText.Text      = "WinUI3Template";
                AppPublisherText.Text = "Publisher: YUNMOXINGHE\nVersion: 1.3.1.0\nSource: (UI Test Mode)";
                PackageInfoBar.Severity = InfoBarSeverity.Warning;
                PackageInfoBar.Title   = "UI 测试模式";
                PackageInfoBar.Message = "未通过 Guard 启动，显示模拟数据。";
                AppCapabilities.CapabilitiesList = ["internetClient", "privateNetworkClientServer", "picturesLibrary"];
                AppCapabilities.Visibility = Visibility.Visible;
                return;
            }

            if (!string.IsNullOrEmpty(_msixPath) && File.Exists(_msixPath))
            {
                AppNameText.Text = Path.GetFileNameWithoutExtension(_msixPath);
                PackageInfoBar.Severity = InfoBarSeverity.Informational;
                PackageInfoBar.Title   = "准备安装";
                PackageInfoBar.Message = Path.GetFileName(_msixPath);

                var info = await PackageInfo.ReadAsync(_msixPath);

                if (!string.IsNullOrEmpty(info.DisplayName))
                    AppNameText.Text = info.DisplayName;

                var lines = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(info.Publisher)) lines.Add($"Publisher: {info.Publisher}");
                if (!string.IsNullOrEmpty(info.Version))   lines.Add($"Version: {info.Version}");
                var sourcePath = Environment.GetEnvironmentVariable("GUARD_SOURCE_PATH");
                lines.Add($"Source: {(string.IsNullOrEmpty(sourcePath) ? _msixPath : sourcePath)}");
                AppPublisherText.Text = string.Join("\n", lines);

                if (info.Icon != null)
                    AppIconImage.Source = info.Icon;

                if (info.Capabilities.Count > 0)
                {
                    AppCapabilities.CapabilitiesList = info.Capabilities;
                    AppCapabilities.Visibility = Visibility.Visible;
                }
            }
            else
            {
                AppNameText.Text      = "未检测到安装包";
                AppPublisherText.Text = string.IsNullOrEmpty(_msixPath) ? "未传入路径参数" : $"Source: {_msixPath}";
                PackageInfoBar.Severity = InfoBarSeverity.Error;
                PackageInfoBar.Title    = "错误";
                PackageInfoBar.Message  = string.IsNullOrEmpty(_msixPath)
                    ? "未收到 msix 路径，请通过 Guard 启动本程序。"
                    : $"找不到文件：{_msixPath}";
                InstallButton.IsEnabled = false;
            }
        }

        // ── 安装 ──────────────────────────────────────────────────────

        private void InstallButton_Click(object sender, RoutedEventArgs e)
            => _ = StartInstallAsync();

        private async Task StartInstallAsync()
        {
            // 切换到安装中状态
            InfoContainer.Visibility       = Visibility.Collapsed;
            InstallingContainer.Visibility = Visibility.Visible;
            ProgressContainer.Visibility   = Visibility.Visible;
            InstallButton.Visibility       = Visibility.Collapsed;

            if (string.IsNullOrEmpty(_msixPath))
            {
                ShowResult(success: false, errorMessage: "未收到安装包路径。");
                return;
            }

            try
            {
                StatusText.Text = "正在验证证书...";
                DetailText.Text = "导入签名证书到受信任根...";
                try
                {
                    await Task.Run(() => ImportSigningCertificate(_msixPath));
                    DetailText.Text = "证书导入完成";
                }
                catch (Exception certEx)
                {
                    DetailText.Text = $"证书导入跳过：{certEx.Message}";
                }

                StatusText.Text = "正在安装...";
                DetailText.Text = "启动部署引擎...";

                var pm  = new PackageManager();
                var uri = new Uri(_msixPath);
                var op  = pm.AddPackageAsync(uri, null, DeploymentOptions.ForceApplicationShutdown);

                op.Progress = (_, progress) =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        InstallProgressBar.IsIndeterminate = progress.percentage >= 99;
                        InstallProgressBar.Value = progress.percentage;
                        ProgressText.Text        = $"{progress.percentage:F0}%";
                        DetailText.Text          = $"进度：{progress.percentage:F0}%";
                    });
                };

                var result = await op.AsTask();

                if (result.ExtendedErrorCode == null || result.ExtendedErrorCode.HResult == 0)
                    ShowResult(success: true);
                else
                    ShowResult(success: false, errorMessage: result.ErrorText ?? "安装失败，原因未知。");
            }
            catch (Exception ex)
            {
                ShowResult(success: false, errorMessage: ex.Message);
            }
        }

        private static void ImportSigningCertificate(string packagePath)
        {
            var cert  = new X509Certificate2(X509Certificate2.CreateFromSignedFile(packagePath));
            var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);
            if (store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false).Count == 0)
                store.Add(cert);
            store.Close();
        }

        // ── 结果 ──────────────────────────────────────────────────────

        private void ShowResult(bool success, string? errorMessage = null)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                InstallingContainer.Visibility = Visibility.Collapsed;
                ProgressContainer.Visibility   = Visibility.Collapsed;
                ResultContainer.Visibility     = Visibility.Visible;
                CloseButton.Visibility         = Visibility.Visible;

                if (success)
                {
                    ResultTitle.Text    = "安装成功";
                    ResultSubtitle.Text = "应用已成功安装到此设备。";

                    _packageName = ReadPackageIdentityName(_msixPath);
                    if (_packageName != null)
                        LaunchButton.Visibility = Visibility.Visible;
                }
                else
                {
                    ResultTitle.Text    = "安装失败";
                    ResultSubtitle.Text = "安装过程中发生错误。";
                    if (!string.IsNullOrEmpty(errorMessage))
                    {
                        ErrorInfoBar.IsOpen  = true;
                        ErrorInfoBar.Message = errorMessage;
                    }
                }
            });
        }

        private async void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            if (_packageName == null) return;
            try
            {
                var pm  = new PackageManager();
                var pkg = pm.FindPackagesForUser("")
                            .FirstOrDefault(p => p.Id.Name.Equals(_packageName, StringComparison.OrdinalIgnoreCase));
                if (pkg == null) return;
                var apps = await pkg.GetAppListEntriesAsync();
                if (apps.Count > 0) await apps[0].LaunchAsync();
            }
            catch { }
            finally { Application.Current.Exit(); }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
            => Application.Current.Exit();

        // ── 工具方法 ──────────────────────────────────────────────────

        private static string? ReadPackageIdentityName(string? packagePath)
        {
            if (string.IsNullOrEmpty(packagePath)) return null;
            try
            {
                var ext = Path.GetExtension(packagePath).ToLowerInvariant();
                if (ext is ".msixbundle" or ".appxbundle")
                {
                    using var bundle = ZipFile.OpenRead(packagePath);
                    var inner = bundle.Entries.FirstOrDefault(e =>
                        e.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.EndsWith(".appx", StringComparison.OrdinalIgnoreCase));
                    if (inner == null) return null;
                    using var s   = inner.Open();
                    using var zip = new ZipArchive(s, ZipArchiveMode.Read);
                    return ExtractNameFromZip(zip);
                }
                using var z = ZipFile.OpenRead(packagePath);
                return ExtractNameFromZip(z);
            }
            catch { return null; }
        }

        private static string? ExtractNameFromZip(ZipArchive zip)
        {
            var entry = zip.GetEntry("AppxManifest.xml");
            if (entry == null) return null;
            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
            return doc.Root?.Element(ns + "Identity")?.Attribute("Name")?.Value;
        }
    }
}
