# 安装器生成器优雅方案

## 问题描述

安装器生成器需要在运行时动态生成自安装包，但依赖预先构建好的 `InstallerUI.zip`。这导致：
1. 需要手动构建 InstallerUI 并打包成 zip
2. 构建流程复杂，容易出错
3. 更新 UI 后需要重新打包

## 解决方案

### 方案 1：嵌入资源方式 ⭐

**原理**：将 `InstallerUI.zip` 作为嵌入资源打包到 InstallationSolution 中，运行时提取。

**优点**：
- ✅ 简单可靠，zip 文件在编译时确定
- ✅ 不需要运行时构建，启动快
- ✅ 文件完整性有保障

**缺点**：
- ❌ 需要预先构建 InstallerUI.zip
- ❌ 更新 UI 需要重新构建整个项目

**实现**：
```csharp
// InstallationSolution/Services/InstallerUIProvider.cs
public static class InstallerUIProvider
{
    public static async Task<string> GetInstallerUIZipAsync()
    {
        // 从嵌入资源提取到临时目录
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("InstallationSolution.Resources.InstallerUI.zip");
        // ... 提取逻辑
    }
}
```

**项目配置**：
```xml
<ItemGroup>
    <EmbeddedResource Include="..\InstallerGuard\Payload\InstallerUI.zip">
        <Link>Resources\InstallerUI.zip</Link>
    </EmbeddedResource>
</ItemGroup>
```

---

### 方案 2：自举式构建（推荐）🚀

**原理**：生成器在运行时，将自己的应用文件复制并打包成 `InstallerUI.zip`。

**优点**：
- ✅ 完全自动化，无需预先构建 zip
- ✅ 始终使用最新的 UI 文件
- ✅ 构建流程简化，只需构建一次
- ✅ 真正的"自安装包生成器"

**缺点**：
- ❌ 首次运行时需要构建，稍慢（约 1-2 秒）
- ❌ 需要额外的磁盘空间（临时目录）

**实现**：
```csharp
// InstallationSolution/Services/SelfBuildService.cs
public static class SelfBuildService
{
    public static async Task<string> GetOrBuildInstallerUIZipAsync()
    {
        // 1. 获取当前应用安装目录
        var installDir = Package.Current.InstalledLocation.Path;
        
        // 2. 复制所有文件到临时目录（排除 GuardSource）
        CopyDirectory(installDir, tempDir, excludeDir: "GuardSource");
        
        // 3. 压缩成 InstallerUI.zip
        ZipFile.CreateFromDirectory(tempDir, zipPath);
        
        return zipPath;
    }
}
```

**使用方式**：
```csharp
// GeneratorPage.xaml.cs
private string RunBuild()
{
    // 自动构建 InstallerUI.zip
    SelfBuildService.CopyToPayloadAsync(payloadDir).GetAwaiter().GetResult();
    
    // 继续构建 Guard...
}
```

---

### 方案 3：混合方式（最佳实践）

结合方案 1 和方案 2 的优点：

```csharp
public static class HybridInstallerUIProvider
{
    public static async Task<string> GetInstallerUIZipAsync()
    {
        // 优先使用嵌入资源（如果存在）
        try
        {
            return await InstallerUIProvider.GetInstallerUIZipAsync();
        }
        catch
        {
            // 回退到自举式构建
            return await SelfBuildService.GetOrBuildInstallerUIZipAsync();
        }
    }
}
```

**优点**：
- ✅ 发布版本使用嵌入资源（快速、可靠）
- ✅ 开发版本使用自举构建（灵活、自动）
- ✅ 容错性强

---

## 推荐方案

### 开发阶段
使用 **方案 2（自举式构建）**：
- 无需手动维护 InstallerUI.zip
- 修改 UI 后立即生效
- 构建流程简单

### 发布阶段
使用 **方案 1（嵌入资源）** 或 **方案 3（混合方式）**：
- 性能更好
- 文件完整性有保障
- 用户体验更佳

---

## 实现细节

### 自举式构建的关键点

1. **排除 GuardSource 目录**
   ```csharp
   CopyDirectory(installDir, uiDir, excludeDir: "GuardSource");
   ```
   避免递归包含，减小 zip 体积。

2. **缓存机制**
   ```csharp
   private static string? _cachedZipPath;
   ```
   首次构建后缓存，避免重复构建。

3. **文件验证**
   ```csharp
   using var test = ZipFile.OpenRead(zipPath);
   ```
   确保 zip 文件完整有效。

### 嵌入资源的关键点

1. **资源命名**
   ```csharp
   const string ResourceName = "InstallationSolution.Resources.InstallerUI.zip";
   ```
   遵循 `命名空间.路径.文件名` 格式。

2. **项目配置**
   ```xml
   <EmbeddedResource Include="..." />
   ```
   确保文件被正确嵌入。

---

## 构建脚本（可选）

如果选择方案 1，可以使用此脚本预先构建 InstallerUI.zip：

```powershell
# Build-InstallerUI.ps1
param([string]$Configuration = 'Release', [string]$Platform = 'x64')

$publishDir = "InstallationSolution\bin\$Platform\$Configuration\net8.0-windows10.0.19041.0\win-$($Platform.ToLower())\publish"
$payloadDir = "InstallerGuard\Payload"

# 1. 发布 InstallationSolution
dotnet publish InstallationSolution\InstallationSolution.csproj -c $Configuration -p:Platform=$Platform

# 2. 创建 InstallerUI 目录结构
$tempDir = Join-Path $env:TEMP "InstallerUI_Build"
$uiDir = Join-Path $tempDir "InstallerUI"
New-Item -ItemType Directory -Path $uiDir -Force | Out-Null
Copy-Item -Path "$publishDir\*" -Destination $uiDir -Recurse -Force

# 3. 压缩
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null
Compress-Archive -Path $tempDir\* -DestinationPath "$payloadDir\InstallerUI.zip" -Force

# 4. 清理
Remove-Item $tempDir -Recurse -Force

Write-Host "InstallerUI.zip created successfully!" -ForegroundColor Green
```

---

## 总结

| 方案 | 适用场景 | 复杂度 | 性能 | 灵活性 |
|------|---------|--------|------|--------|
| 方案 1：嵌入资源 | 发布版本 | 中 | ⭐⭐⭐ | ⭐⭐ |
| 方案 2：自举构建 | 开发阶段 | 低 | ⭐⭐ | ⭐⭐⭐ |
| 方案 3：混合方式 | 全阶段 | 高 | ⭐⭐⭐ | ⭐⭐⭐ |

**最终推荐**：开发时使用方案 2，发布时使用方案 3。
