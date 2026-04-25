# InstallationSolution

一个完整的 Windows 安装程序解决方案，基于 WinUI 3 和 .NET 构建。

## 项目概述

InstallationSolution 包含三个主要组件：

### 1. **InstallerGenerator** (安装程序生成器)
- 用于生成和配置安装程序的工具
- 基于 WinUI 3 开发
- 支持多架构：x64、ARM64
- 支持多语言：英文 (en-US)、中文 (zh-CN)

### 2. **InstallerGuard** (安装程序守护)
- .NET Console 应用程序
- 目标框架：.NET 10.0 和 .NET 8.0
- 支持多架构：x64、ARM64
- 包含 MSIX 包负载

### 3. **InstallerUI** (安装程序用户界面)
- WinUI 3 用户界面应用
- 支持多架构：x64、ARM64、Debug
- 包含自定义控件和对话框
- 多语言支持

## 技术栈

- **语言**: C#
- **UI 框架**: WinUI 3
- **.NET 版本**: .NET 10.0、.NET 8.0
- **应用类型**: MSIX/Desktop App

## 项目结构

```
InstallationSolution/
├── InstallerGenerator/     # 安装程序生成器 (WinUI 3)
│   ├── Pages/             # UI 页面 (主页、设置)
│   ├── Dialogs/           # 对话框组件
│   ├── Constants/         # 常量定义
│   └── Strings/           # 多语言资源
├── InstallerGuard/        # 安装程序守护程序 (.NET Console)
│   ├── Payload/           # 安装包
│   └── Properties/        # 发布配置
├── InstallerUI/           # 安装程序 UI (WinUI 3)
│   ├── Pages/             # UI 页面
│   ├── Controls/          # 自定义控件
│   ├── Dialogs/           # 对话框
│   ├── Models/            # 数据模型
│   ├── Themes/            # 主题配置
│   └── Strings/           # 多语言资源
└── README.md              # 本文件
```

## 开发要求

- Visual Studio 2022 或更新版本
- .NET SDK (包含 .NET 10.0 和 .NET 8.0)
- Windows 10/11 (推荐 Windows 11)
- C# 11 或更新版本支持

## 许可证

本项目遵循 LICENSE 文件中的条款。