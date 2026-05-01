using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace InstallationSolution.Services
{
    /// <summary>
    /// 自举式构建服务：在运行时构建 InstallerUI.zip
    /// </summary>
    public static class SelfBuildService
    {
        private static string? _cachedZipPath;
        private static readonly object _lock = new();

        /// <summary>
        /// 获取或构建 InstallerUI.zip
        /// </summary>
        public static async Task<string> GetOrBuildInstallerUIZipAsync()
        {
            lock (_lock)
            {
                if (_cachedZipPath != null && File.Exists(_cachedZipPath))
                    return _cachedZipPath;
            }

            // 构建 InstallerUI.zip
            var installDir = Package.Current.InstalledLocation.Path;
            var tempDir = Path.Combine(Path.GetTempPath(), "InstallationSolution_SelfBuild");
            Directory.CreateDirectory(tempDir);

            var zipPath = Path.Combine(tempDir, "InstallerUI.zip");

            // 如果已存在且有效，直接使用
            if (File.Exists(zipPath))
            {
                try
                {
                    // 验证 zip 文件完整性（确保 using 正确释放）
                    using (var test = ZipFile.OpenRead(zipPath))
                    {
                        // 只是验证能打开，不做其他操作
                    }
                    lock (_lock) { _cachedZipPath = zipPath; }
                    return zipPath;
                }
                catch
                {
                    // 文件损坏或被占用，尝试删除
                    await TryDeleteFileAsync(zipPath);
                }
            }

            // 创建 InstallerUI 目录结构
            var uiDir = Path.Combine(tempDir, "InstallerUI");
            if (Directory.Exists(uiDir))
            {
                try { Directory.Delete(uiDir, true); } catch { }
            }
            Directory.CreateDirectory(uiDir);

            // 复制当前应用的所有文件（排除 GuardSource）
            await Task.Run(() => CopyDirectory(installDir, uiDir, excludeDir: "GuardSource"));

            // 压缩成 zip（确保文件不存在）
            if (File.Exists(zipPath))
            {
                var deleted = await TryDeleteFileAsync(zipPath);
                if (!deleted)
                {
                    // 如果删除失败，使用新的文件名
                    zipPath = Path.Combine(tempDir, $"InstallerUI_{Guid.NewGuid():N}.zip");
                }
            }

            await Task.Run(() => 
                ZipFile.CreateFromDirectory(
                    Path.GetDirectoryName(uiDir)!, 
                    zipPath, 
                    CompressionLevel.Optimal, 
                    includeBaseDirectory: true));

            // 清理临时目录
            try { Directory.Delete(uiDir, true); } catch { }

            lock (_lock) { _cachedZipPath = zipPath; }
            return zipPath;
        }

        /// <summary>
        /// 将 InstallerUI.zip 复制到 Payload 目录
        /// </summary>
        public static async Task CopyToPayloadAsync(string payloadDir)
        {
            var zipPath = await GetOrBuildInstallerUIZipAsync();
            var destPath = Path.Combine(payloadDir, "InstallerUI.zip");

            Directory.CreateDirectory(payloadDir);
            
            // 如果目标文件存在，先删除
            if (File.Exists(destPath))
            {
                try { File.Delete(destPath); } catch { }
            }

            // 重试机制：最多尝试 3 次
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    File.Copy(zipPath, destPath, overwrite: true);
                    return;
                }
                catch (IOException) when (i < 2)
                {
                    // 等待后重试
                    await Task.Delay(200);
                }
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir, string? excludeDir = null)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");

            Directory.CreateDirectory(destDir);

            foreach (var file in dir.GetFiles())
            {
                try
                {
                    var targetPath = Path.Combine(destDir, file.Name);
                    file.CopyTo(targetPath, overwrite: true);
                }
                catch
                {
                    // 跳过无法复制的文件（可能被锁定）
                }
            }

            foreach (var subDir in dir.GetDirectories())
            {
                // 排除指定目录
                if (excludeDir != null && subDir.Name.Equals(excludeDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                var newDestDir = Path.Combine(destDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestDir, excludeDir);
            }
        }

        /// <summary>
        /// 安全删除文件，带重试机制
        /// </summary>
        private static async Task<bool> TryDeleteFileAsync(string filePath, int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                    return true;
                }
                catch (IOException)
                {
                    if (i < maxRetries - 1)
                        await Task.Delay(100);
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        /// <summary>
        /// 清理缓存
        /// </summary>
        public static void ClearCache()
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "InstallationSolution_SelfBuild");
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                lock (_lock) { _cachedZipPath = null; }
            }
            catch { }
        }
    }
}
