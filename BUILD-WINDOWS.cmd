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
for /f "usebackq delims=" %%V in (`powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\get-pupu-version.ps1"`) do set "PUPU_VERSION=%%V"
echo 构建完成：artifacts\Pupu-win-x64-%PUPU_VERSION%\Pupu.exe
echo 一键安装程序：artifacts\Pupu-Setup-x64-%PUPU_VERSION%.exe
pause
