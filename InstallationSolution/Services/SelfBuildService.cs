using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using InstallationSolution.Constants;

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
                        if (cachedSize > AppConfig.MinValidZipSizeBytes)
                        {
                            Debug.WriteLine($"[SelfBuildService] 使用缓存: {_cachedZipPath} ({cachedSize} 字节)");
                            return _cachedZipPath;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SelfBuildService] 缓存验证失败: {ex.Message}");
                    }
                }
            }

            // 构建非打包版本的 InstallerUI
            var tempBuildDir = Path.Combine(Path.GetTempPath(), AppConfig.TempBuildDirPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempBuildDir);

            try
            {
                Debug.WriteLine($"[SelfBuildService] 开始构建非打包版本");
                
                // 获取当前项目路径
                var installDir = Package.Current.InstalledLocation.Path;
                var projectRoot = FindProjectRoot(installDir);
                
                if (projectRoot == null)
                {
                    throw new DirectoryNotFoundException("无法找到项目根目录");
                }
                
                var csprojPath = Path.Combine(projectRoot, "InstallationSolution.Unpackaged.csproj");
                if (!File.Exists(csprojPath))
                {
                    throw new FileNotFoundException($"找不到非打包项目文件: {csprojPath}");
                }
                
                Debug.WriteLine($"[SelfBuildService] 项目路径: {csprojPath}");
                
                // 使用简单的 publish 命令，不覆盖项目配置
                // 项目文件中已经配置了 WindowsPackageType=MSIX，但我们需要覆盖为 None
                var publishArgs = $"publish \"{csprojPath}\" -r win-x64 -c Release -o \"{tempBuildDir}\" --no-self-contained";
                
                Debug.WriteLine($"[SelfBuildService] 执行: dotnet {publishArgs}");
                
                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = publishArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                
                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    throw new Exception("无法启动 dotnet publish");
                }
                
                // 异步读取输出，避免阻塞 UI 线程
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                
                await proc.WaitForExitAsync();
                
                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                
                Debug.WriteLine($"[SelfBuildService] dotnet publish stdout:");
                Debug.WriteLine(stdout);
                
                if (proc.ExitCode != 0)
                {
                    Debug.WriteLine($"[SelfBuildService] dotnet publish 失败");
                    Debug.WriteLine($"[SelfBuildService] stdout: {stdout}");
                    Debug.WriteLine($"[SelfBuildService] stderr: {stderr}");
                    throw new Exception($"构建非打包版本失败 (exit {proc.ExitCode}):\n{stderr}");
                }
                
                Debug.WriteLine($"[SelfBuildService] 构建成功");
                
                // 检查构建产物
                var exeFile = Path.Combine(tempBuildDir, "InstallationSolution.exe");
                var priFile = Path.Combine(tempBuildDir, "InstallationSolution.pri");
                
                if (!File.Exists(exeFile))
                {
                    throw new FileNotFoundException($"构建产物不存在: {exeFile}");
                }
                
                if (!File.Exists(priFile))
                {
                    Debug.WriteLine($"[SelfBuildService] 警告: 缺少 InstallationSolution.pri");
                }
                
                Debug.WriteLine($"[SelfBuildService] InstallationSolution.exe 存在: {File.Exists(exeFile)}");
                Debug.WriteLine($"[SelfBuildService] InstallationSolution.pri 存在: {File.Exists(priFile)}");
                
                // 创建 ZIP
                var tempDir = Path.Combine(Path.GetTempPath(), AppConfig.TempSelfBuildDir);
                Directory.CreateDirectory(tempDir);
                
                var zipPath = Path.Combine(tempDir, string.Format(AppConfig.InstallerUIZipPattern, Guid.NewGuid().ToString("N")));
                Debug.WriteLine($"[SelfBuildService] 创建 ZIP: {zipPath}");
                
                // 创建 InstallerUI 目录结构
                var uiDir = Path.Combine(tempDir, "InstallerUI");
                if (Directory.Exists(uiDir))
                {
                    try { Directory.Delete(uiDir, true); } catch { }
                }
                Directory.CreateDirectory(uiDir);
                
                // 复制构建产物
                Debug.WriteLine($"[SelfBuildService] 复制构建产物到 {uiDir}");
                await Task.Run(() => CopyDirectory(tempBuildDir, uiDir, excludeDir: "GuardSource"));
                
                var fileCount = Directory.GetFiles(uiDir, "*", SearchOption.AllDirectories).Length;
                Debug.WriteLine($"[SelfBuildService] 已复制 {fileCount} 个文件");
                
                // 压缩成 zip
                if (File.Exists(zipPath))
                {
                    var deleted = await TryDeleteFileAsync(zipPath);
                    if (!deleted)
                    {
                        zipPath = Path.Combine(tempDir, string.Format(AppConfig.InstallerUIZipPattern, Guid.NewGuid().ToString("N")));
                    }
                }
                
                Debug.WriteLine($"[SelfBuildService] 开始压缩到 {zipPath}");
                
                await Task.Run(() =>
                {
                    try
                    {
                        if (File.Exists(zipPath))
                        {
                            File.Delete(zipPath);
                        }
                        
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
                Debug.WriteLine($"[SelfBuildService] ZIP 文件大小: {zipInfo.Length} 字节 ({zipInfo.Length / 1024.0 / 1024.0:F2} MB)");
                
                if (zipInfo.Length < AppConfig.MinValidZipSizeBytes)
                {
                    throw new InvalidOperationException($"ZIP 文件太小 ({zipInfo.Length} 字节)，可能创建失败");
                }
                
                // 清理临时目录
                try { Directory.Delete(uiDir, true); } catch { }
                try { Directory.Delete(tempBuildDir, true); } catch { }
                
                lock (_lock) { _cachedZipPath = zipPath; }
                return zipPath;
            }
            catch
            {
                // 清理失败时的临时目录
                try { Directory.Delete(tempBuildDir, true); } catch { }
                throw;
            }
        }

        /// <summary>
        /// 查找项目根目录
        /// </summary>
        private static string? FindProjectRoot(string startPath)
        {
            var dir = new DirectoryInfo(startPath);
            while (dir != null)
            {
                // 查找 .csproj 文件
                if (Directory.GetFiles(dir.FullName, "*.csproj").Length > 0)
                {
                    return dir.FullName;
                }
                
                // 向上一级
                dir = dir.Parent;
            }
            return null;
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
            
            var destPath = Path.Combine(payloadDir, AppConfig.InstallerUIZipName);
            Debug.WriteLine($"[SelfBuildService] destPath: {destPath}");

            Directory.CreateDirectory(payloadDir);
            Debug.WriteLine($"[SelfBuildService] Payload 目录已创建");
            
            // 如果目标文件存在，先删除
            if (File.Exists(destPath))
            {
                try 
                { 
                    File.Delete(destPath); 
                    Debug.WriteLine($"[SelfBuildService] 已删除旧文件"); 
                } 
                catch (Exception ex) 
                { 
                    Debug.WriteLine($"[SelfBuildService] 删除旧文件失败: {ex.Message}"); 
                }
            }

            // 重试机制：最多尝试配置的次数
            for (int i = 0; i < AppConfig.MaxRetryAttempts; i++)
            {
                try
                {
                    File.Copy(zipPath, destPath, overwrite: true);
                    Debug.WriteLine($"[SelfBuildService] 文件复制成功 (尝试 {i + 1})");
                    Debug.WriteLine($"[SelfBuildService] 目标文件大小: {new FileInfo(destPath).Length} bytes");
                    return;
                }
                catch (IOException ex) when (i < AppConfig.MaxRetryAttempts - 1)
                {
                    Debug.WriteLine($"[SelfBuildService] 复制失败 (尝试 {i + 1}): {ex.Message}");
                    // 等待后重试
                    await Task.Delay(AppConfig.FileOperationRetryDelayMs);
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
                
                // 跳过不需要的大型文件以减小体积
                if (ShouldExcludeFile(file.Name))
                {
                    Debug.WriteLine($"[SelfBuildService] 跳过大型文件: {file.Name}");
                    continue;
                }
                
                try
                {
                    var entryName = Path.GetRelativePath(baseDir, file.FullName).Replace('\\', '/');
                    
                    // 使用共享读取模式打开文件，并指定缓冲区大小
                    using (var fileStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, AppConfig.StreamCopyBufferSize))
                    {
                        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                        using (var entryStream = entry.Open())
                        {
                            fileStream.CopyTo(entryStream, AppConfig.StreamCopyBufferSize);
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
                // 跳过不需要的语言包目录（只保留中文和英文）
                if (IsLanguageDirectory(subDir.Name) && !IsNeededLanguage(subDir.Name))
                {
                    Debug.WriteLine($"[SelfBuildService] 跳过语言包: {subDir.Name}");
                    continue;
                }
                
                AddDirectoryToZip(archive, subDir.FullName, baseDir);
            }
        }

        private static bool IsLanguageDirectory(string dirName)
        {
            // 匹配语言代码格式：xx-XX
            return System.Text.RegularExpressions.Regex.IsMatch(dirName, @"^[a-z]{2}-[A-Z]{2}");
        }

        private static bool IsNeededLanguage(string dirName)
        {
            // 只保留中文和英文
            var needed = new[] { "zh-CN", "zh-TW", "zh-Hans", "zh-Hant", "en-US", "en-us", "en-GB" };
            return needed.Any(lang => dirName.Equals(lang, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ShouldExcludeFile(string fileName)
        {
            // 排除不需要的大型文件
            var excludeList = new[]
            {
                // AI/ML 相关（如果不使用）
                "onnxruntime.dll",      // 20 MB
                "DirectML.dll",         // 17 MB
                // 调试文件
                ".pdb",
                // 其他不需要的文件
                "vs.appxrecipe"
            };
            
            return excludeList.Any(exclude => 
                fileName.Equals(exclude, StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(exclude, StringComparison.OrdinalIgnoreCase));
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
                    
                    // 使用文件共享模式复制，允许其他进程读取，并指定缓冲区大小
                    using (var sourceStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, AppConfig.StreamCopyBufferSize))
                    using (var destStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, AppConfig.StreamCopyBufferSize))
                    {
                        sourceStream.CopyTo(destStream, AppConfig.StreamCopyBufferSize);
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
        private static async Task<bool> TryDeleteFileAsync(string filePath, int maxRetries = AppConfig.FileDeleteMaxRetries)
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
                        await Task.Delay(AppConfig.FileDeleteRetryDelayMs);
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
                var tempDir = Path.Combine(Path.GetTempPath(), AppConfig.TempSelfBuildDir);
                if (Directory.Exists(tempDir))
                {
                    // 删除所有旧的 ZIP 文件
                    foreach (var file in Directory.GetFiles(tempDir, "InstallerUI*.zip"))
                    {
                        try 
                        { 
                            File.Delete(file); 
                        } 
                        catch (Exception ex) 
                        { 
                            Debug.WriteLine($"[SelfBuildService] 删除缓存文件失败: {ex.Message}"); 
                        }
                    }
                }
                lock (_lock) { _cachedZipPath = null; }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SelfBuildService] 清理缓存失败: {ex.Message}");
            }
        }
    }
}
