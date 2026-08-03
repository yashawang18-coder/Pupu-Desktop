param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [switch]$StartWithWindows
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$version = "1.11.2"
$source = Join-Path $root "dist\Pupu-$Runtime-$version"
$destination = Join-Path $env:LOCALAPPDATA "Programs\Pupu"
$exe = Join-Path $destination "Pupu.exe"

if (-not (Test-Path (Join-Path $source "Pupu.exe"))) {
    throw "找不到构建结果。请先运行 .\scripts\build-windows.ps1 -Runtime $Runtime"
}

New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -Path (Join-Path $source "*") -Destination $destination -Recurse -Force

$shell = New-Object -ComObject WScript.Shell
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\pupu.lnk"
$shortcut = $shell.CreateShortcut($startMenu)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $destination
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = "pupu 桌面宠物"
$shortcut.Save()

if ($StartWithWindows) {
    $startup = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup\pupu.lnk"
    Copy-Item $startMenu $startup -Force
}

Start-Process $exe
Write-Host "pupu 已安装到 $destination" -ForegroundColor Green
