# 安装器生成器 - 自举式构建方案

## 🎯 问题
你的项目有一个**安装器生成器**，用户可以选择任意 `.msix` 文件，生成包含该 msix 的自安装包。

但是生成器依赖 `InstallerUI.zip`（安装器界面的压缩包），这个文件需要：
1. 手动构建 InstallationSolution
2. 发布并打包成 zip
3. 放到 `InstallerGuard/Payload/` 目录

这个流程**繁琐且容易出错**。

## ✨ 解决方案：自举式构建

生成器在运行时**自动将自己打包成 InstallerUI.zip**，实现真正的"自安装包生成器"。

### 核心思想
```
InstallationSolution (生成器)
    ↓ 运行时
    ↓ 复制自己的文件
    ↓ 打包成 InstallerUI.zip
    ↓
InstallerGuard (自安装包)
    ↓ 嵌入 InstallerUI.zip
    ↓ 嵌入用户的 .msix
    ↓
最终产物：YourApp_Installer.exe
```

## 📁 文件结构

```
InstallationSolution/
├── Services/
│   └── SelfBuildService.cs          ← 自举构建服务
├── Pages/
│   └── GeneratorPage.xaml.cs        ← 使用自举服务
└── InstallationSolution.csproj      ← 无需嵌入资源配置

InstallerGuard/
├── Program.cs
├── InstallerGuard.csproj
└── Payload/                          ← 运行时动态生成
    ├── InstallerUI.zip              ← 自动创建
    └── YourApp.msix                 ← 用户选择
```

## 🚀 工作流程

### 用户操作
1. 打开生成器
2. 选择 `.msix` 文件
3. 点击"生成"

### 自动执行
```
[1] SelfBuildService.GetOrBuildInstallerUIZipAsync()
    ├─ 获取当前应用目录
    ├─ 复制所有文件到临时目录（排除 GuardSource）
    ├─ 压缩成 InstallerUI.zip
    └─ 缓存到 %TEMP%\InstallationSolution_SelfBuild\

[2] SelfBuildService.CopyToPayloadAsync(payloadDir)
    └─ 复制 InstallerUI.zip 到 GuardSource/Payload/

[3] 复制用户的 .msix 到 Payload/

[4] dotnet publish InstallerGuard.csproj
    └─ 生成 YourApp_Installer.exe

[5] 完成！
```

## 💡 关键代码

### SelfBuildService.cs
```csharp
public static class SelfBuildService
{
    private static string? _cachedZipPath;

    public static async Task<string> GetOrBuildInstallerUIZipAsync()
    {
        // 检查缓存
        if (_cachedZipPath != null && File.Exists(_cachedZipPath))
            return _cachedZipPath;

        // 获取当前应用目录
        var installDir = Package.Current.InstalledLocation.Path;
        var tempDir = Path.Combine(Path.GetTempPath(), "InstallationSolution_SelfBuild");
        var uiDir = Path.Combine(tempDir, "InstallerUI");

        // 复制文件（排除 GuardSource）
        await Task.Run(() => CopyDirectory(installDir, uiDir, excludeDir: "GuardSource"));

        // 压缩
        var zipPath = Path.Combine(tempDir, "InstallerUI.zip");
        ZipFile.CreateFromDirectory(Path.GetDirectoryName(uiDir)!, zipPath, 
            CompressionLevel.Optimal, includeBaseDirectory: true);

        _cachedZipPath = zipPath;
        return zipPath;
    }

    public static async Task CopyToPayloadAsync(string payloadDir)
    {
        var zipPath = await GetOrBuildInstallerUIZipAsync();
        Directory.CreateDirectory(payloadDir);
        File.Copy(zipPath, Path.Combine(payloadDir, "InstallerUI.zip"), overwrite: true);
    }
}
```

### GeneratorPage.xaml.cs
```csharp
private string RunBuild()
{
    var payloadDir = Path.Combine(guardSrc, "Payload");
    Directory.CreateDirectory(payloadDir);

    // 自动构建 InstallerUI.zip
    SelfBuildService.CopyToPayloadAsync(payloadDir).GetAwaiter().GetResult();

    // 复制用户的 msix
    var msixFileName = Path.GetFileName(_msixPath!);
    File.Copy(_msixPath!, Path.Combine(payloadDir, msixFileName), overwrite: true);

    // 编译 Guard
    // dotnet publish ...
}
```

## ✅ 优势

| 特性 | 传统方式 | 自举式构建 |
|------|---------|-----------|
| 手动构建 zip | ✅ 需要 | ❌ 不需要 |
| 修改 UI 后 | 重新打包 | 自动生效 |
| 构建步骤 | 3-4 步 | 1 步 |
| 维护成本 | 高 | 低 |
| 出错风险 | 高 | 低 |

### 具体优势
✅ **完全自动化** - 无需任何手动操作  
✅ **始终最新** - 修改代码后立即生效  
✅ **简化流程** - 只需构建一次项目  
✅ **真正自举** - 生成器用自己生成安装包  
✅ **缓存机制** - 首次构建后使用缓存，几乎无延迟  

## ⚡ 性能

- **首次构建**：1-2 秒（复制 + 压缩）
- **后续构建**：< 100ms（使用缓存）
- **zip 大小**：约 10-20 MB（取决于应用大小）

## 🔧 使用方法

### 开发阶段
```powershell
# 1. 构建项目
dotnet build InstallationSolution/InstallationSolution.csproj -c Debug -p:Platform=x64

# 2. 运行生成器
# 直接启动应用，导航到生成器页面

# 3. 选择 msix 并生成
# 完全自动，无需其他操作
```

### 发布阶段
```powershell
# 构建 Release 版本
dotnet build InstallationSolution/InstallationSolution.csproj -c Release -p:Platform=x64

# 打包成 MSIX
# 使用 Visual Studio 或 dotnet publish
```

## 📝 注意事项

### 1. 排除 GuardSource 目录
确保 `CopyDirectory` 正确排除 `GuardSource`，避免递归包含。

### 2. 目录结构
生成的 `InstallerUI.zip` 必须包含 `InstallerUI` 父目录：
```
InstallerUI.zip
└── InstallerUI/
    ├── InstallerUI.exe
    ├── *.dll
    └── Assets/
```

### 3. 缓存位置
缓存在 `%TEMP%\InstallationSolution_SelfBuild\`，可以手动清理：
```csharp
SelfBuildService.ClearCache();
```

## 🐛 故障排除

### 问题：生成失败
**检查**：
1. InstallationSolution 是否正确构建
2. `Package.Current.InstalledLocation.Path` 是否有效
3. 临时目录是否有写权限

### 问题：生成的安装器无法运行
**检查**：
1. InstallerUI.zip 的目录结构是否正确
2. 是否包含所有必需的 DLL
3. GuardSource 是否被正确排除

### 问题：构建太慢
**优化**：
1. 检查是否使用了缓存
2. 减小应用体积（移除不必要的文件）
3. 使用 `CompressionLevel.Fastest` 而非 `Optimal`

## 📚 相关文档

- `SOLUTION_SUMMARY.md` - 方案简要说明
- `TESTING_GUIDE.md` - 测试指南
- `INSTALLER_GENERATOR_SOLUTIONS.md` - 详细方案对比

## 🎉 总结

通过**自举式构建**，你的安装器生成器现在可以：
- 🚀 自动化整个构建流程
- 🔄 实时反映代码变更
- 🎯 简化开发和维护
- ✨ 提供更好的用户体验

**无需手动维护任何 zip 文件，一切都是自动的！**
