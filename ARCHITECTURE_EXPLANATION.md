# 架构说明：为什么不使用 MSBuild 自动化

## 两种使用场景

### 场景 1：直接构建 InstallerGuard（传统方式）
```
开发者 → 构建 InstallerGuard.csproj
    ↓
MSBuild 自动：
    1. 构建 InstallationSolution
    2. 打包成 InstallerUI.zip
    3. 嵌入到 Guard
    ↓
生成 InstallerGuard.exe
```

### 场景 2：使用生成器（当前方式）
```
用户 → 运行 InstallationSolution（生成器）
    ↓
选择 .msix 文件
    ↓
生成器运行时：
    1. 自举构建 InstallerUI.zip
    2. 复制 Guard 源码到临时目录
    3. dotnet publish Guard
    ↓
生成 YourApp_Installer.exe
```

## 为什么不能在 Guard 中使用 MSBuild 自动化？

### 问题：相对路径失效

当生成器运行时，Guard 源码被复制到：
```
C:\Users\...\AppX\GuardSource\
├── InstallerGuard.csproj
├── Program.cs
└── Payload\
    ├── InstallerUI.zip  ← 运行时生成
    └── YourApp.msix     ← 用户选择
```

但 `InstallerGuard.csproj` 中的 `ProjectReference` 指向：
```xml
<ProjectReference Include="..\InstallationSolution\InstallationSolution.csproj" />
```

这个相对路径在原始位置有效：
```
InstallationSolution/
├── InstallationSolution/
│   └── InstallationSolution.csproj  ← 存在
└── InstallerGuard/
    └── InstallerGuard.csproj
```

但在 AppX 目录中**无效**：
```
AppX\GuardSource\
└── InstallerGuard.csproj
    ↓ 尝试找 ..\InstallationSolution\InstallationSolution.csproj
    ↓ 
    ❌ 不存在！
```

### 错误信息
```
MSB3202: 未找到项目文件
"..\InstallationSolution\InstallationSolution.csproj"
```

## 正确的架构

### InstallerGuard.csproj（简化版）
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <PublishSingleFile>true</PublishSingleFile>
    <!-- ... -->
  </PropertyGroup>

  <ItemGroup>
    <!-- 只嵌入 Payload 目录下的文件 -->
    <EmbeddedResource Include="Payload\**\*" />
    <EmbeddedResource Include="notification.png" />
  </ItemGroup>

  <!-- 不需要 ProjectReference -->
  <!-- 不需要 MSBuild Target -->
</Project>
```

### 生成器负责准备 Payload

**`GeneratorPage.xaml.cs`**
```csharp
private string RunBuild()
{
    var payloadDir = Path.Combine(guardSrc, "Payload");
    Directory.CreateDirectory(payloadDir);

    // 1. 自举构建 InstallerUI.zip
    SelfBuildService.CopyToPayloadAsync(payloadDir).GetAwaiter().GetResult();

    // 2. 复制用户的 msix
    File.Copy(_msixPath!, Path.Combine(payloadDir, msixFileName), overwrite: true);

    // 3. 现在 Payload 目录完整了，可以构建 Guard
    // dotnet publish InstallerGuard.csproj
}
```

## 职责分离

| 组件 | 职责 |
|------|------|
| **InstallerGuard.csproj** | 简单的项目配置，嵌入 Payload 目录 |
| **SelfBuildService** | 自举构建 InstallerUI.zip |
| **GeneratorPage** | 协调整个生成流程 |

## 优势

### ✅ 简单
- Guard 项目配置简单，没有复杂的 MSBuild Target
- 不依赖外部项目引用

### ✅ 可移植
- Guard 源码可以复制到任何位置
- 只要 Payload 目录准备好，就能构建

### ✅ 灵活
- 生成器完全控制构建流程
- 可以动态决定 Payload 内容

### ✅ 可靠
- 没有相对路径问题
- 不会因为目录结构变化而失败

## 构建流程图

```
┌─────────────────────────────────────────┐
│ 用户运行 InstallationSolution（生成器） │
└────────────────┬────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────┐
│ 用户选择 .msix 文件                     │
└────────────────┬────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────┐
│ SelfBuildService.CopyToPayloadAsync()  │
│ ├─ 复制当前应用文件                     │
│ ├─ 排除 GuardSource                     │
│ └─ 打包成 InstallerUI.zip               │
└────────────────┬────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────┐
│ 准备 Payload 目录                       │
│ ├─ InstallerUI.zip                      │
│ └─ YourApp.msix                         │
└────────────────┬────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────┐
│ dotnet publish InstallerGuard.csproj    │
│ ├─ 嵌入 Payload\**\*                    │
│ └─ 生成单文件 exe                       │
└────────────────┬────────────────────────┘
                 │
                 ▼
┌─────────────────────────────────────────┐
│ 输出 YourApp_Installer.exe              │
└─────────────────────────────────────────┘
```

## 总结

- ❌ **不要**在 Guard 中使用 MSBuild 自动化
- ❌ **不要**在 Guard 中引用 InstallationSolution 项目
- ✅ **使用**简单的项目配置
- ✅ **让生成器**负责准备 Payload
- ✅ **使用**自举式构建

这样的架构更简单、更可靠、更灵活！
