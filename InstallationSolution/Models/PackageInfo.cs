using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Storage.Streams;
using Windows.ApplicationModel.Resources;

namespace InstallationSolution.Models
{
    public class PackageInfo
    {
        public string? DisplayName { get; private set; }
        public string? Version     { get; private set; }
        public string? Publisher   { get; private set; }
        public string? IdentityName { get; private set; }
        public List<string> Capabilities { get; private set; } = [];
        public BitmapImage? Icon { get; private set; }

        private PackageInfo() { }

        private static readonly ResourceLoader _resourceLoader = new();

        // Capability ID 到用户友好描述的映射（作为后备）
        private static readonly Dictionary<string, (string zhCN, string enUS)> CapabilityDescriptions = new()
        {
            // 网络相关
            ["internetClient"] = ("访问 Internet 连接", "Access your Internet connection"),
            ["internetClientServer"] = ("访问 Internet 连接（包括传入连接）", "Access your Internet connection, including incoming connections"),
            ["privateNetworkClientServer"] = ("访问家庭或工作网络", "Access your home or work networks"),
            
            // 位置
            ["location"] = ("访问你的位置", "Access your location"),
            ["locationHistory"] = ("访问位置历史记录", "Access your location history"),
            
            // 设备
            ["microphone"] = ("使用麦克风", "Use your microphone"),
            ["webcam"] = ("使用摄像头", "Use your camera"),
            ["proximity"] = ("使用近程通信", "Use proximity"),
            ["bluetooth"] = ("使用蓝牙", "Use Bluetooth"),
            ["radios"] = ("控制无线电设备", "Control radios"),
            ["usb"] = ("访问 USB 设备", "Access USB devices"),
            ["humaninterfacedevice"] = ("访问人机接口设备", "Access human interface devices"),
            ["pointOfService"] = ("访问销售点设备", "Access point of service devices"),
            ["serialcommunication"] = ("访问串行端口", "Access serial ports"),
            ["optical"] = ("访问光盘驱动器", "Access optical disc drives"),
            ["activity"] = ("检测设备运动", "Detect device motion"),
            ["humanPresence"] = ("检测用户在场和参与度", "Detect user presence"),
            
            // 媒体库
            ["musicLibrary"] = ("访问音乐库", "Access your music library"),
            ["picturesLibrary"] = ("访问图片库", "Access your pictures library"),
            ["videosLibrary"] = ("访问视频库", "Access your videos library"),
            ["documentsLibrary"] = ("访问文档库", "Access your documents library"),
            ["removableStorage"] = ("访问可移动存储", "Access removable storage"),
            ["objects3D"] = ("访问 3D 对象库", "Access your 3D objects"),
            
            // 用户信息
            ["contacts"] = ("访问联系人", "Access your contacts"),
            ["appointments"] = ("访问日历", "Access your calendar"),
            ["phoneCall"] = ("拨打电话", "Make phone calls"),
            ["phoneCallHistory"] = ("访问通话记录", "Access call history"),
            ["phoneCallHistoryPublic"] = ("访问公开通话记录", "Access public call history"),
            ["userAccountInformation"] = ("访问帐户信息", "Access your account information"),
            ["email"] = ("访问电子邮件", "Access your email"),
            ["chat"] = ("访问聊天消息", "Access your messages"),
            ["voipCall"] = ("进行 VoIP 通话", "Make VoIP calls"),
            ["userDataTasks"] = ("访问用户数据任务", "Access your tasks"),
            
            // 系统
            ["backgroundMediaPlayback"] = ("在后台播放媒体", "Play media in the background"),
            ["backgroundMediaRecording"] = ("在后台录制媒体", "Record media in the background"),
            ["remoteSystem"] = ("访问远程系统", "Access remote systems"),
            ["systemManagement"] = ("管理系统", "Manage system"),
            ["lowLevelDevices"] = ("访问低级设备", "Access low-level devices"),
            ["lowLevel"] = ("访问 GPIO、I2C、SPI 和 PWM 设备", "Access GPIO, I2C, SPI, and PWM devices"),
            ["broadFileSystemAccess"] = ("访问文件系统", "Access the file system"),
            ["enterpriseAuthentication"] = ("企业身份验证", "Enterprise authentication"),
            ["sharedUserCertificates"] = ("访问共享用户证书", "Access shared user certificates"),
            ["allJoyn"] = ("使用 AllJoyn 发现和交互", "Use AllJoyn"),
            ["codeGeneration"] = ("生成代码（JIT 功能）", "Generate code"),
            
            // 受限能力
            ["runFullTrust"] = ("以完全信任模式运行", "Run with full trust"),
            ["allowElevation"] = ("请求提升权限", "Request elevation"),
            ["appCaptureSettings"] = ("修改应用捕获设置", "Modify app capture settings"),
            ["appDiagnostics"] = ("访问应用诊断信息", "Access app diagnostics"),
            ["packageManagement"] = ("管理应用包", "Manage app packages"),
            ["packageQuery"] = ("查询应用包信息", "Query app packages"),
            ["gazeInput"] = ("使用眼球追踪输入", "Use gaze input"),
            ["globalMediaControl"] = ("全局媒体控制", "Global media control"),
            ["graphicsCapture"] = ("捕获屏幕内容", "Capture screen content"),
            ["spatialPerception"] = ("空间感知", "Spatial perception"),
            ["userNotificationListener"] = ("监听用户通知", "Listen to user notifications"),
            ["wiFiControl"] = ("扫描和连接 Wi-Fi 网络", "Scan and connect to Wi-Fi networks"),
            ["smsSend"] = ("发送短信和彩信", "Send SMS and MMS messages"),
            ["blockedChatMessages"] = ("读取被阻止的短信和彩信", "Read blocked SMS and MMS messages"),
        };

        /// <summary>
        /// 将 Capability ID 转换为用户友好的描述（支持多语言）
        /// </summary>
        public static string GetCapabilityDescription(string capabilityId)
        {
            if (string.IsNullOrWhiteSpace(capabilityId))
                return capabilityId;

            // 1. 尝试从资源文件加载
            try
            {
                var key = $"Capability_{capabilityId}";
                var description = _resourceLoader.GetString(key);
                if (!string.IsNullOrEmpty(description) && description != key)
                    return description;
            }
            catch { }

            // 2. 使用内置字典作为后备
            if (CapabilityDescriptions.TryGetValue(capabilityId, out var translations))
            {
                // 根据当前语言返回对应翻译
                var currentLanguage = Windows.Globalization.ApplicationLanguages.Languages[0];
                return currentLanguage.StartsWith("zh") ? translations.zhCN : translations.enUS;
            }

            // 3. 如果都没有，返回原始 ID
            return capabilityId;
        }

        public static async Task<PackageInfo> ReadAsync(string packagePath)
        {
            var info = new PackageInfo();
            try
            {
                var ext = Path.GetExtension(packagePath).ToLowerInvariant();
                if (ext is ".msixbundle" or ".appxbundle")
                {
                    using var bundle = ZipFile.OpenRead(packagePath);
                    // bundle manifest 里有 DisplayName 等
                    var bundleManifest = bundle.GetEntry("AppxBundleManifest.xml");
                    if (bundleManifest != null)
                    {
                        using var s = bundleManifest.Open();
                        info.ParseBundleManifest(s);
                    }
                    // 进第一个 .msix 读详细信息和图标
                    var inner = bundle.Entries.FirstOrDefault(e =>
                        e.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.EndsWith(".appx", StringComparison.OrdinalIgnoreCase));
                    if (inner != null)
                    {
                        using var innerStream = inner.Open();
                        // ZipArchive 需要可 seek 的流，先复制到 MemoryStream
                        using var ms = new MemoryStream();
                        await innerStream.CopyToAsync(ms);
                        ms.Position = 0;
                        using var innerZip = new ZipArchive(ms, ZipArchiveMode.Read);
                        info.ParseManifest(innerZip);
                        await info.LoadIconAsync(innerZip);
                    }
                }
                else
                {
                    using var zip = ZipFile.OpenRead(packagePath);
                    info.ParseManifest(zip);
                    await info.LoadIconAsync(zip);
                }
            }
            catch { }
            return info;
        }

        private void ParseBundleManifest(Stream stream)
        {
            try
            {
                var doc = XDocument.Load(stream);
                XNamespace ns = "http://schemas.microsoft.com/appx/2013/bundle";
                var identity = doc.Root?.Element(ns + "Identity");
                if (identity != null)
                {
                    Version = identity.Attribute("Version")?.Value;
                    Publisher = identity.Attribute("Publisher")?.Value;
                    IdentityName = identity.Attribute("Name")?.Value;
                }
            }
            catch { }
        }

        private void ParseManifest(ZipArchive zip)
        {
            try
            {
                var entry = zip.GetEntry("AppxManifest.xml");
                if (entry == null) return;

                using var stream = entry.Open();
                var doc = XDocument.Load(stream);
                XNamespace ns  = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
                XNamespace uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
                XNamespace rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";
                XNamespace iot = "http://schemas.microsoft.com/appx/manifest/iot/windows10";
                XNamespace desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";

                var identity = doc.Root?.Element(ns + "Identity");
                IdentityName ??= identity?.Attribute("Name")?.Value;
                Version      ??= identity?.Attribute("Version")?.Value;
                Publisher    ??= identity?.Attribute("Publisher")?.Value;

                // DisplayName 在 Properties 或 Applications/Application/VisualElements
                var props = doc.Root?.Element(ns + "Properties");
                var rawName = props?.Element(ns + "DisplayName")?.Value;
                if (!string.IsNullOrEmpty(rawName) && !rawName.StartsWith("ms-resource:"))
                    DisplayName = rawName;
                DisplayName ??= IdentityName?.Split('.').LastOrDefault();

                // Capabilities - 支持多个命名空间
                var caps = doc.Root?.Element(ns + "Capabilities");
                if (caps != null)
                {
                    // 遍历所有子元素，不限定命名空间
                    foreach (var cap in caps.Elements())
                    {
                        var name = cap.Attribute("Name")?.Value;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            // 添加能力名称，如果有命名空间前缀则保留
                            var localName = cap.Name.LocalName;
                            if (localName == "Capability" || localName == "DeviceCapability")
                            {
                                // 转换为用户友好的描述
                                var description = GetCapabilityDescription(name);
                                Capabilities.Add(description);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private async Task LoadIconAsync(ZipArchive zip)
        {
            try
            {
                // 优先找 Square44x44Logo，其次 Square150x150Logo，再找任意 Logo
                var candidates = new[]
                {
                    "Assets/Square44x44Logo.scale-200.png",
                    "Assets/Square44x44Logo.scale-100.png",
                    "Assets/Square150x150Logo.scale-200.png",
                    "Assets/Square150x150Logo.scale-100.png",
                };

                ZipArchiveEntry? iconEntry = null;
                foreach (var c in candidates)
                {
                    iconEntry = zip.GetEntry(c);
                    if (iconEntry != null) break;
                }
                // fallback：找任意 png
                iconEntry ??= zip.Entries.FirstOrDefault(e =>
                    e.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                    e.FullName.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase));

                if (iconEntry == null) return;

                using var s = iconEntry.Open();
                using var ms = new MemoryStream();
                await s.CopyToAsync(ms);
                ms.Position = 0;

                var bmp = new BitmapImage();
                using var ras = new InMemoryRandomAccessStream();
                await ras.WriteAsync(ms.ToArray().AsBuffer());
                ras.Seek(0);
                await bmp.SetSourceAsync(ras);
                Icon = bmp;
            }
            catch { }
        }
    }
}
