$ErrorActionPreference = "Stop"
$destination = Join-Path $env:LOCALAPPDATA "Programs\Pupu"
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\pupu.lnk"
$startup = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup\pupu.lnk"

Get-Process Pupu -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item $startMenu -Force -ErrorAction SilentlyContinue
Remove-Item $startup -Force -ErrorAction SilentlyContinue
Remove-Item $destination -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "应用已卸载。本地记忆仍保留在 %LOCALAPPDATA%\PupuDesktop。" -ForegroundColor Yellow
Write-Host "如需彻底删除记忆，请在确认备份后手动删除该目录。" -ForegroundColor Yellow
