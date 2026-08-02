@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-windows.ps1" -Runtime win-x64
if errorlevel 1 (
  echo.
  echo pupu 构建失败。请确认已经安装 .NET 8 SDK，并查看上方错误。
  pause
  exit /b 1
)
echo.
echo 构建完成：dist\Pupu-win-x64-1.11.0\Pupu.exe
echo 一键安装程序：dist\Pupu-Setup-x64-1.11.0.exe
pause
