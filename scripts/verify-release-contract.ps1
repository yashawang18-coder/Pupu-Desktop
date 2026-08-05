$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$version = & (Join-Path $PSScriptRoot "get-pupu-version.ps1")

$desktopProject = Get-Content (Join-Path $root "Pupu.Desktop\Pupu.Desktop.csproj") -Raw
$installerProject = Get-Content (Join-Path $root "Pupu.Installer\Pupu.Installer.csproj") -Raw
$installerProgram = Get-Content (Join-Path $root "Pupu.Installer\Program.cs") -Raw
$installerAssembly = Get-Content (Join-Path $root "Pupu.Installer\AssemblyInfo.cs") -Raw
$buildScript = Get-Content (Join-Path $root "scripts\build-windows.ps1") -Raw
$installScript = Get-Content (Join-Path $root "scripts\install.ps1") -Raw
$workflow = Get-Content (Join-Path $root ".github\workflows\windows-x64-build.yml") -Raw
$quickWorkflow = Get-Content (Join-Path $root ".github\workflows\windows-quick-preflight.yml") -Raw

if ($desktopProject -match '<Version>' -or $installerProject -match '<Version>') {
    throw "项目文件不得重复声明版本；只允许 Directory.Build.props 定义 PupuVersion。"
}
if ($installerProgram -match 'const\s+string\s+Version\s*=') {
    throw "安装器不得硬编码版本常量。"
}
if ($installerAssembly -match 'Assembly(?:File|Informational)?Version') {
    throw "AssemblyInfo.cs 不得重复声明版本特性。"
}
if ($buildScript -notmatch 'get-pupu-version\.ps1' -or
    $installScript -notmatch 'get-pupu-version\.ps1') {
    throw "Windows 构建和安装脚本必须从统一版本文件读取版本。"
}
if ($workflow -notmatch 'get-pupu-version\.ps1' -or
    $workflow -match 'Pupu-(?:Windows|Setup)-x64-\d+\.\d+\.\d+') {
    throw "Windows CI 的产物名称必须从统一版本文件读取版本。"
}
if ($buildScript -notmatch 'run-windows-smoke\.ps1') {
    throw "Windows 发布必须启动刚生成的 Pupu.exe 执行桌面冒烟测试。"
}
if ($quickWorkflow -notmatch 'Pupu\.Desktop\.IntegrationTests') {
    throw "Windows 快速预检必须运行聊天端到端模拟 API 测试。"
}

Write-Host "发布版本契约通过：Pupu $version 由 Directory.Build.props 唯一提供。" -ForegroundColor Green
