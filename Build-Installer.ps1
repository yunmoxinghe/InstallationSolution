# Build-Installer.ps1
# 自动化构建完整的自安装包

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Building Installer Package" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "Platform: $Platform" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$rootDir = $PSScriptRoot
$installerUIProject = Join-Path $rootDir "InstallationSolution\InstallationSolution.csproj"
$guardProject = Join-Path $rootDir "InstallerGuard\InstallerGuard.csproj"
$payloadDir = Join-Path $rootDir "InstallerGuard\Payload"

# 1. 清理旧的 Payload
Write-Host "`n[1/5] Cleaning old payload..." -ForegroundColor Yellow
if (Test-Path $payloadDir) {
    Remove-Item $payloadDir -Recurse -Force
}
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

# 2. 构建 InstallationSolution (MSIX)
Write-Host "`n[2/5] Building InstallationSolution..." -ForegroundColor Yellow
dotnet publish $installerUIProject `
    -c $Configuration `
    -p:Platform=$Platform `
    -p:AppxBundle=Always `
    -p:AppxBundlePlatforms=$Platform `
    -p:GenerateAppxPackageOnBuild=true

if ($LASTEXITCODE -ne 0) {
    throw "Failed to build InstallationSolution"
}

# 3. 查找生成的 MSIX Bundle
Write-Host "`n[3/5] Locating MSIX bundle..." -ForegroundColor Yellow
$appPackagesDir = Join-Path $rootDir "InstallationSolution\bin\$Platform\$Configuration\net8.0-windows10.0.19041.0\$Platform\AppPackages"
$msixBundle = Get-ChildItem -Path $appPackagesDir -Filter "*.msixbundle" -Recurse | Select-Object -First 1

if (-not $msixBundle) {
    throw "MSIX bundle not found in $appPackagesDir"
}

Write-Host "Found: $($msixBundle.FullName)" -ForegroundColor Green
Copy-Item $msixBundle.FullName -Destination $payloadDir

# 4. 创建 InstallerUI.zip
Write-Host "`n[4/5] Creating InstallerUI.zip..." -ForegroundColor Yellow
$publishDir = Join-Path $rootDir "InstallationSolution\bin\$Platform\$Configuration\net8.0-windows10.0.19041.0\win-$($Platform.ToLower())\publish"

if (-not (Test-Path $publishDir)) {
    throw "Publish directory not found: $publishDir"
}

# 创建临时目录结构
$tempDir = Join-Path $env:TEMP "InstallerUI_$(Get-Random)"
$installerUIDir = Join-Path $tempDir "InstallerUI"
New-Item -ItemType Directory -Path $installerUIDir -Force | Out-Null

# 复制发布文件
Copy-Item -Path "$publishDir\*" -Destination $installerUIDir -Recurse -Force

# 压缩
$zipPath = Join-Path $payloadDir "InstallerUI.zip"
Compress-Archive -Path $tempDir\* -DestinationPath $zipPath -Force

# 清理临时目录
Remove-Item $tempDir -Recurse -Force

Write-Host "Created: $zipPath" -ForegroundColor Green

# 5. 构建 InstallerGuard
Write-Host "`n[5/5] Building InstallerGuard..." -ForegroundColor Yellow
dotnet publish $guardProject `
    -c $Configuration `
    -p:Platform=$Platform `
    -p:PublishSingleFile=true `
    -p:SelfContained=false

if ($LASTEXITCODE -ne 0) {
    throw "Failed to build InstallerGuard"
}

$guardOutput = Join-Path $rootDir "InstallerGuard\bin\$Platform\$Configuration\net8.0-windows10.0.19041.0\win-$($Platform.ToLower())\publish\InstallerGuard.exe"

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "Output: $guardOutput" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
