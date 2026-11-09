@echo off
setlocal
chcp 936 >nul
fltmc >nul 2>&1
if not "%errorlevel%"=="0" (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
title 小阳ZWCAD轮盘插件一键安装
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
set "code=%errorlevel%"
if not "%code%"=="0" echo.
if not "%code%"=="0" echo 安装未完成，请查看上方错误信息。
echo.
pause
exit /b %code%
