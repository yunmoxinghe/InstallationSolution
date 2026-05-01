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
                XNamespace ns2 = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

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

                // Capabilities
                var caps = doc.Root?.Element(ns + "Capabilities");
                if (caps != null)
                {
                    foreach (var cap in caps.Elements())
                    {
                        var name = cap.Attribute("Name")?.Value;
                        if (!string.IsNullOrWhiteSpace(name))
                            Capabilities.Add(name);
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
