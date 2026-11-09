@echo off
setlocal
title XiaoYang AutoCAD Wheel Update
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
set "result=%errorlevel%"
if not "%result%"=="0" pause
exit /b %result%
