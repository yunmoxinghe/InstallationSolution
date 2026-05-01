# 测试指南

## 验证自举式构建是否工作

### 1. 构建项目
```powershell
# 构建 InstallationSolution
dotnet build InstallationSolution/InstallationSolution.csproj -c Debug -p:Platform=x64
```

### 2. 运行生成器
- 启动 InstallationSolution 应用
- 使用 `-generator` 参数或直接导航到生成器页面

### 3. 测试生成流程
1. 点击"浏览"选择一个 `.msix` 或 `.msixbundle` 文件
2. 选择输出目录
3. 点击"生成"按钮

### 4. 观察日志
生成过程中会：
- ✅ 自动创建 `InstallerUI.zip`（约 1-2 秒）
- ✅ 复制用户选择的 msix 到 Payload
- ✅ 编译 InstallerGuard
- ✅ 输出最终的 `.exe` 文件

### 5. 验证结果
检查输出目录中的 `*_Installer.exe`：
```powershell
# 运行生成的安装器
.\YourApp_Installer.exe
```

应该能看到：
1. Toast 通知"正在启动安装器"
2. 安装器 UI 正常显示
3. 可以正常安装 msix 包

## 调试技巧

### 查看临时文件
```powershell
# 查看自举构建的临时目录
explorer $env:TEMP\InstallationSolution_SelfBuild
```

### 验证 InstallerUI.zip 内容
```powershell
# 解压查看
Expand-Archive -Path "$env:TEMP\InstallationSolution_SelfBuild\InstallerUI.zip" -DestinationPath ".\test_extract"
```

应该包含：
- `InstallerUI/InstallerUI.exe`
- `InstallerUI/*.dll`
- `InstallerUI/Assets/*`
- 但**不包含** `GuardSource` 目录

### 性能测试
```csharp
// 在 GeneratorPage.xaml.cs 中添加计时
var sw = Stopwatch.StartNew();
SelfBuildService.CopyToPayloadAsync(payloadDir).GetAwaiter().GetResult();
sw.Stop();
Debug.WriteLine($"InstallerUI.zip 构建耗时: {sw.ElapsedMilliseconds}ms");
```

预期：
- 首次构建：1000-2000ms
- 后续构建（缓存）：< 100ms

## 常见问题

### Q: 生成失败，提示找不到文件
**A:** 确保 InstallationSolution 已经正确构建并发布。检查 `Package.Current.InstalledLocation.Path` 是否有效。

### Q: InstallerUI.zip 太大
**A:** 检查是否正确排除了 GuardSource 目录。可以在 `SelfBuildService.cs` 中添加更多排除规则：
```csharp
// 排除多个目录
if (excludeDirs.Contains(subDir.Name, StringComparer.OrdinalIgnoreCase))
    continue;
```

### Q: 生成的安装器无法运行
**A:** 验证 InstallerUI.zip 的结构：
```
InstallerUI.zip
└── InstallerUI/
    ├── InstallerUI.exe
    ├── *.dll
    └── Assets/
```

确保目录结构正确（有 `InstallerUI` 父目录）。

## 性能优化建议

### 1. 添加进度提示
```csharp
StatusText.Text = "正在构建 InstallerUI.zip...";
await SelfBuildService.CopyToPayloadAsync(payloadDir);
StatusText.Text = "正在编译 Guard...";
```

### 2. 异步构建
```csharp
private async Task<string> RunBuildAsync()
{
    await SelfBuildService.CopyToPayloadAsync(payloadDir);
    // ...
}
```

### 3. 缓存验证
首次运行后，后续构建会使用缓存，几乎无延迟。
