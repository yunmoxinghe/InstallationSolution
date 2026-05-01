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
                {
                    try
                    {
                        // 验证缓存文件
                        var cachedSize = new FileInfo(_cachedZipPath).Length;
                        if (cachedSize > 1000000) // 大于 1MB
                        {
                            Debug.WriteLine($"[SelfBuildService] 使用缓存: {_cachedZipPath} ({cachedSize} 字节)");
                            return _cachedZipPath;
                        }
                    }
                    catch { }
                }
            }

            // 构建 InstallerUI.zip
            var installDir = Package.Current.InstalledLocation.Path;
            var tempDir = Path.Combine(Path.GetTempPath(), "InstallationSolution_SelfBuild");
            Directory.CreateDirectory(tempDir);

            // 使用唯一的文件名避免冲突
            var zipPath = Path.Combine(tempDir, $"InstallerUI_{Guid.NewGuid():N}.zip");
            Debug.WriteLine($"[SelfBuildService] 创建新的 ZIP: {zipPath}");

            // 创建 InstallerUI 目录结构
            var uiDir = Path.Combine(tempDir, "InstallerUI");
            if (Directory.Exists(uiDir))
            {
                try { Directory.Delete(uiDir, true); } catch { }
            }
            Directory.CreateDirectory(uiDir);

            // 复制当前应用的所有文件（排除 GuardSource）
            Debug.WriteLine($"[SelfBuildService] 开始复制文件从 {installDir} 到 {uiDir}");
            await Task.Run(() => CopyDirectory(installDir, uiDir, excludeDir: "GuardSource"));
            
            // 检查复制的文件
            var fileCount = Directory.GetFiles(uiDir, "*", SearchOption.AllDirectories).Length;
            Debug.WriteLine($"[SelfBuildService] 已复制 {fileCount} 个文件");
            
            // 检查关键文件
            var exeFile = Path.Combine(uiDir, "InstallationSolution.exe");
            var priFile = Path.Combine(uiDir, "resources.pri");
            Debug.WriteLine($"[SelfBuildService] InstallationSolution.exe 存在: {File.Exists(exeFile)}");
            Debug.WriteLine($"[SelfBuildService] resources.pri 存在: {File.Exists(priFile)}");
            
            if (!File.Exists(priFile))
            {
                Debug.WriteLine($"[SelfBuildService] 警告: 缺少 resources.pri，可能导致运行时错误");
            }

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

            Debug.WriteLine($"[SelfBuildService] 开始压缩到 {zipPath}");
            Debug.WriteLine($"[SelfBuildService] 源目录: {Path.GetDirectoryName(uiDir)}");
            
            await Task.Run(() => 
            {
                try
                {
                    // 确保目标文件不存在
                    if (File.Exists(zipPath))
                    {
                        File.Delete(zipPath);
                    }
                    
                    // 手动创建 ZIP，跳过被占用的文件
                    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                    {
                        var sourceDir = Path.GetDirectoryName(uiDir)!;
                        AddDirectoryToZip(archive, sourceDir, sourceDir);
                    }
                    
                    Debug.WriteLine($"[SelfBuildService] 压缩完成");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SelfBuildService] 压缩失败: {ex.Message}");
                    throw;
                }
            });
            
            // 验证 ZIP 文件
            var zipInfo = new FileInfo(zipPath);
            Debug.WriteLine($"[SelfBuildService] ZIP 文件大小: {zipInfo.Length} 字节");
            
            if (zipInfo.Length < 1000)
            {
                throw new InvalidOperationException($"ZIP 文件太小 ({zipInfo.Length} 字节)，可能创建失败");
            }

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
            Debug.WriteLine($"[SelfBuildService] CopyToPayloadAsync 开始");
            Debug.WriteLine($"[SelfBuildService] payloadDir: {payloadDir}");
            
            var zipPath = await GetOrBuildInstallerUIZipAsync();
            Debug.WriteLine($"[SelfBuildService] zipPath: {zipPath}");
            Debug.WriteLine($"[SelfBuildService] zipPath exists: {File.Exists(zipPath)}");
            
            var destPath = Path.Combine(payloadDir, "InstallerUI.zip");
            Debug.WriteLine($"[SelfBuildService] destPath: {destPath}");

            Directory.CreateDirectory(payloadDir);
            Debug.WriteLine($"[SelfBuildService] Payload 目录已创建");
            
            // 如果目标文件存在，先删除
            if (File.Exists(destPath))
            {
                try { File.Delete(destPath); Debug.WriteLine($"[SelfBuildService] 已删除旧文件"); } catch { }
            }

            // 重试机制：最多尝试 3 次
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    File.Copy(zipPath, destPath, overwrite: true);
                    Debug.WriteLine($"[SelfBuildService] 文件复制成功 (尝试 {i + 1})");
                    Debug.WriteLine($"[SelfBuildService] 目标文件大小: {new FileInfo(destPath).Length} bytes");
                    return;
                }
                catch (IOException ex) when (i < 2)
                {
                    Debug.WriteLine($"[SelfBuildService] 复制失败 (尝试 {i + 1}): {ex.Message}");
                    // 等待后重试
                    await Task.Delay(200);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SelfBuildService] 复制出错: {ex.Message}");
                    throw;
                }
            }
            
            throw new IOException($"无法复制 InstallerUI.zip 到 {destPath}");
        }

        private static void AddDirectoryToZip(ZipArchive archive, string sourceDir, string baseDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            
            foreach (var file in dir.GetFiles())
            {
                // 跳过 ZIP 文件（避免嵌套）
                if (file.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[SelfBuildService] 跳过 ZIP 文件: {file.Name}");
                    continue;
                }
                
                try
                {
                    var entryName = Path.GetRelativePath(baseDir, file.FullName).Replace('\\', '/');
                    
                    // 使用共享读取模式打开文件
                    using (var fileStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                        using (var entryStream = entry.Open())
                        {
                            fileStream.CopyTo(entryStream);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 跳过无法访问的文件
                    Debug.WriteLine($"[SelfBuildService] 跳过文件 {file.Name}: {ex.Message}");
                }
            }
            
            foreach (var subDir in dir.GetDirectories())
            {
                AddDirectoryToZip(archive, subDir.FullName, baseDir);
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
                    
                    // 使用文件共享模式复制，允许其他进程读取
                    using (var sourceStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var destStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        sourceStream.CopyTo(destStream);
                    }
                }
                catch (Exception ex)
                {
                    // 跳过无法复制的文件（可能被锁定）
                    Debug.WriteLine($"[SelfBuildService] 跳过文件 {file.Name}: {ex.Message}");
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
        private static async Task<bool> TryDeleteFileAsync(string filePath, int maxRetries = 5)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        Debug.WriteLine($"[SelfBuildService] 文件删除成功: {filePath}");
                    }
                    return true;
                }
                catch (IOException ex)
                {
                    Debug.WriteLine($"[SelfBuildService] 删除文件失败 (尝试 {i + 1}/{maxRetries}): {ex.Message}");
                    if (i < maxRetries - 1)
                    {
                        await Task.Delay(500); // 等待更长时间
                        GC.Collect(); // 强制垃圾回收，释放文件句柄
                        GC.WaitForPendingFinalizers();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SelfBuildService] 删除文件出错: {ex.Message}");
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
                {
                    // 删除所有旧的 ZIP 文件
                    foreach (var file in Directory.GetFiles(tempDir, "InstallerUI*.zip"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
                lock (_lock) { _cachedZipPath = null; }
            }
            catch { }
        }
    }
}
