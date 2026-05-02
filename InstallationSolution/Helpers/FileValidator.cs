using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace InstallationSolution.Helpers
{
    /// <summary>
    /// 文件验证辅助类
    /// </summary>
    public static class FileValidator
    {
        private static readonly string[] ValidMsixExtensions = { ".msix", ".msixbundle", ".appx", ".appxbundle" };
        private const long MaxMsixSizeBytes = 10L * 1024 * 1024 * 1024; // 10 GB
        private const long MinMsixSizeBytes = 1024; // 1 KB

        /// <summary>
        /// 验证是否为有效的 MSIX 文件
        /// </summary>
        public static bool IsMsixFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            if (!File.Exists(filePath))
                return false;

            // 检查扩展名
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (!ValidMsixExtensions.Contains(extension))
                return false;

            // 检查文件大小
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length < MinMsixSizeBytes || fileInfo.Length > MaxMsixSizeBytes)
                    return false;
            }
            catch
            {
                return false;
            }

            // 验证是否为有效的 ZIP 文件（MSIX 基于 ZIP 格式）
            try
            {
                using var archive = ZipFile.OpenRead(filePath);
                // 检查是否包含 AppxManifest.xml（MSIX 必需文件）
                return archive.Entries.Any(e => 
                    e.FullName.Equals("AppxManifest.xml", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 验证 ZIP 文件是否有效
        /// </summary>
        public static bool IsValidZipFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            try
            {
                using var archive = ZipFile.OpenRead(filePath);
                return archive.Entries.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 验证路径是否安全（防止路径遍历攻击）
        /// </summary>
        public static bool IsSafePath(string basePath, string targetPath)
        {
            try
            {
                var fullBasePath = Path.GetFullPath(basePath);
                var fullTargetPath = Path.GetFullPath(targetPath);
                
                return fullTargetPath.StartsWith(fullBasePath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 安全地提取 ZIP 文件（防止路径遍历攻击）
        /// </summary>
        public static void SafeExtractToDirectory(string zipPath, string destinationPath)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var fullDestPath = Path.GetFullPath(destinationPath);

            foreach (var entry in archive.Entries)
            {
                // 跳过目录条目
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                var entryPath = Path.Combine(destinationPath, entry.FullName);
                var fullEntryPath = Path.GetFullPath(entryPath);

                // 验证路径安全性
                if (!fullEntryPath.StartsWith(fullDestPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"检测到路径遍历攻击: {entry.FullName}");
                }

                // 创建目录
                var entryDir = Path.GetDirectoryName(fullEntryPath);
                if (!string.IsNullOrEmpty(entryDir))
                {
                    Directory.CreateDirectory(entryDir);
                }

                // 提取文件
                entry.ExtractToFile(fullEntryPath, overwrite: true);
            }
        }
    }
}
