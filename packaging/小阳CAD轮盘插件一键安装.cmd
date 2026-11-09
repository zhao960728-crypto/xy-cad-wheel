@echo off
setlocal
title XiaoYang CAD Radial Menu Plugin Installer
powershell.exe -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0installer-ui.ps1"
set "result=%errorlevel%"
if not "%result%"=="0" pause
exit /b %result%
