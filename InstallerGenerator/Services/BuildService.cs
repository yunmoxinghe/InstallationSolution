using InstallerGenerator.Constants;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace InstallerGenerator.Services;

/// <summary>
/// 封装将 MSIX 包编译为独立安装器的构建逻辑
/// </summary>
public static class BuildService
{
    /// <summary>
    /// 构建安装器 exe
    /// </summary>
    /// <param name="msixPath">源 MSIX 文件路径</param>
    /// <param name="outputDir">输出目录</param>
    /// <returns>生成的 exe 完整路径</returns>
    public static string Build(string msixPath, string outputDir)
    {
        var guardSrc   = GetGuardSourceDir();
        var csproj     = Path.Combine(guardSrc, AppConstants.Build.GuardCsproj);
        var payloadDir = Path.Combine(guardSrc, AppConstants.Build.PayloadDir);

        ValidateGuardSource(csproj);
        CopyPayload(msixPath, payloadDir);

        var publishOut    = Path.Combine(Path.GetTempPath(), AppConstants.Build.TempDirPrefix + Guid.NewGuid().ToString("N"));
        var outputExeName = Path.GetFileNameWithoutExtension(msixPath) + AppConstants.Build.InstallerSuffix;

        RunDotnetPublish(csproj, publishOut);

        var destPath = Path.Combine(outputDir, outputExeName);
        CopyOutput(publishOut, destPath);
        CleanupTemp(publishOut);
        RefreshDesktopIfNeeded(outputDir);

        return destPath;
    }

    // ── 私有步骤 ─────────────────────────────────────────────────────

    private static string GetGuardSourceDir()
        => Path.Combine(Package.Current.InstalledLocation.Path, AppConstants.Build.GuardSourceDir);

    private static void ValidateGuardSource(string csproj)
    {
        if (!File.Exists(csproj))
            throw new FileNotFoundException($"找不到 Guard 源码：{csproj}");
    }

    private static void CopyPayload(string msixPath, string payloadDir)
    {
        // 清理旧 payload
        foreach (var ext in AppConstants.FileExtensions.Package)
            foreach (var f in Directory.GetFiles(payloadDir, $"*{ext}"))
                File.Delete(f);

        File.Copy(msixPath, Path.Combine(payloadDir, Path.GetFileName(msixPath)), overwrite: true);
    }

    private static void RunDotnetPublish(string csproj, string publishOut)
    {
        var rid  = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        var args = string.Format(AppConstants.Build.PublishArgs, csproj, publishOut, rid);
        var psi  = new ProcessStartInfo
        {
            FileName               = AppConstants.Build.DotnetExe,
            Arguments              = args,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 dotnet publish");

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"dotnet publish 失败 (exit {proc.ExitCode}):\n{stderr}\n{stdout}");
    }

    private static void CopyOutput(string publishOut, string destPath)
    {
        var exeFiles = Directory.GetFiles(publishOut, AppConstants.Build.GuardExe, SearchOption.AllDirectories);
        if (exeFiles.Length == 0)
            throw new FileNotFoundException($"找不到编译产物 {AppConstants.Build.GuardExe}");

        File.Copy(exeFiles[0], destPath, overwrite: true);
    }

    private static void CleanupTemp(string publishOut)
    {
        try { Directory.Delete(publishOut, recursive: true); }
        catch (Exception ex) { Debug.WriteLine($"CleanupTemp failed: {ex.Message}"); }
    }

    private static void RefreshDesktopIfNeeded(string outputDir)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (string.Equals(Path.GetFullPath(outputDir), Path.GetFullPath(desktop),
                          StringComparison.OrdinalIgnoreCase))
            SHChangeNotify(AppConstants.Win32.SHCNE_ASSOCCHANGED,
                           AppConstants.Win32.SHCNF_IDLIST,
                           IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
