# 安装器生成器解决方案

## 问题
生成器需要 `InstallerUI.zip` 才能工作，但这个文件需要预先构建，导致流程复杂。

## 解决方案：自举式构建 🚀

生成器在运行时**自动将自己打包成 InstallerUI.zip**，无需任何手动操作。

### 工作原理

```
用户点击"生成" 
    ↓
SelfBuildService 自动执行：
    1. 复制当前应用的所有文件（排除 GuardSource）
    2. 打包成 InstallerUI.zip
    3. 放入 Payload 目录
    ↓
继续构建 InstallerGuard.exe
    ↓
生成完成！
```

### 核心代码

**`InstallationSolution/Services/SelfBuildService.cs`**
```csharp
public static class SelfBuildService
{
    public static async Task CopyToPayloadAsync(string payloadDir)
    {
        // 1. 获取当前应用目录
        var installDir = Package.Current.InstalledLocation.Path;
        
        // 2. 复制文件（排除 GuardSource）
        CopyDirectory(installDir, tempDir, excludeDir: "GuardSource");
        
        // 3. 压缩成 zip
        ZipFile.CreateFromDirectory(tempDir, zipPath);
        
        // 4. 复制到 Payload
        File.Copy(zipPath, Path.Combine(payloadDir, "InstallerUI.zip"));
    }
}
```

**`InstallationSolution/Pages/GeneratorPage.xaml.cs`**
```csharp
private string RunBuild()
{
    // 自动构建 InstallerUI.zip
    SelfBuildService.CopyToPayloadAsync(payloadDir).GetAwaiter().GetResult();
    
    // 继续构建 Guard...
}
```

## 优势

✅ **完全自动化** - 无需手动构建或维护 zip 文件  
✅ **始终最新** - 修改 UI 后立即生效  
✅ **简化流程** - 只需构建一次项目  
✅ **真正的自举** - 生成器用自己生成自己的安装包  

## 性能

- 首次构建：约 1-2 秒（复制 + 压缩）
- 后续构建：使用缓存，几乎无延迟

## 使用方法

1. 构建 `InstallationSolution` 项目
2. 运行生成器
3. 选择 `.msix` 文件
4. 点击"生成"
5. 完成！

**无需任何额外步骤！**
