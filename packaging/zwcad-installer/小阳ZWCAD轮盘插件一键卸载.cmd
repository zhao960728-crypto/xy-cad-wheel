@echo off
setlocal
chcp 936 >nul
fltmc >nul 2>&1
if not "%errorlevel%"=="0" (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
title –°—ÙZWCAD¬÷≈Ã≤Âº˛“ªº¸–∂‘ÿ
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
set "code=%errorlevel%"
echo.
pause
exit /b %code%
