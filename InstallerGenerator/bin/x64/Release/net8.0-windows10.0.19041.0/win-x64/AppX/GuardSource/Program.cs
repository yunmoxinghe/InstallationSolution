﻿using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace InstallerGuard
{
    internal class Program
    {
        internal const string ToastAppId = "YourInstaller.Guard";
        internal const string ToastTag   = "installer-loading";
        internal const string ToastGroup = "installer";

        // 根据系统首选 UI 语言决定用中文还是英文
        static bool IsChinese()
        {
            var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return lang == "zh";
        }

        static string S(string zh, string en) => IsChinese() ? zh : en;

        [DllImport("kernel32.dll")] static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, uint dwFlags);

        static void Main(string[] args)
        {
            using var mutex = new Mutex(true, "YourInstallerGuard", out bool created);
            if (!created) return;

            try
            {
                var assembly     = Assembly.GetExecutingAssembly();
                var resourceName = "InstallerGuard.Payload.InstallerUI.zip";

                // 启动时清理上次可能残留的文件和注册表（Guard 被强杀时来不及清理）
                Cleanup();

                // 先检查资源，避免发出加载 Toast 后立刻换成错误 Toast 的闪烁
                {
                    using var rs = assembly.GetManifestResourceStream(resourceName);
                    if (rs == null)
                    {
                        var iconPath0 = ExtractIcon();
                        RegisterAppId(iconPath0);
                        ShowErrorToast(S("缺少安装程序", "Missing Installer"), S("安装程序文件丢失，请重新下载安装包。", "Installer files are missing. Please re-download the package."));
                        return;
                    }

                    // 资源正常，再注册并发加载中 Toast
                    var iconPath = ExtractIcon();
                    RegisterAppId(iconPath);
                    ShowLoadingToast();

                    // 注册重启时兜底删除（Guard 被强杀时由系统在重启后清理）
                    RegisterDeleteOnReboot(Path.Combine(Path.GetTempPath(), "YourInstaller"));
                    RegisterDeleteOnReboot(Path.Combine(Path.GetTempPath(), "YourInstaller_icon.png"));
                    RegisterRunOnceCleanupRegistry();

                    var tempDir2 = Path.Combine(Path.GetTempPath(), "YourInstaller");
                    if (Directory.Exists(tempDir2)) Directory.Delete(tempDir2, true);
                    Directory.CreateDirectory(tempDir2);

                    using (var archive = new ZipArchive(rs, ZipArchiveMode.Read))
                        archive.ExtractToDirectory(tempDir2);
                } // rs 在此关闭

                var tempDir = Path.Combine(Path.GetTempPath(), "YourInstaller");
                var installerExe = Path.Combine(tempDir, "InstallerUI", "InstallerUI.exe");
                if (!File.Exists(installerExe))
                {
                    ShowErrorToast(S("缺少安装程序", "Missing Installer"), S("安装程序文件丢失，请重新下载安装包。", "Installer files are missing. Please re-download the package."));
                    return;
                }

                var bundleResourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.StartsWith("InstallerGuard.Payload.") &&
                                        (n.EndsWith(".msixbundle") || n.EndsWith(".msix")));

                if (bundleResourceName == null)
                {
                    ShowErrorToast(S("缺少安装包", "Missing Package"), S("找不到嵌入的安装包，请重新下载安装包。", "Embedded package not found. Please re-download the package."));
                    return;
                }

                var bundleFileName = bundleResourceName.Replace("InstallerGuard.Payload.", "");
                var bundlePath     = Path.Combine(tempDir, "InstallerUI", bundleFileName);
                using (var msixStream = assembly.GetManifestResourceStream(bundleResourceName)!)
                using (var fileStream = File.Create(bundlePath))
                {
                    msixStream.CopyTo(fileStream);
                } // 流在这里关闭，PackageManager 可以独占访问
                var msixArg = $"\"{bundlePath}\"";

                // 把 ToastAppId/Tag/Group 通过环境变量传给 InstallerUI，让它知道关哪条 Toast
                var psi = new ProcessStartInfo
                {
                    FileName        = installerExe,
                    Arguments       = msixArg,
                    UseShellExecute = false,
                };
                psi.Environment["GUARD_TOAST_APPID"] = ToastAppId;
                psi.Environment["GUARD_TOAST_TAG"]   = ToastTag;
                psi.Environment["GUARD_TOAST_GROUP"] = ToastGroup;
                psi.Environment["GUARD_SOURCE_PATH"] = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

                var process = Process.Start(psi);
                process?.WaitForExit();
                Cleanup();
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                ShowErrorToast(S("InstallerGuard 错误", "InstallerGuard Error"), ex.Message);
            }
        }

        // ── 清理 ──────────────────────────────────────────────────────

        static void Cleanup()
        {
            try { Directory.Delete(Path.Combine(Path.GetTempPath(), "YourInstaller"), true); } catch { }
            try { File.Delete(Path.Combine(Path.GetTempPath(), "YourInstaller_icon.png")); } catch { }
            try
            {
                RunPowerShell($@"
Remove-Item -Path ""HKCU:\Software\Classes\AppUserModelId\{ToastAppId}"" -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path ""HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce"" -Name ""YourInstallerCleanup"" -ErrorAction SilentlyContinue
");
            }
            catch { }
        }

        static void RegisterRunOnceCleanupRegistry()
        {
            try
            {
                RunPowerShell($@"
$cmd = 'powershell -NoProfile -NonInteractive -WindowStyle Hidden -Command ""Remove-Item -Path ''''HKCU:\Software\Classes\AppUserModelId\{ToastAppId}'''' -Recurse -Force -ErrorAction SilentlyContinue""'
New-ItemProperty -Path ""HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce"" -Name ""YourInstallerCleanup"" -Value $cmd -PropertyType String -Force | Out-Null
");
            }
            catch { }
        }

        // 注册文件/目录在重启时删除（需要管理员权限）
        static void RegisterDeleteOnReboot(string path)
        {
            if (File.Exists(path))
            {
                MoveFileEx(path, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            }
            else if (Directory.Exists(path))
            {
                // 先递归注册所有文件，再注册各级目录（由内到外）
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    MoveFileEx(file, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                foreach (var dir in Directory.GetDirectories(path, "*", SearchOption.AllDirectories)
                                             .OrderByDescending(d => d.Length)) // 最深的先删
                    MoveFileEx(dir, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                MoveFileEx(path, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            }
        }

        const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

        // ── Toast 辅助 ────────────────────────────────────────────────

        static string? ExtractIcon()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resName  = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("notification.png"));
                if (resName == null) return null;

                var path = Path.Combine(Path.GetTempPath(), "YourInstaller_icon.png");
                using var s = assembly.GetManifestResourceStream(resName)!;
                using var f = File.Create(path);
                s.CopyTo(f);
                return path;
            }
            catch { return null; }
        }

        static void RegisterAppId(string? iconPath)
        {
            try
            {
                var script = $@"
$appId   = '{ToastAppId}'
$regPath = ""HKCU:\Software\Classes\AppUserModelId\$appId""
if (!(Test-Path $regPath)) {{ $null = New-Item -Path $regPath -Force }}
$null = New-ItemProperty -Path $regPath -Name DisplayName      -Value '{S("安装器", "Installer")}'  -PropertyType String -Force
$null = New-ItemProperty -Path $regPath -Name ShowInSettings   -Value 0         -PropertyType DWORD  -Force
";
                if (!string.IsNullOrEmpty(iconPath))
                    script += $@"$null = New-ItemProperty -Path $regPath -Name IconUri -Value '{iconPath}' -PropertyType ExpandString -Force";

                RunPowerShell(script);
            }
            catch { }
        }

        static void ShowLoadingToast()
        {
            try
            {
                var xml = $@"<toast scenario=""reminder"">
  <visual>
    <binding template=""ToastGeneric"">
      <text>{S("正在启动安装器", "Starting Installer")}</text>
      <progress value=""indeterminate"" status=""{S("正在加载...", "Loading...")}"" title="""" valueStringOverride=""""/>
    </binding>
  </visual>
  <actions>
    <action content=""{S("隐藏", "Hide")}"" arguments=""dismiss"" activationType=""system"" hint-buttonStyle=""Critical""/>
  </actions>
</toast>";

                var script = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
$xml = New-Object Windows.Data.Xml.Dom.XmlDocument
$xml.LoadXml(@'
{xml}
'@)
$toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
$toast.Tag   = '{ToastTag}'
$toast.Group = '{ToastGroup}'
$toast.SuppressPopup = $false
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{ToastAppId}').Show($toast)
";
                RunPowerShell(script);
            }
            catch { }
        }

        static void ShowErrorToast(string title, string message)
        {
            try
            {
                DismissToast();

                var safeMsg = message.Replace("'", "`'").Replace("\r", "").Replace("\n", " ");
                var safeTitle = title.Replace("'", "`'");
                var xml = $@"<toast>
  <visual>
    <binding template=""ToastGeneric"">
      <text>{safeTitle}</text>
      <text>{safeMsg}</text>
    </binding>
  </visual>
</toast>";

                var script = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
$xml = New-Object Windows.Data.Xml.Dom.XmlDocument
$xml.LoadXml(@'
{xml}
'@)
$toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
$toast.Tag   = '{ToastTag}'
$toast.Group = '{ToastGroup}'
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{ToastAppId}').Show($toast)
";
                RunPowerShell(script);
            }
            catch { }
        }

        static void DismissToast()
        {
            try
            {
                var script = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.UI.Notifications.ToastNotificationHistory, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.UI.Notifications.ToastNotificationManager]::History.Remove('{ToastTag}', '{ToastGroup}', '{ToastAppId}')
";
                RunPowerShell(script);
            }
            catch { }
        }

        static void RunPowerShell(string script)
        {
            // 写到临时 .ps1 文件避免命令行引号转义问题
            var ps1 = Path.Combine(Path.GetTempPath(), $"guard_{Guid.NewGuid():N}.ps1");
            try
            {
                File.WriteAllText(ps1, script, System.Text.Encoding.UTF8);
                Process.Start(new ProcessStartInfo
                {
                    FileName        = "powershell.exe",
                    Arguments       = $"-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{ps1}\"",
                    UseShellExecute = false,
                    CreateNoWindow  = true,
                })?.WaitForExit(5000);
            }
            finally
            {
                try { File.Delete(ps1); } catch { }
            }
        }
    }
}
