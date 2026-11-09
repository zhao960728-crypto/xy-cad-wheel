@echo off
setlocal
title XiaoYang ZWCAD Wheel Update
set "XY_UPDATE_CMD=%~f0"
fltmc >nul 2>&1
if not "%errorlevel%"=="0" (
  powershell.exe -NoProfile -Command "Start-Process -FilePath $env:XY_UPDATE_CMD -Verb RunAs"
  exit /b
)
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Quiet
set "result=%errorlevel%"
if not "%result%"=="0" pause
exit /b %result%
