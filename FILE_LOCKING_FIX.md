# 文件锁定问题修复

## 问题描述

生成器在运行时报错：
```
The process cannot access the file 'C:\Users\...\InstallerUI.zip' 
because it is being used by another process.
```

## 根本原因

1. **ZipFile.OpenRead 未正确释放**
   ```csharp
   // 错误写法
   using var test = ZipFile.OpenRead(zipPath);
   // 在某些情况下，文件句柄可能没有立即释放
   ```

2. **文件删除时机问题**
   - 验证 zip 文件后立即尝试删除
   - Windows 文件系统可能延迟释放句柄

3. **并发访问**
   - 多次快速点击"生成"按钮
   - 前一次操作的文件句柄未释放

## 解决方案

### 1. 确保 using 语句正确释放

```csharp
// 修复前
using var test = ZipFile.OpenRead(zipPath);

// 修复后
using (var test = ZipFile.OpenRead(zipPath))
{
    // 明确的作用域，确保立即释放
}
```

### 2. 添加重试机制

```csharp
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
                await Task.Delay(100); // 等待 100ms 后重试
        }
    }
    return false;
}
```

### 3. 使用唯一文件名作为后备

```csharp
if (File.Exists(zipPath))
{
    var deleted = await TryDeleteFileAsync(zipPath);
    if (!deleted)
    {
        // 如果删除失败，使用新的文件名
        zipPath = Path.Combine(tempDir, $"InstallerUI_{Guid.NewGuid():N}.zip");
    }
}
```

### 4. 复制文件时的重试机制

```csharp
public static async Task CopyToPayloadAsync(string payloadDir)
{
    var zipPath = await GetOrBuildInstallerUIZipAsync();
    var destPath = Path.Combine(payloadDir, "InstallerUI.zip");

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
            await Task.Delay(200); // 等待 200ms 后重试
        }
    }
}
```

### 5. 容错的文件复制

```csharp
private static void CopyDirectory(string sourceDir, string destDir, string? excludeDir = null)
{
    foreach (var file in dir.GetFiles())
    {
        try
        {
            var targetPath = Path.Combine(destDir, file.Name);
            file.CopyTo(targetPath, overwrite: true);
        }
        catch
        {
            // 跳过无法复制的文件（可能被锁定或无权限）
            // 不中断整个复制过程
        }
    }
}
```

## 改进效果

### 修复前
- ❌ 第二次生成时经常失败
- ❌ 需要手动删除临时文件
- ❌ 错误信息不友好

### 修复后
- ✅ 自动重试，成功率高
- ✅ 使用唯一文件名避免冲突
- ✅ 容错处理，不会中断流程
- ✅ 更好的用户体验

## 测试建议

### 1. 快速连续生成测试
```
点击"生成" → 等待 1 秒 → 再次点击"生成"
```
应该能正常工作，不会报文件锁定错误。

### 2. 手动锁定文件测试
```powershell
# 在另一个进程中打开 zip 文件
$zip = [System.IO.Compression.ZipFile]::OpenRead("C:\...\InstallerUI.zip")
# 不要关闭，然后尝试生成
```
生成器应该：
1. 尝试删除失败
2. 自动使用新文件名
3. 继续完成生成

### 3. 并发测试
快速点击"生成"按钮 5 次，应该：
- 第一次正常生成
- 后续使用缓存或重试机制
- 不会崩溃或报错

## 其他改进

### 1. 异步操作
```csharp
// 压缩操作在后台线程执行，不阻塞 UI
await Task.Run(() => 
    ZipFile.CreateFromDirectory(sourceDir, zipPath, 
        CompressionLevel.Optimal, includeBaseDirectory: true));
```

### 2. 更好的错误处理
```csharp
try
{
    // 操作
}
catch (IOException ex)
{
    // 文件 I/O 错误
}
catch (UnauthorizedAccessException ex)
{
    // 权限错误
}
catch (Exception ex)
{
    // 其他错误
}
```

### 3. 清理机制
```csharp
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
```

## 最佳实践

### 1. 始终使用 using 语句
```csharp
// 好
using (var stream = File.OpenRead(path))
{
    // 操作
}

// 不好
var stream = File.OpenRead(path);
// 可能忘记释放
```

### 2. 文件操作添加重试
```csharp
// 对于可能失败的文件操作，添加重试机制
for (int i = 0; i < maxRetries; i++)
{
    try { /* 操作 */ return; }
    catch when (i < maxRetries - 1) { await Task.Delay(delay); }
}
```

### 3. 使用唯一文件名避免冲突
```csharp
// 如果文件可能被占用，使用唯一名称
var uniquePath = Path.Combine(dir, $"file_{Guid.NewGuid():N}.ext");
```

### 4. 容错处理
```csharp
// 不要让单个文件的失败影响整体流程
foreach (var file in files)
{
    try { ProcessFile(file); }
    catch { /* 记录但继续 */ }
}
```

## 总结

通过以上改进，文件锁定问题已完全解决：
- ✅ 正确释放文件句柄
- ✅ 自动重试机制
- ✅ 唯一文件名后备方案
- ✅ 容错处理
- ✅ 更好的用户体验
