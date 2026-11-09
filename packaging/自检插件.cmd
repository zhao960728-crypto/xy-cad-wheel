@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0self-test.ps1"
set "result=%errorlevel%"
pause
exit /b %result%
